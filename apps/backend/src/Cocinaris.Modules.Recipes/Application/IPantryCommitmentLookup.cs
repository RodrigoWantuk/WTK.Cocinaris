namespace Cocinaris.Modules.Recipes.Application;

/// <summary>Quantity of a lot an accepted menu meal counts on.</summary>
/// <param name="MenuEntryId">Menu meal.</param>
/// <param name="RecipeName">Recipe of the meal.</param>
/// <param name="PlannedDate">Day of the meal, or null when flexible.</param>
/// <param name="Quantity">Quantity reserved, in the lot's unit.</param>
public sealed record LotReservationView(Guid MenuEntryId, string RecipeName, DateOnly? PlannedDate, decimal Quantity);

/// <summary>What is promised from one lot: a protected quantity the owner locked, and menu reservations.</summary>
/// <param name="LotId">Lot.</param>
/// <param name="ProtectedQuantity">Quantity the owner locked; never proposed for use.</param>
/// <param name="Reservations">Accepted menu meals counting on the lot, in plan order.</param>
public sealed record LotCommitment(Guid LotId, decimal ProtectedQuantity, IReadOnlyList<LotReservationView> Reservations);

/// <summary>
/// Planning-owned query of pantry commitments (PLAN-0029 Phase 3), defined here so the cooking
/// proposal can read it without depending on the Planning module. Commitments never change
/// physical quantity; the proposal uses them to warn and to avoid proposing protected quantity.
/// </summary>
public interface IPantryCommitmentLookup
{
    /// <summary>Commitments of the owner's lots, keyed by lot; lots without commitments are absent.</summary>
    Task<IReadOnlyDictionary<Guid, LotCommitment>> GetAsync(Guid ownerUserId, CancellationToken cancellationToken);
}
