using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>
/// Assembles the bounded, already-untrusted-marked AI Gateway request context for one owner from
/// authoritative inventory and profile state. Implementations must never include unrestricted
/// database access, credentials, or more than <c>contextBudgetCharacters</c> of serialized content.
/// </summary>
public interface IRecipeContextAssembler
{
    /// <summary>
    /// Assembles the bounded suggest-candidates request context for one owner: <c>cook_now</c>, or
    /// <c>menu_planning</c> for <paramref name="menuTarget"/>, in which case pantry quantities are
    /// what is neither protected nor reserved for other meals.
    /// </summary>
    Task<RecipeSuggestRequestContext> AssembleSuggestContextAsync(Guid ownerUserId, string requestId, int contextBudgetCharacters, CancellationToken cancellationToken, MenuPlanningTarget? menuTarget = null);

    /// <summary>Assembles the bounded selected-candidate expansion request context for one owner.</summary>
    Task<RecipeExpandRequestContext> AssembleExpandContextAsync(Guid ownerUserId, string requestId, SuggestCandidate selectedCandidate, int contextBudgetCharacters, CancellationToken cancellationToken);
}
