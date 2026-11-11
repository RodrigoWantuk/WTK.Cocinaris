using System.Text.Json;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.SharedKernel.Jobs;

namespace Cocinaris.Api.Planning;

/// <summary>
/// Prepares the recipe of a chosen menu dish on the server (PLAN-0039 Slice 2, decisions R7 to R9).
/// Each attempt has the full recovery of Slice 1. A failed attempt leaves the dish pending and counted,
/// so the board says "Ainda tentando…"; after the last, the dish shows its reason. Waiting for the
/// person's other AI work uses no attempt.
/// </summary>
public sealed class PlanCardExpansionJobHandler(ShoppingPlanApplicationService plans) : IJobHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Codes no retry can fix: the job gives up at once and the dish says why.</summary>
    public static readonly IReadOnlySet<string> FinalCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "ai_budget_exhausted", "ai_budget_unavailable", "ai_capability_unavailable", "validation_failed", "domain_rule_violated",
    };

    /// <summary>Codes that mean the plan or dish is gone or no longer waits: nothing to do.</summary>
    public static readonly IReadOnlySet<string> DoneCodes = new HashSet<string>(StringComparer.Ordinal) { "resource_not_found", "not_found", "plan_not_open" };

    /// <inheritdoc />
    public string Kind => ShoppingPlanApplicationService.ExpansionJobKind;

    /// <inheritdoc />
    public async Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ExpansionJobPayload>(job.PayloadJson, Json);
        if (payload is null || job.OwnerUserId is not { } owner)
        {
            return JobResult.Failed("invalid_payload");
        }

        var result = await plans.ExpandForOwnerAsync(owner, payload.SessionId, payload.CardId, $"job-{job.JobId:N}-{job.Attempt}", payload.Language, failCard: false, cancellationToken);
        if (result.Problem is not { } problem)
        {
            return JobResult.Succeeded;
        }

        var code = problem.ErrorCode;
        if (DoneCodes.Contains(code))
        {
            return JobResult.Succeeded;
        }

        if (code == "ai_operation_conflict")
        {
            return JobResult.Retry(code, TimeSpan.FromSeconds(20), countsAsAttempt: false);
        }

        if (FinalCodes.Contains(code) || job.IsLastAttempt)
        {
            await plans.MarkExpansionFailedAsync(owner, payload.SessionId, payload.CardId, code, cancellationToken);
            return JobResult.Failed(code);
        }

        await plans.NoteExpansionRetryAsync(owner, payload.SessionId, payload.CardId, cancellationToken);
        return JobResult.Retry(code);
    }
}
