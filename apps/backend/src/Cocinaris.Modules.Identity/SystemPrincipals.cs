namespace Cocinaris.Modules.Identity;

/// <summary>
/// Well-known internal users that represent the platform itself, not a person (PLAN-0055).
/// </summary>
/// <remarks>
/// The AI usage ledger requires an owner for every provider call. Background maintenance that no
/// person triggered, such as re-checking ingredient names after the catalog changes, is charged
/// to <see cref="Maintenance"/>. Its issuer is not an OIDC issuer, so no sign-in can ever resolve
/// to it, and it owns no recipes, pantry, or profile. A migration creates the row.
/// </remarks>
public static class SystemPrincipals
{
    /// <summary>Internal user id of the background-maintenance principal.</summary>
    public static readonly Guid Maintenance = new("00000000-0000-0000-0000-00000000c0c1");

    /// <summary>Issuer recorded for system principals; deliberately not a URL any identity provider can use.</summary>
    public const string Issuer = "urn:cocinaris:system";

    /// <summary>Subject of the background-maintenance principal.</summary>
    public const string MaintenanceSubject = "maintenance";
}
