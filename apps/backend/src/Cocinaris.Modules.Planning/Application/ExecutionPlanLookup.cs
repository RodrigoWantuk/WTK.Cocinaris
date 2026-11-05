using System.Text.Json;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>Planning's answer to what a menu entry makes when it is cooked (PLAN-0062). Owner-scoped and read-only.</summary>
public sealed class ExecutionPlanLookup(IMenuPlanStore menus, IRecipeStore recipes, IIngredientMatching matching, StorageGuidance guidance) : IExecutionPlanLookup
{
    /// <inheritdoc />
    public async Task<ExecutionPlanContext?> FindAsync(Guid ownerUserId, Guid menuEntryId, int servings, string normalizedRecipeJson, CancellationToken cancellationToken)
    {
        var entry = await menus.FindEntryAsync(ownerUserId, menuEntryId, cancellationToken);
        if (entry is null || (entry.Kind == MenuEntryKind.Meal && entry.MealsCovered < 2))
        {
            return null;
        }

        var plan = await menus.FindAsync(ownerUserId, entry.MenuPlanId, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var entries = (await menus.ListEntriesAsync(ownerUserId, [plan.Id], cancellationToken)).Where(item => item.Status == MenuEntryStatus.Planned).ToList();
        var days = plan.EndDate.DayNumber - plan.StartDate.DayNumber + 1;
        var meals = Math.Max(1, entries.Where(item => item.Kind == MenuEntryKind.Meal).Sum(item => item.MealsCovered));
        if (entry.Production is not { } production)
        {
            var later = Enumerable.Range(1, entry.MealsCovered - 1).Select(index => index * days / meals).ToList();
            return new ExecutionPlanContext(false, null, null, null, StoredOutputAdvisor.ForDish(servings, entry.MealsCovered, later, FreezeCompatible(normalizedRecipeJson), guidance.CookedFridgeDays));
        }

        var active = (await recipes.ListActiveAsync(ownerUserId, 200, cancellationToken)).ToDictionary(item => item.RecipeId);
        var names = entries.Where(item => active.ContainsKey(item.RecipeId)).SelectMany(item => RecipeIngredientReader.Read(active[item.RecipeId].NormalizedRecipeJson).Select(ingredient => ingredient.Name)).Append(production.Name);
        var keys = await matching.KeysAsync(ownerUserId, names, cancellationToken);
        var key = keys.Key(production.Name);
        var position = 0;
        var uses = new List<OutputUse>();
        foreach (var other in entries.Where(item => item.Kind == MenuEntryKind.Meal).OrderBy(item => item.SuggestedOrder ?? int.MaxValue).ThenBy(item => item.CreatedAt))
        {
            var inDays = other.PlannedDate is { } date ? Math.Max(0, date.DayNumber - (entry.PlannedDate ?? plan.StartDate).DayNumber) : position * days / meals;
            position += other.MealsCovered;
            if (!active.TryGetValue(other.RecipeId, out var detail))
            {
                continue;
            }

            var scale = (decimal)other.Servings / Math.Max(1, detail.Servings);
            var need = RecipeIngredientReader.Read(detail.NormalizedRecipeJson)
                .Where(item => keys.Key(item.Name) == key && RecipeIngredientReader.ToLotUnit(item.Unit) == production.Unit)
                .Sum(item => RecipeIngredientReader.Scale(item.Quantity, production.Unit, scale));
            if (need > 0)
            {
                uses.Add(new OutputUse(need, inDays));
            }
        }

        return new ExecutionPlanContext(true, production.Name, production.Quantity, production.Unit, StoredOutputAdvisor.ForComponent(production.Quantity, production.Unit, uses, guidance.RuleForName(production.Name)));
    }

    private static bool FreezeCompatible(string normalizedRecipeJson)
    {
        try
        {
            using var document = JsonDocument.Parse(normalizedRecipeJson);
            var root = document.RootElement;
            if (root.TryGetProperty("recipe", out var recipe))
            {
                root = recipe;
            }

            return root.TryGetProperty("storage", out var storage) && storage.TryGetProperty("freezeCompatible", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
