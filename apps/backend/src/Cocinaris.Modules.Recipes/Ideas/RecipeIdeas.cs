using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.Modules.Recipes.Ideas;

/// <summary>
/// The generic content of one shared recipe idea (PLAN-0055 Phase 2, ADR-0011). It holds no owner,
/// pantry lot, equipment item, or word the owner typed: ingredients carry catalog names and
/// equipment carries stable codes.
/// </summary>
/// <param name="Candidate">Suggestion card: no inventory uses or additional ingredients (those are rebuilt per user); equipment ids are stable codes; primary refs are catalog slugs.</param>
/// <param name="Recipe">Expanded recipe: ingredients are <c>additional</c> or <c>assumption</c> with catalog names and no inventory ids; equipment ids are stable codes.</param>
/// <param name="IngredientSlugs">Catalog slug of every recipe ingredient, by ingredient id.</param>
/// <param name="RequiredStates">State the suggestion asked for each slug it took from the pantry (<c>raw</c>, <c>cooked</c>, …).</param>
public sealed record RecipeIdeaContent(
    SuggestCandidate Candidate,
    ExpandRecipe Recipe,
    IReadOnlyDictionary<string, string> IngredientSlugs,
    IReadOnlyDictionary<string, string> RequiredStates);

/// <summary>One stored idea and the facts the library filters and ranks by.</summary>
/// <param name="Id">Idea id; suggestion candidates built from it are named <c>idea-&lt;id:n&gt;</c>.</param>
/// <param name="Signature">SHA-256 of everything that makes two ideas the same; duplicates are not stored twice.</param>
/// <param name="Language">Output language the content is written in.</param>
/// <param name="InstructionDetail">Instruction detail level the content was written for, if any.</param>
/// <param name="CookingSkill">Cooking skill the content was written for, if any.</param>
/// <param name="Restrictions">Restriction codes the idea was generated under, sorted. It may be served only to someone whose restrictions are all in this set.</param>
/// <param name="MealType">Target meal type.</param>
/// <param name="Strategy">Suggestion strategy the idea came from.</param>
/// <param name="IngredientSlugs">Slugs of the required, non-assumed ingredients, for retrieval.</param>
/// <param name="EquipmentCodes">Equipment stable codes the recipe needs.</param>
/// <param name="Content">Generic content.</param>
/// <param name="TimesOffered">Times the idea was shown as a suggestion.</param>
/// <param name="TimesSelected">Times someone chose it.</param>
public sealed record RecipeIdea(
    Guid Id,
    string Signature,
    string Language,
    string? InstructionDetail,
    string? CookingSkill,
    IReadOnlyList<string> Restrictions,
    string MealType,
    string Strategy,
    IReadOnlyList<string> IngredientSlugs,
    IReadOnlyList<string> EquipmentCodes,
    RecipeIdeaContent Content,
    int TimesOffered,
    int TimesSelected);

/// <summary>Hard filters applied in the store before any scoring.</summary>
/// <param name="Language">Required output language.</param>
/// <param name="InstructionDetail">Required instruction detail (null matches null only).</param>
/// <param name="CookingSkill">Required cooking skill (null matches null only).</param>
/// <param name="Restrictions">The user's restriction codes; the idea's set must contain all of them.</param>
/// <param name="MealType">Required meal type, or null for any.</param>
/// <param name="PantrySlugs">Catalog slugs in the user's pantry; the idea must use at least one.</param>
/// <param name="EquipmentCodes">The user's equipment codes; the idea may need only these.</param>
/// <param name="Limit">Most ideas returned.</param>
public sealed record RecipeIdeaQuery(
    string Language,
    string? InstructionDetail,
    string? CookingSkill,
    IReadOnlyList<string> Restrictions,
    string? MealType,
    IReadOnlyList<string> PantrySlugs,
    IReadOnlyList<string> EquipmentCodes,
    int Limit);

/// <summary>Persistence port of the shared recipe-idea library. The rows are global and hold no owner.</summary>
public interface IRecipeIdeaStore
{
    /// <summary>Stores an idea unless one with the same signature exists.</summary>
    Task AddAsync(RecipeIdea idea, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Finds ideas that pass every hard filter, most chosen first.</summary>
    Task<IReadOnlyList<RecipeIdea>> FindCandidatesAsync(RecipeIdeaQuery query, CancellationToken cancellationToken);

    /// <summary>Finds one idea.</summary>
    Task<RecipeIdea?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds one to the offered count of each idea.</summary>
    Task MarkOfferedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Adds one to the selected count of an idea.</summary>
    Task MarkSelectedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Normalized names of the owner's recipes (every revision), so ideas they already have are not offered again.</summary>
    Task<IReadOnlySet<string>> OwnerRecipeNamesAsync(Guid ownerUserId, CancellationToken cancellationToken);
}

/// <summary>Library policy under <c>Recipes:Ideas</c>.</summary>
public sealed class RecipeIdeaOptions
{
    /// <summary>Gets or sets whether ideas are captured and served.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the share of suggestion requests sent to the AI even when the library could
    /// answer, so the library keeps growing and suggestions stay varied.
    /// </summary>
    public double NoveltyShare { get; set; } = 0.25;

    /// <summary>Gets or sets the lowest pantry coverage (0–1) of an idea's required ingredients for it to be suggested.</summary>
    public double MinimumCoverage { get; set; } = 0.6;

    /// <summary>Gets or sets the most ideas read from the store per request before scoring.</summary>
    public int CandidatePoolSize { get; set; } = 40;

    /// <summary>Validates that the policy is coherent.</summary>
    public bool IsValid() => NoveltyShare is >= 0 and <= 1 && MinimumCoverage is > 0 and <= 1 && CandidatePoolSize is >= 3 and <= 200;
}
