using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Api.Privacy;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Infrastructure.Persistence.AccountData;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0049 Slice 2: an account deletion erases the person's rows, and only theirs, at once and atomically.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private const string Deletion = "/api/v1/privacy/account-deletion";

    private static async Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, string csrf, string confirmation, DateTimeOffset? signedInAt = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Deletion) { Content = JsonContent.Create(new { confirmation }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (signedInAt is { } at)
        {
            request.Headers.Add("X-Test-Signed-In-At", at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SessionAsync(HttpClient client, DateTimeOffset? signedInAt = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/session");
        if (signedInAt is { } at)
        {
            request.Headers.Add("X-Test-Signed-In-At", at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }

        return await client.SendAsync(request);
    }

    /// <summary>Rows per catalog table and exemption for one owner id, straight from the database.</summary>
    private static async Task<Dictionary<string, int>> RowsOfAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, Guid owner)
    {
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var tables = AccountDataCatalog.Tables.Select(table => (table.Schema, table.Name, table.OwnerColumn))
            .Concat(AccountDataCatalog.Exemptions.Where(item => item.Erasure != AccountDataErasure.SelfManaged).Select(item => (item.Schema, item.Name, item.OwnerColumn)));
        foreach (var (schema, name, column) in tables)
        {
            // Names come from the catalog, never from input; the id is a parameter.
            var sql = $"SELECT count(*)::int AS \"Value\" FROM \"{schema}\".\"{name}\" WHERE \"{column}\" = @p0";
            counts[$"{schema}.{name}"] = await database.Database.SqlQueryRaw<int>(sql, new Npgsql.NpgsqlParameter("p0", owner)).SingleAsync();
        }

        return counts;
    }

    private static async Task<int> RunDeletionsAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        // Deletions run as jobs (PLAN-0039 Slice 3); this counts the deletions the run completed.
        var deletions = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AccountDeletions;
        var before = await deletions.CountAsync(item => item.State == "Completed");
        await scope.ServiceProvider.GetRequiredService<Cocinaris.Api.Jobs.JobProcessor>().RunDueAsync(CancellationToken.None);
        return await deletions.CountAsync(item => item.State == "Completed") - before;
    }

    [Fact]
    public async Task DeletingAnAccountErasesEveryRowOfThePersonAndNothingElse()
    {
        var (host, provider) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "privacy-delete");
        using var other = PlanClient(host, "privacy-delete-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        var owner = (await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("userId").GetGuid();
        await CreateMealRecipeAsync(client, csrf, "Bolo de Apagar", "snack", "Farinha");
        await CreateLotAsync(client, csrf, "Farinha", 500m);
        var plan = await JsonOf(await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, days: 7), Guid.NewGuid()));
        // An AI batch leaves usage rows, which are kept anonymized (D4).
        provider.Enqueue(AiOperationRegistry.SuggestPlanDeck, Snacks("Apagar"));
        await PostAsync(client, csrf, $"{Plans}/{plan.GetProperty("sessionId").GetGuid()}/batches", new { mealType = "snack" }, Guid.NewGuid());
        await CreateMealRecipeAsync(other, otherCsrf, "Bolo que Fica", "snack", "Queijo");
        await CreateLotAsync(other, otherCsrf, "Queijo", 300m);
        var otherOwner = (await other.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("userId").GetGuid();
        var before = await RowsOfAsync(host, owner);
        var otherBefore = await RowsOfAsync(host, otherOwner);
        Assert.True(before["recipes.recipes"] > 0 && before["inventory.lots"] > 0 && before["ai.usage_ledger"] > 0 && before["identity.users"] == 1);

        // The word must be typed, and the sign-in must be recent (E1).
        Assert.Equal(HttpStatusCode.BadRequest, (await DeleteAccountAsync(client, csrf, "sim")).StatusCode);
        var stale = await DeleteAccountAsync(client, csrf, "EXCLUIR", DateTimeOffset.UtcNow.AddMinutes(-20));
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal("reauthentication_required", (await JsonOf(stale)).GetProperty("errorCode").GetString());
        var signedIn = DateTimeOffset.UtcNow.AddMinutes(-1);
        var accepted = await DeleteAccountAsync(client, csrf, " excluir ", signedIn);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        // Every session from before the request ends, and a new sign-in waits for the erasure.
        var ended = await SessionAsync(client, signedIn);
        Assert.Equal(HttpStatusCode.Unauthorized, ended.StatusCode);
        Assert.Equal("session_ended", (await JsonOf(ended)).GetProperty("errorCode").GetString());
        var waiting = await SessionAsync(client);
        Assert.Equal("account_deletion_in_progress", (await JsonOf(waiting)).GetProperty("errorCode").GetString());

        Assert.Equal(1, await RunDeletionsAsync(host));
        var after = await RowsOfAsync(host, owner);
        Assert.All(after, pair => Assert.True(pair.Value == 0, $"{pair.Key} still has {pair.Value} rows of the deleted account."));
        Assert.Equal(otherBefore, await RowsOfAsync(host, otherOwner));
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // The usage rows stay, under one id that names nobody.
            Assert.Equal(before["ai.usage_ledger"], await database.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM ai.usage_ledger WHERE \"OwnerUserId\" NOT IN (SELECT \"Id\" FROM identity.users)").SingleAsync());
            var tombstone = await database.AccountDeletions.AsNoTracking().SingleAsync(item => item.CountsJson != null && item.SubjectHash == SessionGuard.HashOf("https://integration.test", "privacy-delete"));
            Assert.Equal(("Completed", (Guid?)null), (tombstone.State, tombstone.OwnerUserId));
            Assert.DoesNotContain("privacy-delete", tombstone.CountsJson!);
        }

        // Nothing is left to do, and an old session still ends.
        Assert.Equal(0, await RunDeletionsAsync(host));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionAsync(client, signedIn)).StatusCode);
        // Signing in again starts an empty account (D2).
        var fresh = await SessionAsync(client);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        var newOwner = (await JsonOf(fresh)).GetProperty("userId").GetGuid();
        Assert.NotEqual(owner, newOwner);
        Assert.Equal(0, (await RowsOfAsync(host, newOwner)).Where(pair => pair.Key != "identity.users").Sum(pair => pair.Value));
    }

    [Fact]
    public async Task AFailedErasureChangesNothingAndTheRetryCompletesIt()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "privacy-delete-retry");
        var csrf = await CsrfAsync(client);
        var owner = (await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("userId").GetGuid();
        await CreateMealRecipeAsync(client, csrf, "Bolo Teimoso", "snack", "Farinha");
        await CreateLotAsync(client, csrf, "Farinha", 500m);
        var before = await RowsOfAsync(host, owner);

        using (var scope = host.Services.CreateScope())
        {
            // Fails the erasure of this account's recipes, after the pantry and other tables were already touched.
            // The id is a Guid formatted by the test, never input.
            var block = $"""
                CREATE OR REPLACE FUNCTION recipes.test_block_{owner:N}() RETURNS trigger AS $$
                BEGIN IF OLD."OwnerUserId" = '{owner}' THEN RAISE EXCEPTION 'blocked by test'; END IF; RETURN OLD; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER test_block_{owner:N} BEFORE DELETE ON recipes.recipes FOR EACH ROW EXECUTE FUNCTION recipes.test_block_{owner:N}();
                """;
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync(block);
        }

        Assert.Equal(HttpStatusCode.Accepted, (await DeleteAccountAsync(client, csrf, "DELETE")).StatusCode);
        Assert.Equal(0, await RunDeletionsAsync(host));
        // One transaction: nothing of the account was erased.
        Assert.Equal(before, await RowsOfAsync(host, owner));

        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pending = await database.AccountDeletions.SingleAsync(item => item.OwnerUserId == owner);
            Assert.Equal("Pending", pending.State);
            // The attempt lives on the deletion's job (PLAN-0039 Slice 3).
            var subject = pending.Id.ToString();
            var job = await database.Jobs.AsNoTracking().SingleAsync(item => item.SubjectKey == subject);
            Assert.Equal(("account_deletion", "Queued", 1, "PostgresException"), (job.Kind, job.State, job.Attempts, job.LastFailureCode));
            var unblock = $"DROP TRIGGER test_block_{owner:N} ON recipes.recipes; DROP FUNCTION recipes.test_block_{owner:N}();";
            await database.Database.ExecuteSqlRawAsync(unblock);
            // The retry waits a minute; the test does not.
            await database.Jobs.Where(item => item.SubjectKey == subject).ExecuteUpdateAsync(update => update.SetProperty(item => item.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        var retried = await RunDeletionsAsync(host);
        using (var scope = host.Services.CreateScope())
        {
            var state = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AccountDeletions.AsNoTracking().SingleAsync(item => item.SubjectHash == SessionGuard.HashOf("https://integration.test", "privacy-delete-retry"));
            Assert.True(retried == 1, $"Retry gave {retried}: {state.State}");
        }

        Assert.All(await RowsOfAsync(host, owner), pair => Assert.Equal(0, pair.Value));
    }
}
