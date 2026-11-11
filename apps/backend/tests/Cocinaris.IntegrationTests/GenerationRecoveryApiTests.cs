using System.Net;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0039 Slice 1: generation recovers from abandoned work and records every call without content.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task AbandonedWorkNoLongerBlocksAndEveryCallIsRecorded()
    {
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: true);
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IAiProvider>(sp => new RecordingAiProvider(provider, new PostgreSqlAiAttemptStore(sp.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostgreSqlAiAttemptStore>>())))));
        using var client = PlanClient(host, "recovery");
        var csrf = await CsrfAsync(client);
        var owner = (await JsonOf(await client.GetAsync("/api/v1/session"))).GetProperty("userId").GetGuid();
        await CreateLotAsync(client, csrf, "Peito de frango", 600m);

        // Two reservations of requests that died long ago would fill the concurrency limit of two (R4).
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < 2; index++)
            {
                database.AiUsageLedgerEntries.Add(new AiUsageLedgerRecord { Id = Guid.NewGuid(), OwnerUserId = owner, Operation = AiOperationRegistry.ExpandSelected, Status = "Reserved", ReservedUnits = 5, CorrelationId = "dead-request", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20), EstimatedCostMicroUsd = 5_000, EstimatedTokens = 10_000 });
            }

            await database.SaveChangesAsync();
        }

        provider.Enqueue(AiOperationRegistry.SuggestCandidates, request => AiProviderInvocationResult.Success(FakeAiProvider.BuildDefaultContent(request).Replace("pantry staples", "basic staples"), "fake-model", 100, 50));
        var session = await RequestSuggestionsAsync(client, csrf);
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dead = await database.AiUsageLedgerEntries.AsNoTracking().Where(item => item.CorrelationId == "dead-request").ToListAsync();
            Assert.All(dead, entry => Assert.Equal(("Released", "abandoned", 0L), (entry.Status, entry.QualityFlags, entry.CostMicroUsd ?? -1)));

            // A selection whose request died while the model worked (R3): Expanding, untouched for ten minutes.
            var key = Guid.NewGuid();
            await database.Database.ExecuteSqlAsync($"""
                UPDATE recipes.generation_sessions SET "Status" = 'Expanding', "SelectedCandidateId" = 'c1', "SelectIdempotencyKey" = {key}, "UpdatedAt" = now() - interval '10 minutes'
                WHERE "Id" = {session.SessionId}
                """);
            provider.Enqueue(AiOperationRegistry.ExpandSelected, request => AiProviderInvocationResult.Success(GenericExpansion(FakeAiProvider.BuildDefaultContent(request)), "fake-model", 100, 50));
            var resumed = await PostAsync(client, csrf, $"/api/v1/recipes/generation-sessions/{session.SessionId}/selection", new { candidateId = "c1" }, key);
            Assert.Equal(HttpStatusCode.Created, resumed.StatusCode);
        }

        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var attempts = await database.AiGenerationAttempts.AsNoTracking().OrderBy(item => item.OccurredAt).ToListAsync();
            Assert.Equal([AiOperationRegistry.SuggestCandidates, AiOperationRegistry.ExpandSelected], attempts.Select(item => item.Operation));
            Assert.All(attempts, item => Assert.Equal(("accepted", 1, 1, "fake-model"), (item.Outcome, item.Round, item.Call, item.Model)));
            // Nothing of the person: no pantry name, and the table has no owner column at all.
            Assert.All(attempts, item => Assert.DoesNotContain("frango", item.Rules ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(database.Model.FindEntityType(typeof(AiGenerationAttemptRecord))!.GetProperties(), property => property.Name.Contains("Owner", StringComparison.Ordinal));
        }
    }
}
