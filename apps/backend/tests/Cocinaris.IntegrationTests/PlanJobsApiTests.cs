using System.Net;
using System.Text.Json;
using Cocinaris.Api.Jobs;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0039 Slice 2: a chosen menu dish is prepared by a server job, with no request from the app.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private const string InvalidExpansion = """{"operation":"recipe.expand_selected.v1","schemaVersion":"0.3"}""";

    private static async Task<int> RunJobsAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<JobProcessor>().RunDueAsync(CancellationToken.None);
    }

    private static async Task<JobRecord> JobOfAsync(WebApplicationFactory<Program> host, Guid cardId)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Jobs.AsNoTracking().SingleAsync(job => job.SubjectKey == cardId.ToString());
    }

    private static async Task MakeDueAsync(WebApplicationFactory<Program> host, Guid cardId)
    {
        using var scope = host.Services.CreateScope();
        var subject = cardId.ToString();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Jobs.Where(job => job.SubjectKey == subject)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    private static JsonElement CardOf(JsonElement plan, Guid cardId) =>
        plan.GetProperty("accepted").EnumerateArray().Single(card => card.GetProperty("cardId").GetGuid() == cardId);

    /// <summary>A plan of snacks with six AI dishes on offer; returns the plan, its id, and one AI dish.</summary>
    private static async Task<(Guid SessionId, Guid MenuId, Guid CardId)> AiDishAsync(HttpClient client, string csrf, Modules.Ai.Providers.FakeAiProvider provider, string tag)
    {
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 3, days: 7), Guid.NewGuid()));
        var sessionId = plan.GetProperty("sessionId").GetGuid();
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks(tag));
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/batches", new { mealType = "snack" }, Guid.NewGuid()));
        return (sessionId, plan.GetProperty("menuId").GetGuid(), OptionIds(plan, "snack")[0]);
    }

    [Fact]
    public async Task AChosenDishIsPreparedOnTheServerAndRetriesBeforeItSaysWhy()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "jobs-prepare");
        var csrf = await CsrfAsync(client);
        var (sessionId, menuId, cardId) = await AiDishAsync(client, csrf, provider, "Job");

        // Choosing queues the job after the undo window; nothing runs before it.
        var plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" }));
        Assert.Equal("pending", CardOf(plan, cardId).GetProperty("expansionState").GetString());
        var job = await JobOfAsync(host, cardId);
        Assert.Equal(("Queued", 0, 3), (job.State, job.Attempts, job.MaxAttempts));
        Assert.True(job.NextAttemptAt > DateTimeOffset.UtcNow);
        Assert.Equal(0, await RunJobsAsync(host));

        // The first attempt fails in both rounds: the dish stays pending and says it is still trying.
        await MakeDueAsync(host, cardId);
        for (var call = 0; call < 4; call++)
        {
            provider.Enqueue(AiOperationRegistry.ExpandSelected, AiProviderInvocationResult.Success(InvalidExpansion, "fake-model"));
        }

        Assert.Equal(1, await RunJobsAsync(host));
        job = await JobOfAsync(host, cardId);
        Assert.Equal(("Queued", 1, "ai_output_invalid"), (job.State, job.Attempts, job.LastFailureCode));
        Assert.True(job.NextAttemptAt > DateTimeOffset.UtcNow.AddSeconds(30));
        plan = await JsonOf(await client.GetAsync($"{Plans}/{sessionId}"));
        Assert.Equal(("pending", true), (CardOf(plan, cardId).GetProperty("expansionState").GetString(), CardOf(plan, cardId).GetProperty("retrying").GetBoolean()));

        // The next attempt works with no request from the app: the recipe and its meal exist.
        await MakeDueAsync(host, cardId);
        Assert.Equal(1, await RunJobsAsync(host));
        Assert.Equal("Succeeded", (await JobOfAsync(host, cardId)).State);
        plan = await JsonOf(await client.GetAsync($"{Plans}/{sessionId}"));
        Assert.Equal(("ready", false), (CardOf(plan, cardId).GetProperty("expansionState").GetString(), CardOf(plan, cardId).GetProperty("retrying").GetBoolean()));
        Assert.Equal(1, (await EntriesAsync(client, menuId)).GetArrayLength());
    }

    [Fact]
    public async Task AfterTheLastAttemptTheDishSaysWhyAndCanBeTriedAgain()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "jobs-fail");
        var csrf = await CsrfAsync(client);
        var (sessionId, _, cardId) = await AiDishAsync(client, csrf, provider, "Falha");
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" });
        for (var attempt = 0; attempt < 3; attempt++)
        {
            for (var call = 0; call < 4; call++)
            {
                provider.Enqueue(AiOperationRegistry.ExpandSelected, AiProviderInvocationResult.Success(InvalidExpansion, "fake-model"));
            }

            await MakeDueAsync(host, cardId);
            Assert.Equal(1, await RunJobsAsync(host));
        }

        var job = await JobOfAsync(host, cardId);
        Assert.Equal(("Failed", 3, "ai_output_invalid"), (job.State, job.Attempts, job.LastFailureCode));
        var plan = await JsonOf(await client.GetAsync($"{Plans}/{sessionId}"));
        Assert.Equal(("failed", "ai_output_invalid"), (CardOf(plan, cardId).GetProperty("expansionState").GetString(), CardOf(plan, cardId).GetProperty("failureCode").GetString()));

        // "Tentar de novo" queues it again at once, with every attempt back.
        plan = await JsonOf(await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/expansion-retry", new { }));
        Assert.Equal("pending", CardOf(plan, cardId).GetProperty("expansionState").GetString());
        job = await JobOfAsync(host, cardId);
        Assert.Equal(("Queued", 0), (job.State, job.Attempts));
        Assert.Equal(1, await RunJobsAsync(host));
        plan = await JsonOf(await client.GetAsync($"{Plans}/{sessionId}"));
        Assert.Equal("ready", CardOf(plan, cardId).GetProperty("expansionState").GetString());
    }

    [Fact]
    public async Task UndoCancelsALostJobIsQueuedAgainAndTwoRunnersNeverShareOne()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "jobs-undo");
        var csrf = await CsrfAsync(client);
        var (sessionId, _, cardId) = await AiDishAsync(client, csrf, provider, "Desfaz");

        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" });
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/undo", new { });
        Assert.Equal("Cancelled", (await JobOfAsync(host, cardId)).State);
        await MakeDueAsync(host, cardId);
        Assert.Equal(0, await RunJobsAsync(host));

        // Chosen again, then its job is lost: reading the plan queues one.
        await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/decision", new { decision = "accept", mealType = "snack" });
        using (var scope = host.Services.CreateScope())
        {
            var subject = cardId.ToString();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Jobs.Where(job => job.SubjectKey == subject).ExecuteDeleteAsync();
        }

        await client.GetAsync($"{Plans}/{sessionId}");
        Assert.Equal("Queued", (await JobOfAsync(host, cardId)).State);

        // A runner that died left the job leased; after the lease, two runners race and one wins.
        using (var scope = host.Services.CreateScope())
        {
            var subject = cardId.ToString();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Jobs.Where(job => job.SubjectKey == subject)
                .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.State, "Running").SetProperty(job => job.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        var runs = await Task.WhenAll(RunJobsAsync(host), RunJobsAsync(host));
        Assert.Equal(1, runs.Sum());
        Assert.Equal("Succeeded", (await JobOfAsync(host, cardId)).State);
        var plan = await JsonOf(await client.GetAsync($"{Plans}/{sessionId}"));
        Assert.Equal("ready", CardOf(plan, cardId).GetProperty("expansionState").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync(client, csrf, $"{Plans}/{sessionId}/cards/{cardId}/expansion-retry", new { })).StatusCode);
    }
}
