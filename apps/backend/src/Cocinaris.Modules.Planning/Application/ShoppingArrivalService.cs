using System.Security.Cryptography;
using System.Text;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Profiles.Application;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>One purchased line and what to do with it.</summary>
/// <param name="ItemKey">Shopping line.</param>
/// <param name="DisplayName">Name shown; the lot takes this name, so the list recalculates.</param>
/// <param name="Unit">Canonical unit.</param>
/// <param name="StoreSection">Store section.</param>
/// <param name="Checked">Whether the line was checked off.</param>
/// <param name="BoughtQuantity">Quantity taken as bought; editable.</param>
/// <param name="NeedsRealQuantity">Whether to ask how much came: perishable, with no confirmed package size.</param>
/// <param name="PackageSize">Confirmed package size, if any.</param>
/// <param name="RequiredQuantity">What is left to buy, to compare with the amount bought.</param>
/// <param name="Portions">How to store it.</param>
public sealed record ArrivalLineView(string ItemKey, string DisplayName, string Unit, string StoreSection, bool Checked, decimal BoughtQuantity, bool NeedsRealQuantity, decimal? PackageSize, decimal RequiredQuantity, IReadOnlyList<ArrivalPortion> Portions);

/// <summary>What arrival proposes.</summary>
/// <param name="MenuId">Menu.</param>
/// <param name="GuidanceEnabled">Whether curated storage guidance is active. When false, nothing mentions freezing or dates.</param>
/// <param name="ContainerCount">Containers or bags to set aside.</param>
/// <param name="Lines">Lines to store.</param>
public sealed record ArrivalPlanView(Guid MenuId, bool GuidanceEnabled, int ContainerCount, IReadOnlyList<ArrivalLineView> Lines);

/// <summary>A quantity the person corrected.</summary>
/// <param name="ItemKey">Shopping line.</param>
/// <param name="BoughtQuantity">Quantity bought.</param>
public sealed record ArrivalQuantity(string ItemKey, decimal BoughtQuantity);

/// <summary>A portion to record.</summary>
/// <param name="Location"><c>Pantry</c>, <c>Refrigerator</c>, or <c>Freezer</c>.</param>
/// <param name="Quantity">Quantity of one portion.</param>
/// <param name="Count">Portions of this size.</param>
/// <param name="Notes">Note for the lot, such as what it is for.</param>
public sealed record ArrivalRecordPortion(string Location, decimal Quantity, int Count, string? Notes);

/// <summary>A line to record.</summary>
/// <param name="ItemKey">Shopping line.</param>
/// <param name="DisplayName">Product name.</param>
/// <param name="Unit">Canonical unit.</param>
/// <param name="Portions">Portions.</param>
public sealed record ArrivalRecordLine(string ItemKey, string DisplayName, string Unit, IReadOnlyList<ArrivalRecordPortion> Portions);

/// <summary>A lot that could not be recorded.</summary>
/// <param name="ItemKey">Shopping line.</param>
/// <param name="ErrorCode">Stable reason.</param>
public sealed record ArrivalFailure(string ItemKey, string ErrorCode);

/// <summary>Result of recording arrival. Lots are not one transaction; a retry with the same key replays what was recorded.</summary>
/// <param name="Recorded">Lots recorded or replayed.</param>
/// <param name="Failed">Lots that failed.</param>
public sealed record ArrivalResultView(int Recorded, IReadOnlyList<ArrivalFailure> Failed);

/// <summary>
/// "Cheguei do mercado" (PLAN-0062): where each purchase goes and how to divide it, recorded with one
/// confirmation through the normal inventory commands. Reservations are derived, so new lots are
/// reserved on the next read and nothing here writes them.
/// </summary>
public sealed class ShoppingArrivalService(
    ICurrentUserAccessor currentUser,
    MenuPlanningApplicationService planning,
    IProfileReadStore profiles,
    IIngredientMatching matching,
    StorageGuidance guidance,
    ICreateInventoryLotUseCase createLot,
    ITransitionInventoryLotUseCase transitionLot)
{
    /// <summary>Most lots one request records.</summary>
    public const int MaxLots = 120;

    private static readonly HashSet<string> ToBuy = new(StringComparer.Ordinal) { "required", "assumed", "optional" };
    private static readonly HashSet<string> Locations = new(StringComparer.Ordinal) { "Pantry", "Refrigerator", "Freezer" };

    /// <summary>Proposes storage for the checked lines, or every line to buy when none is checked.</summary>
    /// <param name="menuId">Menu.</param>
    /// <param name="from">The owner's day.</param>
    /// <param name="quantities">Quantities the person corrected; none for the first proposal.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="allLines">Proposes for every line to buy, so the list can show a hint before anything is checked.</param>
    public async Task<RecipeApplicationResult<ArrivalPlanView>> ProposeAsync(Guid menuId, DateOnly? from, IReadOnlyList<ArrivalQuantity>? quantities, CancellationToken cancellationToken, bool allLines = false)
    {
        if (from is not { } today)
        {
            return RecipeApplicationResult<ArrivalPlanView>.Failure("validation_failed", "The owner's current day is required.");
        }

        if (quantities?.Any(item => item.BoughtQuantity is <= 0 or > 100_000m) == true)
        {
            return RecipeApplicationResult<ArrivalPlanView>.Failure("validation_failed", "A quantity must be greater than zero and at most 100,000.");
        }

        var list = await planning.GetShoppingAsync(menuId, today, cancellationToken);
        if (list.Problem is { } problem)
        {
            return RecipeApplicationResult<ArrivalPlanView>.Failure(problem.ErrorCode, problem.Detail, problem.Errors);
        }

        var menu = await planning.GetAsync(menuId, cancellationToken);
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var profile = await profiles.FindAsync(user.Id, cancellationToken);
        // An empty equipment list says nothing about the freezer; only a list without one rules it out.
        var equipment = profile?.Equipment.Where(item => !item.IsRemoved).ToList() ?? [];
        var hasFreezer = equipment.Count == 0 || equipment.Any(item => item.StableCode.Value == "freezer");
        var corrected = (quantities ?? []).ToDictionary(item => item.ItemKey, item => item.BoughtQuantity, StringComparer.Ordinal);
        var toBuy = list.Value!.Items.Where(item => ToBuy.Contains(item.Category) && item.ToBuyQuantity > 0 && !item.Later).ToList();
        var lines = !allLines && toBuy.Any(item => item.Checked) ? toBuy.Where(item => item.Checked) : toBuy;
        var keys = await matching.KeysAsync(user.Id, lines.Select(item => item.DisplayName), cancellationToken);
        var order = (menu.Value?.Entries ?? []).Select((entry, index) => (entry.EntryId, index)).ToDictionary(item => item.EntryId, item => item.index);
        var start = menu.Value?.StartDate ?? today;
        var days = Math.Max(1, (menu.Value?.EndDate.DayNumber ?? today.DayNumber) - start.DayNumber + 1);
        var meals = Math.Max(1, menu.Value?.Entries.Count ?? 1);
        var views = new List<ArrivalLineView>();
        foreach (var line in lines)
        {
            var item = IngredientPerishability.ItemOfKey(keys.Key(line.DisplayName), IngredientCatalog.Default);
            var perishable = IngredientPerishability.IsPerishable(item);
            var bought = corrected.TryGetValue(line.ItemKey, out var real)
                ? real
                : line.PackageCount is { } packages && line.PackageSize is { } size ? packages * size : line.ToBuyQuantity;
            var uses = line.Uses.Where(use => use.MissingQuantity > 0)
                .Select(use => new ArrivalUse(use.RecipeName, use.MissingQuantity, use.PlannedDate ?? start.AddDays(order.GetValueOrDefault(use.EntryId) * days / meals)))
                .ToList();
            var portions = ArrivalAdvisor.Advise(bought, line.Unit, line.StoreSection, guidance.RuleFor(item), perishable, hasFreezer, today, uses);
            views.Add(new ArrivalLineView(line.ItemKey, line.DisplayName, line.Unit, line.StoreSection, line.Checked, bought, perishable && line.PackageSize is null, line.PackageSize, line.ToBuyQuantity, portions));
        }

        var containers = views.Sum(line => line.Portions.Sum(portion => portion.Count) > 1 || line.Portions.Any(portion => portion.Location == "Freezer") ? line.Portions.Sum(portion => portion.Count) : 0);
        return RecipeApplicationResult<ArrivalPlanView>.Succeeded(new ArrivalPlanView(menuId, guidance.Enabled, containers, views));
    }

    /// <summary>
    /// Records one lot per portion. A freezer portion is created in the refrigerator and then frozen, so
    /// its state is <c>Frozen</c> with a date: a lot created in the freezer would count as bought frozen.
    /// </summary>
    /// <param name="key">Request key; each lot's key derives from it, so a retry never counts twice.</param>
    /// <param name="lines">Lines to record.</param>
    /// <param name="correlationId">Request correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ArrivalResultView>> RecordAsync(Guid? key, IReadOnlyList<ArrivalRecordLine>? lines, string correlationId, CancellationToken cancellationToken)
    {
        if (key is not Guid requestKey)
        {
            return RecipeApplicationResult<ArrivalResultView>.Failure("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        var items = lines ?? [];
        if (items.Count == 0 || items.Sum(line => line.Portions.Sum(portion => portion.Count)) > MaxLots
            || items.Any(line => line.Portions.Any(portion => !Locations.Contains(portion.Location) || portion.Count is < 1 or > MaxLots || portion.Quantity <= 0)))
        {
            return RecipeApplicationResult<ArrivalResultView>.Failure("validation_failed", $"Record between 1 and {MaxLots} portions, each with a known place and a positive quantity.");
        }

        var recorded = 0;
        var failed = new List<ArrivalFailure>();
        foreach (var line in items)
        {
            var lotIndex = 0;
            foreach (var portion in line.Portions)
            {
                for (var copy = 0; copy < portion.Count; copy++, lotIndex++)
                {
                    var freezer = portion.Location == "Freezer";
                    var created = await createLot.CreateAsync(
                        new CreateInventoryLotCommand(line.DisplayName, portion.Quantity, line.Unit, null, freezer ? "Refrigerator" : portion.Location, null, null, null, portion.Notes, DerivedKey(requestKey, line.ItemKey, lotIndex, "lot"), correlationId),
                        cancellationToken);
                    if (created.Problem is { } problem)
                    {
                        failed.Add(new ArrivalFailure(line.ItemKey, problem.ErrorCode));
                        continue;
                    }

                    if (freezer)
                    {
                        var frozen = await transitionLot.TransitionAsync(
                            new TransitionInventoryLotCommand(created.Value!.LotId, "Freeze", null, null, null, null, DerivedKey(requestKey, line.ItemKey, lotIndex, "freeze"), InventoryVersionPrecondition.Valid(created.Value.ConcurrencyToken), correlationId),
                            cancellationToken);
                        if (frozen.Problem is { } freezeProblem)
                        {
                            failed.Add(new ArrivalFailure(line.ItemKey, freezeProblem.ErrorCode));
                            continue;
                        }
                    }

                    recorded++;
                }
            }
        }

        return RecipeApplicationResult<ArrivalResultView>.Succeeded(new ArrivalResultView(recorded, failed));
    }

    private static Guid DerivedKey(Guid key, string itemKey, int index, string purpose) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{key:n}:{itemKey}:{index}:{purpose}")).AsSpan(0, 16));
}
