using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Profiles.Application;
using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.Modules.Recipes.Ideas;

/// <summary>
/// The shared recipe-idea library (PLAN-0055 Phase 2, ADR-0011). It keeps generic versions of
/// recipes the AI already wrote, and it answers later suggestion requests and selections from them
/// without calling the AI when they fit the person well.
/// </summary>
/// <remarks>
/// <para><b>Capture.</b> After an AI expansion, the recipe is kept only when it can be made
/// generic: every ingredient resolves to a catalog ingredient, every equipment item has a stable
/// code, and no word the owner typed (a product or equipment name outside the catalog) or private
/// context appears in its text. Names become catalog names, and inventory and equipment ids are
/// removed.</para>
/// <para><b>Safety.</b> An idea carries the restriction codes it was generated under, and it is
/// served only to someone whose restrictions are all among them. The rule is conservative, needs no
/// allergen data, and never relies on the model.</para>
/// <para><b>Serving.</b> Deterministic code rebuilds each idea for the person: their pantry lots
/// (by catalog ingredient and unit), their equipment ids, and their names. The result must pass the
/// same protocol validation as AI output against the person's own request. Anything that does not
/// fit falls back to the AI, so the library can only remove calls, never change a decision.</para>
/// </remarks>
public sealed class RecipeIdeaLibrary(
    IRecipeIdeaStore store,
    IIngredientMatching matching,
    IngredientCatalog catalog,
    IProfileReadStore profiles,
    RecipeIdeaOptions options)
{
    /// <summary>Candidate id prefix of suggestions served from the library.</summary>
    public const string CandidatePrefix = "idea-";

    private const string IngredientKeyPrefix = "ingredient:";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, IngredientCatalogItem> _bySlug = catalog.Items.ToDictionary(item => item.Slug, StringComparer.Ordinal);

    /// <summary>Random source for the novelty share; replaceable in tests.</summary>
    public Func<double> NextRandom { get; init; } = Random.Shared.NextDouble;

    /// <summary>Whether a candidate id names a library idea, and which.</summary>
    public static bool TryParseCandidateId(string candidateId, out Guid ideaId)
    {
        ideaId = Guid.Empty;
        return candidateId.StartsWith(CandidatePrefix, StringComparison.Ordinal) && Guid.TryParseExact(candidateId[CandidatePrefix.Length..], "n", out ideaId);
    }

    /// <summary>
    /// Keeps a generic copy of an AI-expanded recipe when it can be made generic without private
    /// content. Returns whether an idea was stored (or an identical one already existed).
    /// </summary>
    /// <param name="ownerUserId">Owner whose request produced the recipe; used only to read their names and equipment, never stored.</param>
    /// <param name="candidate">The suggestion the owner chose.</param>
    /// <param name="recipe">The validated expanded recipe.</param>
    /// <param name="context">The expansion context (presets, pantry, equipment).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> TryCaptureAsync(Guid ownerUserId, SuggestCandidate candidate, ExpandRecipe recipe, RecipeExpandRequestContext context, CancellationToken cancellationToken)
    {
        var presets = context.Presets;
        if (!options.Enabled || string.IsNullOrWhiteSpace(presets.OutputLanguage))
        {
            return false;
        }

        // Only the names this recipe uses: a pantry item it does not use cannot leak into its text.
        var typed = recipe.Ingredients.Select(item => item.DisplayName)
            .Concat(candidate.InventoryUses.Select(use => use.UserName))
            .Concat(candidate.AdditionalIngredients.Select(item => item.Name))
            .ToList();
        var keys = await matching.KeysAsync(ownerUserId, typed, cancellationToken);
        var slugs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ingredient in recipe.Ingredients)
        {
            if (Slug(keys, ingredient.DisplayName) is not { } slug)
            {
                return false;
            }

            slugs[ingredient.IngredientId] = slug;
        }

        // A name that is not a curated catalog name may be the owner's own words ("frango da vó");
        // the recipe is kept only when none of them appears in its text.
        var ownWords = typed.Select(IngredientText.Normalize).Where(name => name.Length >= 3 && !catalog.Aliases.ContainsKey(name)).ToHashSet(StringComparer.Ordinal);
        var profile = await profiles.FindAsync(ownerUserId, cancellationToken);
        var equipmentById = (profile?.Equipment ?? []).Where(item => !item.IsRemoved).ToDictionary(item => item.Id.ToString("n"), StringComparer.Ordinal);
        foreach (var item in equipmentById.Values.Where(item => !string.IsNullOrWhiteSpace(item.CustomName)))
        {
            ownWords.Add(IngredientText.Normalize(item.CustomName!));
        }

        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in recipe.Equipment.Select(item => item.EquipmentId).Concat(candidate.RequiredEquipmentIds))
        {
            if (!equipmentById.TryGetValue(id, out var item))
            {
                return false;
            }

            codes[id] = item.StableCode.Value;
        }

        var text = string.Join('\n', FreeText(candidate, recipe));
        var normalizedText = IngredientText.Normalize(text.Replace('\n', ' '));
        if (RecipeExpandSemantics.ContainsPrivateContext(text) || ownWords.Any(word => normalizedText.Contains(word, StringComparison.Ordinal)))
        {
            return false;
        }

        var language = LanguageOf(presets.OutputLanguage);
        var requiredStates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var use in candidate.InventoryUses)
        {
            if (Slug(keys, use.UserName) is { } slug)
            {
                requiredStates.TryAdd(slug, use.RequiredState);
            }
        }

        var genericRecipe = CopyRecipe(
            recipe,
            recipe.Ingredients.Select(item => CopyIngredient(item, item.SourceType == "inventory" ? "additional" : item.SourceType, CatalogName(slugs[item.IngredientId], language), null)).ToList(),
            recipe.Equipment.Select(item => new ExpandEquipment { EquipmentId = codes[item.EquipmentId], Name = GenericEquipmentName(item, equipmentById, codes), Capabilities = item.Capabilities }).DistinctBy(item => item.EquipmentId).ToList());
        var required = recipe.Ingredients.Where(item => !item.Optional && item.SourceType != "assumption").Select(item => slugs[item.IngredientId]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var primary = recipe.Ingredients.Where(item => item.SourceType != "assumption").OrderByDescending(item => item.RequiredQuantity).Select(item => slugs[item.IngredientId]).Distinct(StringComparer.Ordinal).Take(6).ToList();
        if (required.Count == 0 || primary.Count == 0)
        {
            return false;
        }

        var genericCandidate = CopyCandidate(candidate, "idea", primary, recipe.Servings, codes.Values.Distinct(StringComparer.Ordinal).ToList(), [], []);
        var restrictions = presets.Restrictions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var signature = Sha256(string.Join('\n',
            language, presets.InstructionDetail ?? "-", presets.CookingSkill ?? "-", string.Join(',', restrictions), candidate.TargetMealType,
            candidate.DishFormat, string.Join(',', slugs.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)), IngredientText.Normalize(recipe.Name)));
        var idea = new RecipeIdea(
            Guid.NewGuid(), signature, language, presets.InstructionDetail, presets.CookingSkill, restrictions, candidate.TargetMealType, candidate.CandidateStrategy,
            required, codes.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            new RecipeIdeaContent(genericCandidate, genericRecipe, slugs, requiredStates), 0, 0);
        await store.AddAsync(idea, DateTimeOffset.UtcNow, cancellationToken);
        return true;
    }

    /// <summary>
    /// Answers a suggestion request with three library ideas rebuilt for this person, or returns
    /// null so the caller asks the AI. The answer passed the same validation as AI output.
    /// </summary>
    /// <param name="ownerUserId">Requesting owner.</param>
    /// <param name="context">The suggestion context the AI would have received.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SuggestCandidatesResponse?> TrySuggestAsync(Guid ownerUserId, RecipeSuggestRequestContext context, CancellationToken cancellationToken)
    {
        var presets = context.Presets;
        // Ideas carry no meal-kind, family, or cuisine tags yet, so a request that asks for them goes to the AI.
        if (!options.Enabled || string.IsNullOrWhiteSpace(presets.OutputLanguage) || context.InventoryItems.Count == 0 || context.Guidance is { HasStylePreferences: true }
            || NextRandom() < options.NoveltyShare)
        {
            return null;
        }

        var mode = context.ExecutionContext.ExecutionMode;
        var keys = await matching.KeysAsync(ownerUserId, context.InventoryItems.Select(item => item.UserName), cancellationToken);
        var pantry = Pantry(context.InventoryItems, keys);
        if (pantry.Count == 0)
        {
            return null;
        }

        var equipment = await EquipmentAsync(ownerUserId, context.Equipment, cancellationToken);
        var query = new RecipeIdeaQuery(
            LanguageOf(presets.OutputLanguage), presets.InstructionDetail, presets.CookingSkill, presets.Restrictions.Distinct(StringComparer.Ordinal).ToList(),
            mode == "menu_planning" ? context.ExecutionContext.TargetMealType : null,
            pantry.Keys.Select(key => key.Slug).Distinct(StringComparer.Ordinal).ToList(), equipment.Keys.ToList(), options.CandidatePoolSize);
        var ideas = await store.FindCandidatesAsync(query, cancellationToken);
        if (ideas.Count < 3)
        {
            return null;
        }

        var owned = await store.OwnerRecipeNamesAsync(ownerUserId, cancellationToken);
        var ranked = ideas
            .Where(idea => !owned.Contains(IngredientText.Normalize(idea.Content.Recipe.Name)) && !owned.Contains(IngredientText.Normalize(idea.Content.Candidate.Name)))
            .Where(idea => StrategyAllowed(idea.Strategy, mode))
            .Select(idea => (Idea: idea, Coverage: Coverage(idea, pantry)))
            .Where(entry => entry.Coverage >= options.MinimumCoverage)
            .OrderByDescending(entry => 0.8 * entry.Coverage + 0.2 * (entry.Idea.TimesSelected + 1.0) / (entry.Idea.TimesOffered + 2.0))
            .ThenBy(entry => entry.Idea.Id)
            .ToList();

        // Three ideas with different strategies and different dishes, as the protocol requires of the AI.
        var chosen = new List<(RecipeIdea Idea, SuggestCandidate Candidate)>();
        var strategies = new HashSet<string>(StringComparer.Ordinal);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (idea, _) in ranked)
        {
            var generic = idea.Content.Candidate;
            var fingerprint = string.Join('|', new[] { generic.DishFormat, generic.PrimaryTechnique }.Concat(generic.PrimaryIngredientRefs));
            if (strategies.Contains(idea.Strategy) || fingerprints.Contains(fingerprint) || Rebuild(idea, pantry, equipment) is not { } candidate || !FitsGuidance(candidate, context))
            {
                continue;
            }

            chosen.Add((idea, candidate));
            strategies.Add(idea.Strategy);
            fingerprints.Add(fingerprint);
            if (chosen.Count == 3)
            {
                break;
            }
        }

        if (chosen.Count < 3)
        {
            return null;
        }

        var response = new SuggestCandidatesResponse
        {
            Operation = "recipe.suggest_candidates.v1",
            SchemaVersion = "0.3",
            Candidates = chosen.Select(item => item.Candidate).ToList(),
            Clarifications = []
        };
        using var request = JsonDocument.Parse(RecipeAiRequestEnvelopes.BuildSuggestRequest(context));
        var validation = RecipeProtocolValidator.ValidateSuggest(JsonSerializer.Serialize(response, SerializerOptions), request.RootElement);
        if (!validation.IsValid)
        {
            return null;
        }

        await store.MarkOfferedAsync(chosen.Select(item => item.Idea.Id).ToList(), cancellationToken);
        return validation.Response;
    }

    /// <summary>
    /// Builds the expanded recipe of a chosen library idea for this person without the AI, or
    /// returns null when the idea no longer fits (gone, written for another language or level, or
    /// their restrictions changed); the caller then expands the candidate with the AI.
    /// </summary>
    /// <param name="ownerUserId">Owner choosing the idea.</param>
    /// <param name="ideaId">Idea id from the candidate id.</param>
    /// <param name="context">The expansion context the AI would have received.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ExpandRecipe?> TryBuildRecipeAsync(Guid ownerUserId, Guid ideaId, RecipeExpandRequestContext context, CancellationToken cancellationToken)
    {
        var presets = context.Presets;
        if (!options.Enabled || string.IsNullOrWhiteSpace(presets.OutputLanguage) || await store.FindAsync(ideaId, cancellationToken) is not { } idea)
        {
            return null;
        }

        if (idea.Language != LanguageOf(presets.OutputLanguage) || idea.InstructionDetail != presets.InstructionDetail || idea.CookingSkill != presets.CookingSkill
            || !presets.Restrictions.All(code => idea.Restrictions.Contains(code, StringComparer.Ordinal)))
        {
            return null;
        }

        var keys = await matching.KeysAsync(ownerUserId, context.InventoryItems.Select(item => item.UserName), cancellationToken);
        var pantry = Pantry(context.InventoryItems, keys);
        var equipment = await EquipmentAsync(ownerUserId, context.Equipment, cancellationToken);
        var generic = idea.Content.Recipe;
        var ingredients = new List<ExpandIngredient>();
        foreach (var ingredient in generic.Ingredients)
        {
            var slug = idea.Content.IngredientSlugs[ingredient.IngredientId];
            ingredients.Add(ingredient.SourceType != "assumption" && pantry.TryGetValue((slug, ingredient.Unit), out var lots)
                ? CopyIngredient(ingredient, "inventory", lots[0].UserName, lots[0].ItemId)
                : ingredient);
        }

        var equipmentItems = new List<ExpandEquipment>();
        foreach (var item in generic.Equipment)
        {
            if (!equipment.TryGetValue(item.EquipmentId, out var owned))
            {
                return null;
            }

            equipmentItems.Add(new ExpandEquipment { EquipmentId = owned.EquipmentId, Name = owned.CustomName ?? item.Name, Capabilities = item.Capabilities });
        }

        var recipe = CopyRecipe(generic, ingredients, equipmentItems);
        var response = new ExpandSelectedResponse { Operation = "recipe.expand_selected.v1", SchemaVersion = "0.3", Recipe = recipe };
        using var request = JsonDocument.Parse(RecipeAiRequestEnvelopes.BuildExpandRequest(context));
        var validation = RecipeProtocolValidator.ValidateExpand(JsonSerializer.Serialize(response, SerializerOptions), request.RootElement);
        if (!validation.IsValid)
        {
            return null;
        }

        await store.MarkSelectedAsync(idea.Id, cancellationToken);
        return validation.Response!.Recipe;
    }

    private SuggestCandidate? Rebuild(RecipeIdea idea, IReadOnlyDictionary<(string Slug, string Unit), List<RecipeInventoryContextItem>> pantry, IReadOnlyDictionary<string, OwnedEquipment> equipment)
    {
        var uses = new List<SuggestInventoryUse>();
        var additional = new List<SuggestAdditionalIngredient>();
        foreach (var ingredient in idea.Content.Recipe.Ingredients.Where(item => item.SourceType != "assumption"))
        {
            var slug = idea.Content.IngredientSlugs[ingredient.IngredientId];
            if (pantry.TryGetValue((slug, ingredient.Unit), out var lots))
            {
                var lot = lots[0];
                uses.Add(new SuggestInventoryUse
                {
                    IngredientRef = lot.IngredientRef ?? ingredient.IngredientId,
                    InventoryItemId = lot.ItemId,
                    UserName = lot.UserName,
                    RequiredQuantity = ingredient.RequiredQuantity,
                    Unit = lot.Unit,
                    AvailabilitySource = lot.AvailabilitySource,
                    RequiredState = idea.Content.RequiredStates.TryGetValue(slug, out var state) ? state : "raw"
                });
            }
            else
            {
                additional.Add(new SuggestAdditionalIngredient { Name = ingredient.DisplayName, RequiredQuantity = ingredient.RequiredQuantity, Unit = ingredient.Unit, Optional = ingredient.Optional });
            }
        }

        if (uses.Count is 0 or > 10 || additional.Count > 5 || idea.EquipmentCodes.Any(code => !equipment.ContainsKey(code)))
        {
            return null;
        }

        var generic = idea.Content.Candidate;
        return CopyCandidate(generic, $"{CandidatePrefix}{idea.Id:n}", generic.PrimaryIngredientRefs, generic.Servings, generic.RequiredEquipmentIds.Select(code => equipment[code].EquipmentId).Distinct(StringComparer.Ordinal).ToList(), uses, additional);
    }

    /// <summary>Whether a rebuilt candidate uses every required lot and fits the time limit (PLAN-0056).</summary>
    private static bool FitsGuidance(SuggestCandidate candidate, RecipeSuggestRequestContext context)
    {
        var used = candidate.InventoryUses.Select(use => use.InventoryItemId).ToHashSet(StringComparer.Ordinal);
        return context.InventoryItems.Where(item => item.MustUse).All(item => used.Contains(item.ItemId))
            && (context.Guidance?.MaxTotalMinutes is not { } minutes || candidate.Time.TotalMinutes <= minutes);
    }

    /// <summary>Average share of each required ingredient the pantry covers, by catalog ingredient and unit.</summary>
    private static double Coverage(RecipeIdea idea, IReadOnlyDictionary<(string Slug, string Unit), List<RecipeInventoryContextItem>> pantry)
    {
        var required = idea.Content.Recipe.Ingredients.Where(item => !item.Optional && item.SourceType != "assumption" && item.RequiredQuantity > 0).ToList();
        if (required.Count == 0)
        {
            return 0;
        }

        return required.Average(item =>
        {
            var available = pantry.TryGetValue((idea.Content.IngredientSlugs[item.IngredientId], item.Unit), out var lots) ? lots.Sum(lot => lot.Quantity) : 0m;
            return (double)Math.Min(1m, available / item.RequiredQuantity);
        });
    }

    /// <summary>The person's measured lots by catalog ingredient and unit, the largest lot first.</summary>
    private static Dictionary<(string Slug, string Unit), List<RecipeInventoryContextItem>> Pantry(IReadOnlyList<RecipeInventoryContextItem> items, IngredientMatchKeys keys) =>
        items
            .Select(item => (Item: item, Slug: Slug(keys, item.UserName)))
            .Where(entry => entry.Slug is not null && entry.Item.Quantity > 0)
            .GroupBy(entry => (entry.Slug!, entry.Item.Unit))
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Item).OrderByDescending(item => item.Quantity).ToList());

    /// <summary>The person's equipment in the request, by stable code (first item of each code).</summary>
    private async Task<Dictionary<string, OwnedEquipment>> EquipmentAsync(Guid ownerUserId, IReadOnlyList<RecipeEquipmentContextItem> declared, CancellationToken cancellationToken)
    {
        var inRequest = declared.Select(item => item.EquipmentId).ToHashSet(StringComparer.Ordinal);
        var profile = await profiles.FindAsync(ownerUserId, cancellationToken);
        return (profile?.Equipment ?? [])
            .Where(item => !item.IsRemoved && inRequest.Contains(item.Id.ToString("n")))
            .GroupBy(item => item.StableCode.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new OwnedEquipment(group.First().Id.ToString("n"), group.First().CustomName), StringComparer.Ordinal);
    }

    private static string? Slug(IngredientMatchKeys keys, string name)
    {
        var key = keys.Key(name);
        return key.StartsWith(IngredientKeyPrefix, StringComparison.Ordinal) ? key[IngredientKeyPrefix.Length..] : null;
    }

    private static bool StrategyAllowed(string strategy, string mode) =>
        !(mode == "cook_now" && strategy == "planned_purchase_reuse") && !(mode == "menu_planning" && strategy == "on_hand_flexible");

    /// <summary>Normalized output language (lower case, for example <c>pt-br</c>); catalog names use its first two letters.</summary>
    private static string LanguageOf(string outputLanguage) => outputLanguage.Trim().ToLowerInvariant();

    private string CatalogName(string slug, string language)
    {
        var item = _bySlug[slug];
        var code = language.Length >= 2 ? language[..2] : language;
        return item.Names.TryGetValue(code, out var names) && names.Count > 0 ? names[0] : item.Names.Values.First(names => names.Count > 0)[0];
    }

    private static string GenericEquipmentName(ExpandEquipment item, IReadOnlyDictionary<string, Profiles.Domain.EquipmentEntry> owned, IReadOnlyDictionary<string, string> codes) =>
        owned.TryGetValue(item.EquipmentId, out var entry) && !string.IsNullOrWhiteSpace(entry.CustomName) ? codes[item.EquipmentId].Replace('_', ' ') : item.Name;

    private static IEnumerable<string> FreeText(SuggestCandidate candidate, ExpandRecipe recipe) =>
        new[] { candidate.Name, candidate.Summary, recipe.Name, recipe.Yield, recipe.Storage.ReheatNotes }
            .Concat(candidate.AssumptionsUsed)
            .Concat(recipe.Equipment.Select(item => item.Name))
            .Concat(recipe.Stages.SelectMany(stage => new[] { stage.Name, stage.Instructions }.Concat(stage.SensoryCues)))
            .Concat(recipe.Preparations.SelectMany(task => new[] { task.Name, task.Instructions }))
            .Concat(recipe.Assumptions)
            .Concat(recipe.ReconciliationHints)
            .Concat(recipe.ProducedComponents);

    private static ExpandIngredient CopyIngredient(ExpandIngredient source, string sourceType, string displayName, string? inventoryItemId) => new()
    {
        IngredientId = source.IngredientId,
        SourceType = sourceType,
        DisplayName = displayName,
        InventoryItemId = inventoryItemId,
        RequiredQuantity = source.RequiredQuantity,
        Unit = source.Unit,
        Optional = source.Optional
    };

    private static ExpandRecipe CopyRecipe(ExpandRecipe source, List<ExpandIngredient> ingredients, List<ExpandEquipment> equipment) => new()
    {
        RecipeId = source.RecipeId,
        Revision = source.Revision,
        Name = source.Name,
        MealTypes = source.MealTypes,
        Servings = source.Servings,
        Yield = source.Yield,
        Ingredients = ingredients,
        Equipment = equipment,
        Preparations = source.Preparations,
        Stages = source.Stages,
        Dependencies = source.Dependencies,
        Storage = source.Storage,
        ProducedComponents = source.ProducedComponents,
        ReconciliationHints = source.ReconciliationHints,
        Assumptions = source.Assumptions,
        ThumbnailVisual = source.ThumbnailVisual
    };

    private static SuggestCandidate CopyCandidate(SuggestCandidate source, string candidateId, List<string> primaryRefs, int servings, List<string> equipmentIds, List<SuggestInventoryUse> uses, List<SuggestAdditionalIngredient> additional) => new()
    {
        CandidateId = candidateId,
        CandidateStrategy = source.CandidateStrategy,
        Name = source.Name,
        TargetMealType = source.TargetMealType,
        DishFormat = source.DishFormat,
        PrimaryTechnique = source.PrimaryTechnique,
        PrimaryIngredientRefs = primaryRefs,
        Summary = source.Summary,
        Servings = servings,
        Time = source.Time,
        Difficulty = source.Difficulty,
        RequiredEquipmentIds = equipmentIds,
        RequiredCapabilities = source.RequiredCapabilities,
        InventoryUses = uses,
        AdditionalIngredients = additional,
        PreparationProfile = source.PreparationProfile,
        AssumptionsUsed = source.AssumptionsUsed
    };

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record OwnedEquipment(string EquipmentId, string? CustomName);
}
