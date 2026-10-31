using System.Text.Json;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Profiles.Application;
using Cocinaris.Modules.Profiles.Domain;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>
/// The quick chooser's answers for one request (PLAN-0021). Nothing here is stored: the answers
/// shape this list only and never change the profile.
/// </summary>
/// <param name="MealType"><c>breakfast</c>, <c>lunch</c>, <c>snack</c>, or <c>dinner</c>; null for any.</param>
/// <param name="MaxMinutes">Most total minutes the person has now (5–240); null to use the profile's ordinary time as a preference.</param>
/// <param name="Today">The person's local date, for "use soon".</param>
/// <param name="Limit">Recommendations to return (1–10).</param>
public sealed record RecipeRecommendationQuery(string? MealType, int? MaxMinutes, DateOnly Today, int Limit);

/// <summary>
/// One reason a recipe was recommended, as a stable code the interface translates:
/// <c>have_all</c>, <c>have_most</c>, <c>uses_soon</c> (with <see cref="Item"/>), <c>fits_time</c>,
/// <c>fits_meal</c>, <c>favorite</c>, or <c>not_recent</c>.
/// </summary>
/// <param name="Code">Reason code.</param>
/// <param name="Item">Pantry item name for <c>uses_soon</c>; null otherwise.</param>
public sealed record RecipeRecommendationReason(string Code, string? Item = null);

/// <summary>A recommended saved recipe with its score and reasons, best first.</summary>
/// <param name="RecipeId">Recipe.</param>
/// <param name="Score">Deterministic score from 0 to 100.</param>
/// <param name="TotalMinutes">Total active and passive minutes; null when unknown.</param>
/// <param name="Available">Ingredients the pantry fully covers.</param>
/// <param name="Total">Ingredients counted (basics and optional left out).</param>
/// <param name="Reasons">Why it was recommended, most important first.</param>
public sealed record RecipeRecommendationView(Guid RecipeId, int Score, int? TotalMinutes, int Available, int Total, IReadOnlyList<RecipeRecommendationReason> Reasons);

/// <summary>
/// "Para você agora" on the home page (PLAN-0021 confirmed-profile tier and quick chooser). It
/// ranks the owner's saved recipes deterministically, without any AI call, from:
/// <list type="bullet">
/// <item>pantry coverage, from the cooking proposal's matching (up to 50 points);</item>
/// <item>using a pantry item whose date is within two days (20);</item>
/// <item>fitting the time the person has, from the chooser or the profile's ordinary prep time (10);</item>
/// <item>fitting the chosen meal (10);</item>
/// <item>being a favorite (5);</item>
/// <item>not having been cooked in the last seven days (5), with 10 points off when cooked in the last two days.</item>
/// </list>
/// A time from the chooser is a hard limit; the profile's time is only a preference. Restrictions
/// are not checked here: saved recipes were written or chosen by the owner, and this list never
/// creates new ones. Read-only.
/// </summary>
public sealed class RecipeRecommendationService(
    ICurrentUserAccessor currentUser,
    IRecipeStore recipes,
    IRecipeLibraryStore library,
    IInventoryLotReadStore inventory,
    IPantryCommitmentLookup commitments,
    IIngredientMatching matching,
    IProfileReadStore profiles,
    TimeProvider timeProvider)
{
    private const int MaxRecipes = 200;
    private const int MaxPantryLots = 200;
    private const int UseSoonDays = 2;
    private static readonly HashSet<string> MealTypes = ["breakfast", "lunch", "snack", "dinner"];

    /// <summary>Recommends saved recipes for now.</summary>
    /// <param name="query">The chooser's answers and the person's local date.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<IReadOnlyList<RecipeRecommendationView>>> RecommendAsync(RecipeRecommendationQuery query, CancellationToken cancellationToken)
    {
        if (query.MealType is not null && !MealTypes.Contains(query.MealType))
        {
            return Failure("The meal must be breakfast, lunch, snack, or dinner.", "mealType");
        }

        if (query.MaxMinutes is < 5 or > 240)
        {
            return Failure("The time must be between 5 and 240 minutes.", "maxMinutes");
        }

        if (query.Limit is < 1 or > 10)
        {
            return Failure("Ask for one to ten recommendations.", "limit");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var active = await recipes.ListActiveAsync(user.Id, MaxRecipes, cancellationToken);
        if (active.Count == 0)
        {
            return RecipeApplicationResult<IReadOnlyList<RecipeRecommendationView>>.Succeeded([]);
        }

        var libraryItems = (await library.ListAsync(user.Id, new RecipeLibraryQuery(false, false, null), cancellationToken))
            .ToDictionary(item => item.RecipeId);
        var lots = await inventory.ListActiveAsync(user.Id, MaxPantryLots, cancellationToken);
        var lotById = lots.ToDictionary(lot => lot.LotId);
        var committed = await commitments.GetAsync(user.Id, cancellationToken);
        var keys = await matching.KeysAsync(
            user.Id,
            lots.Select(lot => lot.ProductName).Concat(active.SelectMany(recipe => RecipeIngredientReader.Read(recipe.NormalizedRecipeJson).Select(ingredient => ingredient.Name))),
            cancellationToken);
        var profile = await profiles.FindAsync(user.Id, cancellationToken);
        var preferredMinutes = profile?.Profile.OrdinaryPrepMinutesPresence == ProfileFieldPresence.Confirmed ? profile.Profile.OrdinaryPrepMinutes : null;
        var timeLimit = query.MaxMinutes ?? preferredMinutes;
        var now = timeProvider.GetUtcNow();

        var views = new List<RecipeRecommendationView>();
        foreach (var recipe in active)
        {
            var minutes = TotalMinutes(recipe.NormalizedRecipeJson);
            // A time chosen now is a hard limit; the profile's ordinary time only scores.
            if (query.MaxMinutes is { } hardLimit && minutes is { } total && total > hardLimit)
            {
                continue;
            }

            var assumed = RecipeIngredientReader.Read(recipe.NormalizedRecipeJson)
                .Where(ingredient => ingredient.SourceType == "assumption")
                .Select(ingredient => ingredient.Id)
                .ToHashSet(StringComparer.Ordinal);
            var lines = RecipeExecutionApplicationService.BuildProposal(recipe.NormalizedRecipeJson, recipe.Servings, recipe.Servings, lots, committed, matchKeys: keys).Lines
                .Where(line => !assumed.Contains(line.IngredientId) && !line.Optional)
                .ToList();
            var available = lines.Count(line => line.Status == "ready");
            var reasons = new List<RecipeRecommendationReason>();
            double score = 0;

            if (lines.Count > 0)
            {
                score += 50.0 * available / lines.Count;
                if (available == lines.Count)
                {
                    reasons.Add(new("have_all"));
                }
                else if (available * 2 >= lines.Count)
                {
                    reasons.Add(new("have_most"));
                }
            }

            var soon = lines
                .Where(line => line.LotId is { } lotId && (line.ProposedQuantity ?? 0) > 0 && lotById.TryGetValue(lotId, out var lot) && UsesSoon(lot, query.Today))
                .Select(line => lotById[line.LotId!.Value].ProductName)
                .FirstOrDefault();
            if (soon is not null)
            {
                score += 20;
                reasons.Insert(0, new("uses_soon", soon));
            }

            if (timeLimit is { } limit && minutes is { } recipeMinutes && recipeMinutes <= limit)
            {
                score += 10;
                reasons.Add(new("fits_time"));
            }

            if (query.MealType is { } meal && recipe.MealTypes.Contains(meal))
            {
                score += 10;
                reasons.Add(new("fits_meal"));
            }

            if (libraryItems.TryGetValue(recipe.RecipeId, out var item))
            {
                if (item.IsFavorite)
                {
                    score += 5;
                    reasons.Add(new("favorite"));
                }

                if (item.LastCookedAt is null || now - item.LastCookedAt.Value > TimeSpan.FromDays(7))
                {
                    score += 5;
                    if (item.LastCookedAt is not null)
                    {
                        reasons.Add(new("not_recent"));
                    }
                }
                else if (now - item.LastCookedAt.Value < TimeSpan.FromDays(2))
                {
                    score -= 10;
                }
            }

            views.Add(new RecipeRecommendationView(recipe.RecipeId, (int)Math.Clamp(Math.Round(score), 0, 100), minutes, available, lines.Count, reasons));
        }

        return RecipeApplicationResult<IReadOnlyList<RecipeRecommendationView>>.Succeeded(views
            .OrderByDescending(view => view.Score)
            .ThenByDescending(view => view.Available)
            .ThenBy(view => view.TotalMinutes ?? int.MaxValue)
            .Take(query.Limit)
            .ToList());
    }

    /// <summary>Total active and passive minutes of a revision; null when no stage states a time.</summary>
    internal static int? TotalMinutes(string normalizedRecipeJson)
    {
        try
        {
            using var document = JsonDocument.Parse(normalizedRecipeJson);
            var root = document.RootElement.TryGetProperty("recipe", out var inner) ? inner : document.RootElement;
            if (!root.TryGetProperty("stages", out var stages) || stages.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            int? total = null;
            foreach (var stage in stages.EnumerateArray())
            {
                foreach (var name in new[] { "activeMinutes", "passiveMinutes" })
                {
                    if (stage.TryGetProperty(name, out var value) && value.TryGetInt32(out var minutes))
                    {
                        total = (total ?? 0) + minutes;
                    }
                }
            }

            return total;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Same evidence as the pantry's attention: the printed date or the prepared shelf-life date.
    private static bool UsesSoon(InventoryLotReadModel lot, DateOnly today)
    {
        var dates = new[] { lot.PrintedExpirationDate, lot.Lifecycle?.PreparedShelfLifeDate }.OfType<DateOnly>().ToList();
        return dates.Count > 0 && dates.Min() <= today.AddDays(UseSoonDays);
    }

    private static RecipeApplicationResult<IReadOnlyList<RecipeRecommendationView>> Failure(string detail, string field) =>
        RecipeApplicationResult<IReadOnlyList<RecipeRecommendationView>>.Failure("validation_failed", detail, new Dictionary<string, string[]> { [field] = [detail] });
}
