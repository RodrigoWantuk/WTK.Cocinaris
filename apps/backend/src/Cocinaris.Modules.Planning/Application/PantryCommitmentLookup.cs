using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>Planning implementation of the cooking proposal's commitment query (PLAN-0029 Phase 3).</summary>
public sealed class PantryCommitmentLookup(MenuPlanningApplicationService planning) : IPantryCommitmentLookup
{
    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, LotCommitment>> GetAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        planning.GetCommitmentsAsync(ownerUserId, cancellationToken);
}
