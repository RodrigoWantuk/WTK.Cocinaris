using Cocinaris.Modules.Ai.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// Writes attempt records (PLAN-0039 R5) on a connection of its own, so a record never joins, and can
/// never break, the transaction of the generation it describes. Failures are logged by type and lost.
/// </summary>
public sealed class PostgreSqlAiAttemptStore(ApplicationDbContext database, ILogger<PostgreSqlAiAttemptStore> logger) : IAiAttemptStore
{
    /// <inheritdoc />
    public async Task RecordAsync(AiAttemptRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(database.Database.GetConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                INSERT INTO ai.generation_attempts ("Id", "OccurredAt", "Operation", "Model", "Round", "Call", "Outcome", "Rules", "LatencyMs", "PromptTokens", "CompletionTokens", "CorrelationId")
                VALUES (@id, @at, @operation, @model, @round, @call, @outcome, @rules, @latency, @prompt, @completion, @correlation)
                """, connection);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("at", record.OccurredAt);
            command.Parameters.AddWithValue("operation", Cut(record.Operation, 100)!);
            command.Parameters.AddWithValue("model", (object?)Cut(record.Model, 100) ?? DBNull.Value);
            command.Parameters.AddWithValue("round", Math.Clamp(record.Round, 1, 2));
            command.Parameters.AddWithValue("call", record.Call);
            command.Parameters.AddWithValue("outcome", record.Outcome);
            command.Parameters.AddWithValue("rules", (object?)Cut(record.Rules, 300) ?? DBNull.Value);
            command.Parameters.AddWithValue("latency", (object?)record.LatencyMs ?? DBNull.Value);
            command.Parameters.AddWithValue("prompt", (object?)record.PromptTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("completion", (object?)record.CompletionTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("correlation", Cut(record.CorrelationId, 100)!);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("AI attempt record lost: {ExceptionType}", exception.GetType().Name);
        }
    }

    private static string? Cut(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
