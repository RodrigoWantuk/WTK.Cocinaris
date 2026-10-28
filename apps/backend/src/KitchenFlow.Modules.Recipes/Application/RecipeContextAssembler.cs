using System.Text.Json;
using KitchenFlow.Modules.Inventory.Application;
using KitchenFlow.Modules.Profiles.Application;
using KitchenFlow.Modules.Profiles.Domain;
using KitchenFlow.Modules.Recipes.Ai;

namespace KitchenFlow.Modules.Recipes.Application;

/// <summary>
/// Default <see cref="IRecipeContextAssembler"/> that reads owner-scoped inventory lots and profile
/// state through the Inventory and Profiles modules' own application read boundaries. It never
/// queries persistence directly; it depends only on the read ports those modules already expose.
/// </summary>
public sealed class RecipeContextAssembler(IInventoryLotReadStore inventoryReadStore, IProfileReadStore profileReadStore) : IRecipeContextAssembler
{
    /// <summary>Maximum number of on-hand inventory items considered per request before budget-based truncation.</summary>
    private const int MaxInventoryItemsConsidered = 60;

    /// <summary>Maximum number of active equipment items considered per request.</summary>
    private const int MaxEquipmentItemsConsidered = 30;

    /// <summary>Default servings used when the owner has not declared a household serving default.</summary>
    private const int DefaultServings = 2;

    /// <inheritdoc />
    public async Task<RecipeSuggestRequestContext> AssembleSuggestContextAsync(Guid ownerUserId, string requestId, int contextBudgetCharacters, CancellationToken cancellationToken)
    {
        var (inventoryItems, equipment, presets) = await AssembleSharedAsync(ownerUserId, cancellationToken);
        var bounded = BoundBySuggestBudget(inventoryItems, equipment, contextBudgetCharacters);
        return new RecipeSuggestRequestContext(requestId, bounded.InventoryItems, bounded.Equipment, presets, new RecipeExecutionContext("cook_now", AvailableLeadMinutes: null));
    }

    /// <inheritdoc />
    public async Task<RecipeExpandRequestContext> AssembleExpandContextAsync(Guid ownerUserId, string requestId, SuggestCandidate selectedCandidate, int contextBudgetCharacters, CancellationToken cancellationToken)
    {
        var (inventoryItems, equipment, presets) = await AssembleSharedAsync(ownerUserId, cancellationToken);
        var bounded = BoundBySuggestBudget(inventoryItems, equipment, contextBudgetCharacters);
        var selected = ToSelectedCandidateContext(selectedCandidate);
        return new RecipeExpandRequestContext(requestId, bounded.InventoryItems, bounded.Equipment, presets, selected);
    }

    private async Task<(IReadOnlyList<RecipeInventoryContextItem> InventoryItems, IReadOnlyList<RecipeEquipmentContextItem> Equipment, RecipeUserPresets Presets)> AssembleSharedAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var lotsPage = await inventoryReadStore.ListAsync(new InventoryLotReadQuery(ownerUserId, MaxInventoryItemsConsidered, "active", null, null, null), cancellationToken);
        // v1 cook-now context is bounded to measured on-hand lots; qualitative availability-only
        // lots carry no usable quantity for candidate generation and are intentionally omitted.
        var inventoryItems = lotsPage.Items
            .Where(lot => lot.MeasuredValue is not null && lot.MeasuredUnit is not null)
            .Select(lot => new RecipeInventoryContextItem(lot.LotId.ToString("n"), lot.ProductName, lot.MeasuredValue!.Value, ToCanonicalUnit(lot.MeasuredUnit!), "on_hand", null))
            .ToList();

        var profile = await profileReadStore.FindAsync(ownerUserId, cancellationToken);
        var equipment = profile?.Equipment
            .Where(item => !item.IsRemoved)
            .Take(MaxEquipmentItemsConsidered)
            .Select(item => new RecipeEquipmentContextItem(item.Id.ToString("n"), item.CustomName ?? item.StableCode.Value, []))
            .ToList()
            ?? [];

        var presets = BuildPresets(profile);
        return (inventoryItems, equipment, presets);
    }

    private static RecipeUserPresets BuildPresets(ProfileReadModel? profile)
    {
        if (profile is null)
        {
            return new RecipeUserPresets(DefaultServings, [], [], []);
        }

        var confirmed = profile.Preferences.Where(item => item.Presence == ProfileFieldPresence.Confirmed).ToList();
        var preferences = confirmed.Where(item => item.Category == PreferenceCategory.Preference).Select(item => item.StableCode.Value).ToList();
        var restrictions = confirmed.Where(item => item.Category != PreferenceCategory.Preference).Select(item => item.StableCode.Value).ToList();
        var servings = profile.Profile.DefaultServingCount ?? DefaultServings;
        return new RecipeUserPresets(servings, preferences, restrictions, []);
    }

    private static (IReadOnlyList<RecipeInventoryContextItem> InventoryItems, IReadOnlyList<RecipeEquipmentContextItem> Equipment) BoundBySuggestBudget(
        IReadOnlyList<RecipeInventoryContextItem> inventoryItems, IReadOnlyList<RecipeEquipmentContextItem> equipment, int contextBudgetCharacters)
    {
        var items = inventoryItems;
        var equipmentItems = equipment;
        while (EstimateSerializedLength(items, equipmentItems) > contextBudgetCharacters && items.Count > 1)
        {
            items = items.Take(items.Count / 2).ToList();
        }

        while (EstimateSerializedLength(items, equipmentItems) > contextBudgetCharacters && equipmentItems.Count > 1)
        {
            equipmentItems = equipmentItems.Take(equipmentItems.Count / 2).ToList();
        }

        return (items, equipmentItems);
    }

    private static int EstimateSerializedLength(IReadOnlyList<RecipeInventoryContextItem> inventoryItems, IReadOnlyList<RecipeEquipmentContextItem> equipment) =>
        JsonSerializer.Serialize(inventoryItems).Length + JsonSerializer.Serialize(equipment).Length;

    private static RecipeSelectedCandidateContext ToSelectedCandidateContext(SuggestCandidate candidate) => new(
        candidate.CandidateId,
        candidate.Name,
        [candidate.TargetMealType],
        candidate.Servings,
        candidate.RequiredEquipmentIds,
        candidate.InventoryUses.Select(use => new RecipeInventoryContextItem(use.InventoryItemId, use.UserName, use.RequiredQuantity, use.Unit, use.AvailabilitySource, use.IngredientRef)).ToList(),
        candidate.AdditionalIngredients.Select(item => (item.Name, item.RequiredQuantity, item.Unit, item.Optional)).ToList());

    private static string ToCanonicalUnit(string measuredUnit) => measuredUnit switch
    {
        "Gram" => "g",
        "Milliliter" => "ml",
        "Unit" => "unit",
        _ => "unit"
    };
}
