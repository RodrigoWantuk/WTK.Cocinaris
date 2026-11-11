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
public sealed record AccountDataTable(string Module, string Schema, string Name, string OwnerColumn, IReadOnlyList<string> OmittedColumns)
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
public sealed record AccountDataExemption(string Schema, string Name, string Reason);

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
