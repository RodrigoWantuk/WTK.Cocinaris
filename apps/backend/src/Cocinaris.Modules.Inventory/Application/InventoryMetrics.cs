using System.Diagnostics.Metrics;

namespace Cocinaris.Modules.Inventory.Application;

/// <summary>
/// Owns low-cardinality inventory operational metrics. Labels are restricted to stable operation,
/// outcome, and error-code categories; owner IDs, product names, notes, headers, and request
/// bodies are intentionally never accepted as metric values.
/// </summary>
public sealed class InventoryMetrics
{
    private static readonly Meter Meter = new("Cocinaris.Inventory", "1.0.0");
    private static readonly Counter<long> Mutations = Meter.CreateCounter<long>("cocinaris.inventory.mutations", unit: "operations", description: "Completed inventory mutation attempts by operation and outcome.");
    private static readonly Counter<long> Rejections = Meter.CreateCounter<long>("cocinaris.inventory.rejections", unit: "operations", description: "Inventory validation and domain-rule rejections by stable category.");
    private static readonly Counter<long> ConcurrencyFailures = Meter.CreateCounter<long>("cocinaris.inventory.concurrency_failures", unit: "operations", description: "Optimistic concurrency failures for inventory mutations.");
    private static readonly Counter<long> IdempotencyOutcomes = Meter.CreateCounter<long>("cocinaris.inventory.idempotency", unit: "operations", description: "Idempotency replay, reuse, and in-progress outcomes.");

    /// <summary>Records the final outcome of a state-changing inventory operation.</summary>
    /// <param name="operation">Stable operation name such as <c>create</c> or <c>adjust</c>.</param>
    /// <param name="result">Typed result whose stable error category controls the metric labels.</param>
    public void RecordMutation<T>(string operation, InventoryApplicationResult<T> result)
    {
        var errorCode = result.Problem?.ErrorCode;
        var outcome = errorCode is null ? "succeeded" : errorCode;
        Mutations.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("outcome", outcome));
        if (errorCode is "validation_failed" or "domain_rule_violated")
        {
            Rejections.Add(1, new KeyValuePair<string, object?>("category", errorCode));
        }

        if (errorCode == "precondition_failed")
        {
            ConcurrencyFailures.Add(1, new KeyValuePair<string, object?>("operation", operation));
        }

        if (errorCode is "idempotency_key_reused" or "idempotency_in_progress" || result.Idempotency is not InventoryIdempotencyDisposition.NotApplicable)
        {
            var idempotencyOutcome = errorCode switch
            {
                "idempotency_key_reused" => "reused",
                "idempotency_in_progress" => "in_progress",
                _ => result.Idempotency == InventoryIdempotencyDisposition.Replayed ? "replayed" : "completed"
            };
            IdempotencyOutcomes.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("outcome", idempotencyOutcome));
        }
    }
}
