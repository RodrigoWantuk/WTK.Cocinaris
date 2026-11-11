using System.Net;
using System.Text.Json;
using Cocinaris.Api.Privacy;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0049 Slice 3: the retention job removes only what has no use, and never the shared recipe library.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private static async Task<int> CountAsync(ApplicationDbContext database, string sql) =>
        await database.Database.SqlQueryRaw<int>(sql).SingleAsync();

    [Fact]
    public async Task RetentionRemovesOnlyWhatHasNoUseAndKeepsTheRecipeLibrary()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "retention");
        var csrf = await CsrfAsync(client);
        var owner = (await JsonOf(await client.GetAsync("/api/v1/session"))).GetProperty("userId").GetGuid();

        // A recipe in the shared library, made by choosing an AI suggestion (decision D5 keeps it forever).
        await CreateLotAsync(client, csrf, "Peito de frango", 600m);
        provider.Enqueue(AiOperationRegistry.SuggestCandidates, request => AiProviderInvocationResult.Success(FakeAiProvider.BuildDefaultContent(request).Replace("pantry staples", "basic staples"), "fake-model", 100, 50));
        provider.Enqueue(AiOperationRegistry.ExpandSelected, request => AiProviderInvocationResult.Success(GenericExpansion(FakeAiProvider.BuildDefaultContent(request)), "fake-model", 100, 50));
        var suggestions = await RequestSuggestionsAsync(client, csrf);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, csrf, $"/api/v1/recipes/generation-sessions/{suggestions.SessionId}/selection", new { candidateId = "c1" }, Guid.NewGuid())).StatusCode);
        await CreateLotAsync(client, csrf, "Arroz", 800m);

        // Two abandoned plans, one long untouched, and one still being built.
        var oldPlan = (await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, days: 7), Guid.NewGuid()))).GetProperty("sessionId").GetGuid();
        Assert.True((await PostAsync(client, csrf, $"{Plans}/{oldPlan}/abandon", new { })).IsSuccessStatusCode);
        var recentPlan = (await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, days: 7), Guid.NewGuid()))).GetProperty("sessionId").GetGuid();
        Assert.True((await PostAsync(client, csrf, $"{Plans}/{recentPlan}/abandon", new { })).IsSuccessStatusCode);
        var buildingPlan = (await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, days: 7), Guid.NewGuid()))).GetProperty("sessionId").GetGuid();

        var gone = Guid.NewGuid();
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True(await database.RecipeIdeas.CountAsync() > 0);
            Assert.True(await CountAsync(database, "SELECT count(*)::int AS \"Value\" FROM platform.idempotency_records") >= 2);
            Assert.True(await CountAsync(database, "SELECT count(*)::int AS \"Value\" FROM ai.usage_ledger") >= 2);
            var now = DateTimeOffset.UtcNow;
            await database.Database.ExecuteSqlRawAsync("""
                -- The library is ten years old and stays.
                UPDATE recipes.recipe_ideas SET "CreatedAt" = now() - interval '10 years';
                -- One stored reply and one usage row past their limits, one of each just inside.
                UPDATE platform.idempotency_records SET "CreatedAt" = now() - interval '31 days' WHERE "Id" = (SELECT "Id" FROM platform.idempotency_records ORDER BY "CreatedAt" LIMIT 1);
                UPDATE platform.idempotency_records SET "CreatedAt" = now() - interval '29 days' WHERE "Id" = (SELECT "Id" FROM platform.idempotency_records ORDER BY "CreatedAt" DESC LIMIT 1);
                UPDATE ai.usage_ledger SET "CreatedAt" = now() - interval '14 months' WHERE "Id" = (SELECT "Id" FROM ai.usage_ledger ORDER BY "CreatedAt" LIMIT 1);
                UPDATE ai.usage_ledger SET "CreatedAt" = now() - interval '12 months' WHERE "Id" = (SELECT "Id" FROM ai.usage_ledger ORDER BY "CreatedAt" DESC LIMIT 1);
                """);
            await database.Database.ExecuteSqlAsync($"""
                UPDATE planning.shopping_plan_sessions SET "UpdatedAt" = now() - interval '31 days' WHERE "Id" IN ({oldPlan}, {buildingPlan});
                UPDATE planning.shopping_plan_sessions SET "UpdatedAt" = now() - interval '10 days' WHERE "Id" = {recentPlan};
                INSERT INTO planning.push_delivery_days ("OwnerUserId", "LastSentOn") VALUES ({owner}, CURRENT_DATE - 40);
                INSERT INTO platform.account_deletions ("Id", "SubjectHash", "OwnerUserId", "State", "RequestedAt", "CompletedAt")
                VALUES ({Guid.NewGuid()}, 'old', NULL, 'Completed', {now.AddDays(-40)}, {now.AddDays(-40)}),
                       ({Guid.NewGuid()}, 'recent', NULL, 'Completed', {now.AddDays(-20)}, {now.AddDays(-20)});
                -- A queued name and a dish image that still name an account that no longer exists.
                INSERT INTO ingredients.resolution_queue ("NormalizedText", "ExactText", "State", "Attempts", "CreatedAt", "UpdatedAt", "NextAttemptAt", "Evaluation", "Reason", "RequestedByUserId")
                VALUES ('fruta-do-conde', 'Fruta-do-conde', 'Unresolved', 1, now(), now(), now(), '', '', {gone});
                INSERT INTO recipes.recipe_thumbnails ("RenderKey", "VisualIdentityKey", "DescriptorJson", "State", "Attempts", "CreatedAt", "UpdatedAt", "NextAttemptAt", "MediaType", "FailureCode", "RequestedByUserId")
                VALUES ('retention-test', 'retention-test', CAST({"{}"} AS jsonb), 'Failed', 3, now(), now(), now() + interval '30 days', 'image/webp', 'test', {gone});
                """);
        }

        int ideasBefore, queueBefore, thumbnailsBefore;
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ideasBefore = await database.RecipeIdeas.CountAsync();
            queueBefore = await CountAsync(database, "SELECT count(*)::int AS \"Value\" FROM ingredients.resolution_queue");
            thumbnailsBefore = await CountAsync(database, "SELECT count(*)::int AS \"Value\" FROM recipes.recipe_thumbnails");
            Assert.True(queueBefore > 0, "No queued ingredient name to keep.");
            Assert.True(thumbnailsBefore > 0, "No dish image to keep.");
        }

        IReadOnlyDictionary<string, int> counts;
        using (var scope = host.Services.CreateScope())
        {
            counts = await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(CancellationToken.None);
        }

        Assert.Equal((1, 1, 1, 1, 1), (counts["idempotency"], counts["ai_usage"], counts["deletion_tombstones"], counts["push_delivery_days"], counts["abandoned_plans"]));
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Nothing of the library, the images, or the queued names was deleted.
            Assert.Equal(ideasBefore, await database.RecipeIdeas.CountAsync());
            Assert.Equal(queueBefore, await CountAsync(database, "SELECT count(*)::int AS \"Value\" FROM ingredients.resolution_queue"));
            Assert.Equal(thumbnailsBefore, await CountAsync(database, "SELECT count(*)::int AS \"Value\" FROM recipes.recipe_thumbnails"));
            Assert.Equal(0, await CountAsync(database, $"SELECT count(*)::int AS \"Value\" FROM ingredients.resolution_queue WHERE \"RequestedByUserId\" = '{gone}'"));
            Assert.Equal(0, await CountAsync(database, $"SELECT count(*)::int AS \"Value\" FROM recipes.recipe_thumbnails WHERE \"RequestedByUserId\" = '{gone}'"));
            // Only the old abandoned plan went; the recent one and the plan being built stay, with the person's data.
            var plans = await database.ShoppingPlanSessions.AsNoTracking().Select(item => item.Id).ToListAsync();
            Assert.DoesNotContain(oldPlan, plans);
            Assert.Contains(recentPlan, plans);
            Assert.Contains(buildingPlan, plans);
            Assert.Equal(2, await CountAsync(database, $"SELECT count(*)::int AS \"Value\" FROM inventory.lots WHERE \"OwnerUserId\" = '{owner}'"));
            Assert.Equal(1, await CountAsync(database, "SELECT count(*)::int AS \"Value\" FROM platform.account_deletions"));
        }

        // A second run finds nothing more.
        using (var scope = host.Services.CreateScope())
        {
            counts = await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(CancellationToken.None);
        }

        Assert.All(counts, pair => Assert.Equal(0, pair.Value));
    }
}
