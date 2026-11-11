namespace Cocinaris.SharedKernel.Privacy;

/// <summary>
/// One table that holds a person's own rows (PLAN-0049). The export reads it whole, minus the omitted
/// columns, filtered by <see cref="OwnerColumn"/>; nothing else of the table is ever read.
/// </summary>
/// <param name="Module">The module that owns the table, such as <c>recipes</c>; also the export section.</param>
/// <param name="Schema">PostgreSQL schema.</param>
/// <param name="Name">Table name.</param>
/// <param name="OwnerColumn">Column holding the person's internal user id.</param>
/// <param name="OmittedColumns">Columns left out of the export, such as a browser's push keys, which are secrets of that browser.</param>
/// <param name="Erasure">What account deletion does to the person's rows (PLAN-0049 Slice 2).</param>
public sealed record AccountDataTable(string Module, string Schema, string Name, string OwnerColumn, IReadOnlyList<string> OmittedColumns, AccountDataErasure Erasure = AccountDataErasure.Delete)
{
    /// <summary>Gets the qualified name, <c>schema.table</c>, used as the export key.</summary>
    public string QualifiedName => $"{Schema}.{Name}";
}

/// <summary>
/// A table with a person's id that is deliberately not exported, with the reason (PLAN-0049). The
/// coverage test accepts a table only if it is exported or listed here.
/// </summary>
/// <param name="Schema">PostgreSQL schema.</param>
/// <param name="Name">Table name.</param>
/// <param name="Reason">Why it is left out of the export.</param>
/// <param name="OwnerColumn">Column holding the person's internal user id.</param>
/// <param name="Erasure">What account deletion does to the person's rows.</param>
public sealed record AccountDataExemption(string Schema, string Name, string Reason, string OwnerColumn, AccountDataErasure Erasure);

/// <summary>What account deletion does to a table's rows of the deleted person (PLAN-0049 Slice 2).</summary>
public enum AccountDataErasure
{
    /// <summary>The rows are deleted.</summary>
    Delete,

    /// <summary>The owner id is replaced by one random id per deletion; used for AI usage, kept for spend accounting (decision D4).</summary>
    Anonymize,

    /// <summary>The owner id is set to null and the shared row stays, such as a dish image others use.</summary>
    ClearOwner,

    /// <summary>The deletion's own record, which the deletion store clears when the erasure completes.</summary>
    SelfManaged,
}

/// <summary>A deletion's state as the session check needs it: who, when, and whether it finished.</summary>
/// <param name="SubjectHash">SHA-256, in lowercase hex, of the login's issuer and subject; never the subject itself.</param>
/// <param name="RequestedAt">When the deletion was asked for; sessions signed in before it end.</param>
/// <param name="Completed">Whether every row is gone; until then, signing in again is refused.</param>
public sealed record AccountDeletionMark(string SubjectHash, DateTimeOffset RequestedAt, bool Completed);

/// <summary>Durable account deletions (PLAN-0049 Slice 2): the request, the erasure, and the short-lived tombstone.</summary>
public interface IAccountDeletionStore
{
    /// <summary>Records a deletion for one person, or returns the one already pending, so repeating the request changes nothing.</summary>
    /// <param name="subjectHash">The login's hash, kept as the tombstone.</param>
    /// <param name="ownerUserId">The internal user id whose rows go; cleared when the deletion completes.</param>
    /// <param name="now">The request time.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The deletion's id and when it was requested.</returns>
    Task<(Guid Id, DateTimeOffset RequestedAt)> RequestAsync(string subjectHash, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Lists the deletions requested since <paramref name="since"/>, for the session check.</summary>
    /// <param name="since">The oldest request that can still matter: now minus the longest session.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<AccountDeletionMark>> ListMarksAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Lists pending deletions whose next attempt is due.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="max">At most this many.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken);

    /// <summary>
    /// Erases every row of the deletion's person in one transaction, in an order the foreign keys allow,
    /// and marks the deletion completed with per-table counts. A failure changes nothing, so a retry
    /// starts over; an already completed deletion is left as it is.
    /// </summary>
    /// <param name="deletionId">A pending deletion.</param>
    /// <param name="now">The completion time.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Rows affected per table, or null when the deletion was already completed or does not exist.</returns>
    Task<IReadOnlyDictionary<string, int>?> EraseAsync(Guid deletionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Records a failed attempt with a closed code and when to try again.</summary>
    /// <param name="deletionId">The deletion.</param>
    /// <param name="failureCode">A closed code, such as an exception type name; never data.</param>
    /// <param name="nextAttemptAt">When the next attempt is due.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task RecordFailureAsync(Guid deletionId, string failureCode, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken);

    /// <summary>Deletes tombstones of completed deletions older than <paramref name="before"/> (retention, Slice 3).</summary>
    /// <param name="before">The cutoff.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How many were deleted.</returns>
    Task<int> PurgeCompletedAsync(DateTimeOffset before, CancellationToken cancellationToken);
}

/// <summary>Reads everything a person has stored, for the export of the privacy center (PLAN-0049).</summary>
public interface IAccountDataReader
{
    /// <summary>Gets every exported table, grouped by module in a stable order.</summary>
    IReadOnlyList<AccountDataTable> Tables { get; }

    /// <summary>
    /// Opens a read-only snapshot for one person, so every table is read at the same moment even
    /// when the export reads a table twice.
    /// </summary>
    /// <param name="ownerUserId">The internal user id of the person exporting.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A snapshot that must be disposed, which ends its transaction.</returns>
    Task<IAccountDataSnapshot> OpenAsync(Guid ownerUserId, CancellationToken cancellationToken);
}

/// <summary>A read-only view of one person's rows at one moment.</summary>
public interface IAccountDataSnapshot : IAsyncDisposable
{
    /// <summary>
    /// Streams the person's rows of one table, each as the text of a JSON object whose keys are the
    /// column names. Rows of other people are never returned.
    /// </summary>
    /// <param name="table">A table from <see cref="IAccountDataReader.Tables"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="ArgumentException">The table is not one of <see cref="IAccountDataReader.Tables"/>.</exception>
    IAsyncEnumerable<string> ReadRowsAsync(AccountDataTable table, CancellationToken cancellationToken);
}

/// <summary>
/// The limits of one retention run (PLAN-0049 Slice 3): rows older than these go. Each comes from a
/// documented decision; nothing here touches the shared recipe library (decision D5).
/// </summary>
/// <param name="IdempotencyBefore">Stored replies created before this (the <c>Idempotency:Retention</c> option, 30 days by default).</param>
/// <param name="UsageBefore">AI usage rows created before this (13 months, decision D4).</param>
/// <param name="TombstonesBefore">Completed deletions finished before this (31 days, decision E3).</param>
/// <param name="PushDaysBefore">Last-notified days before this (30 days).</param>
/// <param name="AbandonedPlansBefore">Shopping plans abandoned and untouched since before this (30 days).</param>
public sealed record RetentionCutoffs(DateTimeOffset IdempotencyBefore, DateTimeOffset UsageBefore, DateTimeOffset TombstonesBefore, DateOnly PushDaysBefore, DateTimeOffset AbandonedPlansBefore);

/// <summary>Applies the retention rules (PLAN-0049 Slice 3).</summary>
public interface IRetentionStore
{
    /// <summary>
    /// Runs every rule once, each as its own statement and capped at <paramref name="maxRowsPerRule"/>,
    /// so a backlog is worked off over several runs instead of one long lock.
    /// </summary>
    /// <param name="cutoffs">The limits of this run.</param>
    /// <param name="maxRowsPerRule">At most this many rows per rule.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Rows deleted or cleared per rule name; counts only.</returns>
    Task<IReadOnlyDictionary<string, int>> ApplyAsync(RetentionCutoffs cutoffs, int maxRowsPerRule, CancellationToken cancellationToken);
}
