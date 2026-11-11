using Cocinaris.SharedKernel.Privacy;

namespace Cocinaris.Infrastructure.Persistence.AccountData;

/// <summary>
/// Every table that holds a person's rows, by owning module (PLAN-0049 data map). A new table with an
/// owner column must be added here or to <see cref="Exemptions"/>; `AccountDataCoverageTests` reads
/// the EF model and fails otherwise. Each entry also says what account deletion does to its rows.
/// </summary>
public static class AccountDataCatalog
{
    private const string Owner = "OwnerUserId";

    private static AccountDataTable Table(string module, string schema, string name, string owner = Owner, params string[] omitted) =>
        new(module, schema, name, owner, omitted);

    private static AccountDataTable Anonymized(string module, string schema, string name) =>
        new(module, schema, name, Owner, [], AccountDataErasure.Anonymize);

    /// <summary>Gets the exported tables in export order: account first, then each module.</summary>
    public static IReadOnlyList<AccountDataTable> Tables { get; } =
    [
        Table("identity", "identity", "users", "Id"),
        Table("profiles", "profiles", "user_profiles"),
        Table("profiles", "profiles", "preference_entries"),
        Table("profiles", "profiles", "ordered_code_entries"),
        Table("profiles", "profiles", "equipment_entries"),
        Table("profiles", "profiles", "change_history"),
        Table("inventory", "inventory", "products"),
        Table("inventory", "inventory", "lots"),
        Table("inventory", "inventory", "transactions"),
        Table("inventory", "inventory", "preparation_batches"),
        Table("inventory", "inventory", "preparation_inputs"),
        Table("inventory", "inventory", "preparation_outputs"),
        Table("inventory", "inventory", "prepared_lots"),
        Table("recipes", "recipes", "recipes"),
        Table("recipes", "recipes", "recipe_revisions"),
        Table("recipes", "recipes", "recipe_executions"),
        Table("recipes", "recipes", "generation_sessions"),
        Table("recipes", "recipes", "revision_proposals"),
        Table("recipes", "recipes", "saved_candidates"),
        Table("planning", "planning", "menu_plans"),
        Table("planning", "planning", "menu_entries"),
        Table("planning", "planning", "lot_protections"),
        Table("planning", "planning", "package_sizes"),
        Table("planning", "planning", "shopping_checks"),
        Table("planning", "planning", "shopping_line_marks"),
        Table("planning", "planning", "shopping_plan_sessions"),
        Table("planning", "planning", "shopping_plan_cards"),
        Table("planning", "planning", "reminder_dismissals"),
        Table("planning", "planning", "push_delivery_days"),
        // The push address and key belong to the browser and would let anyone send it messages.
        Table("planning", "planning", "push_subscriptions", Owner, "Endpoint", "P256dh", "Auth"),
        // Kept anonymized for 13 months so monthly spend stays right (decision D4).
        Anonymized("ai", "ai", "usage_ledger"),
        Table("platform", "platform", "audit_events", "ActorUserId"),
        // Background work done for the person (PLAN-0039): kinds, states, and ids, no text.
        Table("platform", "platform", "jobs"),
    ];

    /// <summary>Gets the tables with a person's id that are not exported, with the reason.</summary>
    public static IReadOnlyList<AccountDataExemption> Exemptions { get; } =
    [
        new("platform", "idempotency_records", "Stored replies for retried requests; they repeat data already exported and expire.", Owner, AccountDataErasure.Delete),
        new("recipes", "recipe_thumbnails", "Generic dish images shared by everyone; the person's id only says who asked first.", "RequestedByUserId", AccountDataErasure.ClearOwner),
        new("platform", "account_deletions", "The deletion request itself; it names the account only while pending, and its tombstone keeps a hash of the login for 31 days.", Owner, AccountDataErasure.SelfManaged),
        new("ingredients", "resolution_queue", "Shared ingredient names waiting for recognition; the person's id is held only while a name is pending.", "RequestedByUserId", AccountDataErasure.ClearOwner),
    ];
}
