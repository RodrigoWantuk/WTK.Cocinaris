using System.Text.Json;
using Cocinaris.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence.AccountData;

/// <summary>
/// Account deletions in PostgreSQL (PLAN-0049 Slice 2). The erasure runs in one transaction over every
/// table of <see cref="AccountDataCatalog"/>, in the order its foreign keys allow, so it either removes
/// everything or nothing, and a retry simply starts over.
/// </summary>
/// <remarks>
/// Table and column names come only from the catalog and are quoted; ids are parameters. The deletion
/// row is locked for the transaction, so two workers never erase the same account twice.
/// </remarks>
public sealed class PostgreSqlAccountDeletionStore(ApplicationDbContext database) : IAccountDeletionStore
{
    private const string Pending = "Pending";
    private const string Completed = "Completed";

    /// <inheritdoc />
    public async Task<(Guid Id, DateTimeOffset RequestedAt)> RequestAsync(string subjectHash, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await database.AccountDeletions.AsNoTracking().FirstOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.State == Pending, cancellationToken);
        if (existing is not null)
        {
            return (existing.Id, existing.RequestedAt);
        }

        var record = new AccountDeletionRecord { Id = Guid.NewGuid(), SubjectHash = subjectHash, OwnerUserId = ownerUserId, State = Pending, RequestedAt = now, NextAttemptAt = now };
        database.AccountDeletions.Add(record);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return (record.Id, record.RequestedAt);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests at once: the unique index kept one; both answer with it.
            database.ChangeTracker.Clear();
            var winner = await database.AccountDeletions.AsNoTracking().SingleAsync(item => item.OwnerUserId == ownerUserId && item.State == Pending, cancellationToken);
            return (winner.Id, winner.RequestedAt);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountDeletionMark>> ListMarksAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        await database.AccountDeletions.AsNoTracking()
            .Where(item => item.RequestedAt >= since || item.State == Pending)
            .Select(item => new AccountDeletionMark(item.SubjectHash, item.RequestedAt, item.State == Completed))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken) =>
        await database.AccountDeletions.AsNoTracking()
            .Where(item => item.State == Pending && item.NextAttemptAt <= now)
            .OrderBy(item => item.RequestedAt)
            .Select(item => item.Id)
            .Take(max)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>?> EraseAsync(Guid deletionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var deletion = await database.AccountDeletions
            .FromSql($"SELECT * FROM platform.account_deletions WHERE \"Id\" = {deletionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (deletion is null || deletion.State != Pending || deletion.OwnerUserId is not { } owner)
        {
            return null;
        }

        // Append-only history (pantry transactions, audit events, recipe revisions) refuses deletion
        // unless the transaction says it is an account erasure; the mark ends with the transaction.
        await database.Database.ExecuteSqlRawAsync("SELECT set_config('cocinaris.account_erasure', 'on', true)", cancellationToken);
        // One random id per deletion: the usage rows stay countable but name nobody (decision D4).
        var anonymous = Guid.NewGuid();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in ErasureOrder(database.Model))
        {
            var target = $"{Quote(step.Schema)}.{Quote(step.Name)}";
            var column = Quote(step.OwnerColumn);
            var (sql, replacement) = step.Erasure switch
            {
                AccountDataErasure.Delete => ($"DELETE FROM {target} WHERE {column} = @owner", (Guid?)null),
                AccountDataErasure.Anonymize => ($"UPDATE {target} SET {column} = @replacement WHERE {column} = @owner", anonymous),
                // A shared row stays; a column that cannot be null gets the empty id, which names nobody.
                AccountDataErasure.ClearOwner when step.OwnerNullable => ($"UPDATE {target} SET {column} = NULL WHERE {column} = @owner", null),
                AccountDataErasure.ClearOwner => ($"UPDATE {target} SET {column} = @replacement WHERE {column} = @owner", Guid.Empty),
                _ => throw new InvalidOperationException("Unknown erasure."),
            };
            object[] parameters = replacement is { } value
                ? [new NpgsqlParameter("owner", owner), new NpgsqlParameter("replacement", value)]
                : [new NpgsqlParameter("owner", owner)];
            counts[$"{step.Schema}.{step.Name}"] = await database.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }

        deletion.State = Completed;
        deletion.CompletedAt = now;
        deletion.OwnerUserId = null;
        deletion.FailureCode = null;
        deletion.CountsJson = JsonSerializer.Serialize(counts);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return counts;
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(Guid deletionId, string failureCode, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        await database.AccountDeletions
            .Where(item => item.Id == deletionId && item.State == Pending)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Attempts, item => item.Attempts + 1)
                .SetProperty(item => item.FailureCode, failureCode.Length > 120 ? failureCode[..120] : failureCode)
                .SetProperty(item => item.NextAttemptAt, nextAttemptAt), cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PurgeCompletedAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        database.AccountDeletions.Where(item => item.State == Completed && item.CompletedAt < before).ExecuteDeleteAsync(cancellationToken);

    /// <summary>One table's erasure, with what the order needs.</summary>
    /// <param name="Schema">Schema.</param>
    /// <param name="Name">Table.</param>
    /// <param name="OwnerColumn">Column holding the person's id.</param>
    /// <param name="Erasure">What happens to the rows.</param>
    /// <param name="OwnerNullable">Whether the owner column accepts null.</param>
    public sealed record ErasureStep(string Schema, string Name, string OwnerColumn, AccountDataErasure Erasure, bool OwnerNullable);

    /// <summary>
    /// Every catalog table and exemption in an order the foreign keys allow: a table that points at
    /// another is erased first. A table erased by update keeps its rows and points nowhere new, so only
    /// deletions constrain the order.
    /// </summary>
    /// <param name="model">The EF model, which knows every foreign key.</param>
    /// <exception cref="InvalidOperationException">A table is missing from the model, or the keys form a cycle.</exception>
    public static IReadOnlyList<ErasureStep> ErasureOrder(IModel model)
    {
        var types = model.GetEntityTypes().Where(type => type.GetTableName() is not null)
            .ToDictionary(type => $"{type.GetSchema()}.{type.GetTableName()}", StringComparer.Ordinal);
        var steps = AccountDataCatalog.Tables.Select(table => (table.Schema, table.Name, table.OwnerColumn, table.Erasure))
            .Concat(AccountDataCatalog.Exemptions.Where(exemption => exemption.Erasure != AccountDataErasure.SelfManaged).Select(exemption => (exemption.Schema, exemption.Name, exemption.OwnerColumn, exemption.Erasure)))
            .Select(item =>
            {
                var type = types.GetValueOrDefault($"{item.Schema}.{item.Name}") ?? throw new InvalidOperationException($"{item.Schema}.{item.Name} is not in the model.");
                var owner = type.GetProperties().Single(property => property.GetColumnName() == item.OwnerColumn);
                return new ErasureStep(item.Schema, item.Name, item.OwnerColumn, item.Erasure, owner.IsNullable);
            })
            .ToList();
        var deleted = steps.Where(step => step.Erasure == AccountDataErasure.Delete).Select(step => $"{step.Schema}.{step.Name}").ToHashSet(StringComparer.Ordinal);
        // principal -> dependents that must go before it
        var before = deleted.ToDictionary(name => name, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var name in deleted)
        {
            foreach (var key in types[name].GetForeignKeys())
            {
                var principal = $"{key.PrincipalEntityType.GetSchema()}.{key.PrincipalEntityType.GetTableName()}";
                if (principal != name && deleted.Contains(principal))
                {
                    before[principal].Add(name);
                }
            }
        }

        var ordered = new List<string>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name)
        {
            if (done.Contains(name))
            {
                return;
            }

            if (!visiting.Add(name))
            {
                throw new InvalidOperationException($"Foreign keys form a cycle through {name}.");
            }

            foreach (var dependent in before[name].Order(StringComparer.Ordinal))
            {
                Visit(dependent);
            }

            visiting.Remove(name);
            done.Add(name);
            ordered.Add(name);
        }

        foreach (var name in deleted.Order(StringComparer.Ordinal))
        {
            Visit(name);
        }

        // The account goes last, also after tables that hold its id without a foreign key.
        if (ordered.Remove("identity.users"))
        {
            ordered.Add("identity.users");
        }

        var byName = steps.ToDictionary(step => $"{step.Schema}.{step.Name}", StringComparer.Ordinal);
        // Updates first, while every row they touch still exists; then deletions, dependents before principals.
        return steps.Where(step => step.Erasure != AccountDataErasure.Delete).Concat(ordered.Select(name => byName[name])).ToList();
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
