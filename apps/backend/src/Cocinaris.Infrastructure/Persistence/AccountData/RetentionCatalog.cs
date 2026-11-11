namespace Cocinaris.Infrastructure.Persistence.AccountData;

/// <summary>What a retention rule does to the rows it selects.</summary>
public enum RetentionAction
{
    /// <summary>The rows are deleted.</summary>
    Delete,

    /// <summary>Only a person id that names no account any more is cleared; the shared row stays.</summary>
    ClearOrphanOwner,
}

/// <summary>One retention rule: a name for metrics, the table it touches, and what it does there.</summary>
/// <param name="Name">Closed metric label.</param>
/// <param name="Schema">Schema.</param>
/// <param name="Table">Table.</param>
/// <param name="Action">What happens to the selected rows.</param>
/// <param name="Purpose">Why the rows have no use any more, as documented in the privacy document.</param>
public sealed record RetentionRule(string Name, string Schema, string Table, RetentionAction Action, string Purpose)
{
    /// <summary>Gets <c>schema.table</c>.</summary>
    public string QualifiedName => $"{Schema}.{Table}";
}

/// <summary>A table no retention rule may delete from, with the reason.</summary>
/// <param name="Schema">Schema.</param>
/// <param name="Table">Table.</param>
/// <param name="Reason">Why it stays.</param>
public sealed record RetainedTable(string Schema, string Table, string Reason)
{
    /// <summary>Gets <c>schema.table</c>.</summary>
    public string QualifiedName => $"{Schema}.{Table}";
}

/// <summary>
/// The retention rules (PLAN-0049 Slice 3) and the tables they must never delete from. Personal data of
/// an existing account has no time limit: it stays until the person deletes it or the account. The
/// rules only remove what no feature reads any more. `RetentionCoverageTests` keeps the two lists apart.
/// </summary>
public static class RetentionCatalog
{
    /// <summary>Gets the rules, in the order they run.</summary>
    public static IReadOnlyList<RetentionRule> Rules { get; } =
    [
        new("idempotency", "platform", "idempotency_records", RetentionAction.Delete, "Stored replies of retried requests; a retry comes within minutes, and the option bounds them at 30 days."),
        new("ai_usage", "ai", "usage_ledger", RetentionAction.Delete, "AI usage counts toward a month's spend; after 13 months no ceiling or report reads them (decision D4)."),
        new("ai_attempts", "ai", "generation_attempts", RetentionAction.Delete, "Records of AI calls explain failures; after 13 months they are kept no longer than usage (PLAN-0039 R5)."),
        new("finished_jobs", "platform", "jobs", RetentionAction.Delete, "A finished job only explains what happened; after 30 days nothing reads it (PLAN-0039)."),
        new("deletion_tombstones", "platform", "account_deletions", RetentionAction.Delete, "A completed deletion's tombstone ends older sessions; after 31 days none can remain (decision E3)."),
        new("push_delivery_days", "planning", "push_delivery_days", RetentionAction.Delete, "The last day a person was notified only prevents a second message that day."),
        new("abandoned_plans", "planning", "shopping_plan_sessions", RetentionAction.Delete, "A shopping plan the person discarded, untouched for 30 days, with its cards."),
        new("orphan_thumbnail_owner", "recipes", "recipe_thumbnails", RetentionAction.ClearOrphanOwner, "A dish image asked for during an account's erasure may still name it; the image stays for everyone."),
        new("orphan_ingredient_owner", "ingredients", "resolution_queue", RetentionAction.ClearOrphanOwner, "An ingredient name queued during an account's erasure may still name it; the name stays."),
    ];

    /// <summary>Gets the tables no rule may delete from.</summary>
    public static IReadOnlyList<RetainedTable> Retained { get; } =
    [
        // Owner decision D5 (2026-11-11): the shared library is inspiration and suggestion without AI.
        new("recipes", "recipe_ideas", "The shared recipe library, found through ingredients recognized with embeddings, serves as inspiration and suggestion without AI. It goes only on an explicit request to delete a recipe or under a future rule for very low ratings (owner decision D5)."),
        new("recipes", "recipe_thumbnails", "Dish images are shared and reused by similarity; only a person id that names nobody is cleared."),
        new("ingredients", "aliases", "Shared ingredient names and what they mean; reference data with no owner."),
        new("ingredients", "catalog_items", "The curated ingredient catalog."),
        new("ingredients", "catalog_state", "The catalog's version and embedding state."),
        new("ingredients", "catalog_vectors", "Embeddings of the catalog, which recognize ingredients and so find recipes in the library."),
        new("ingredients", "text_embeddings", "Embeddings of exact ingredient texts, cached so the same text is never sent twice."),
        new("ingredients", "resolution_queue", "Names waiting for recognition, then kept so a later catalog can check them again; only a person id that names nobody is cleared."),
    ];
}
