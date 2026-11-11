using System.Data;
using System.Runtime.CompilerServices;
using Cocinaris.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence.AccountData;

/// <summary>
/// Reads a person's rows for the export (PLAN-0049). PostgreSQL turns each row into JSON with
/// <c>to_jsonb</c>, so every column is exported without a mapping that could fall behind the schema.
/// </summary>
/// <remarks>
/// Table and column names come only from <see cref="AccountDataCatalog"/>, never from a request, and
/// are quoted; the owner id is a parameter. A snapshot uses its own connection in a read-only
/// repeatable-read transaction, so the JSON and the readable page of one export agree.
/// </remarks>
public sealed class PostgreSqlAccountDataReader(ApplicationDbContext database) : IAccountDataReader
{
    /// <inheritdoc />
    public IReadOnlyList<AccountDataTable> Tables => AccountDataCatalog.Tables;

    /// <inheritdoc />
    public async Task<IAccountDataSnapshot> OpenAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(database.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await readOnly.ExecuteNonQueryAsync(cancellationToken);
            }

            return new Snapshot(connection, transaction, ownerUserId);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Snapshot(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid ownerUserId) : IAccountDataSnapshot
    {
        public async IAsyncEnumerable<string> ReadRowsAsync(AccountDataTable table, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!AccountDataCatalog.Tables.Contains(table))
            {
                throw new ArgumentException("Only catalog tables can be read.", nameof(table));
            }

            var sql = $"SELECT (to_jsonb(t) - @omitted)::text FROM {Quote(table.Schema)}.{Quote(table.Name)} AS t WHERE t.{Quote(table.OwnerColumn)} = @owner";
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("omitted", table.OmittedColumns.ToArray());
            command.Parameters.AddWithValue("owner", ownerUserId);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                yield return reader.GetString(0);
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Read-only, so ending it either way changes nothing; rollback avoids a commit round trip.
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
