using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Api.Inventory;
using Cocinaris.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0056 Phase 4: automatic removal of pantry lots after a chosen day.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task LotsAreRemovedOnlyAfterTheirDayAndTheRemovalIsInTheirHistory()
    {
        using var client = Client("auto-remover");
        var csrf = await CsrfAsync(client);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var created = await PostAsync(client, csrf, "/api/v1/inventory/lots", new { productName = "Requeijão cremoso", quantity = new { measuredValue = 100m, unit = "Gram" }, storageLocation = "Refrigerator", autoRemoveOn = today.AddDays(3) }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(today.AddDays(3).ToString("yyyy-MM-dd"), body.GetProperty("autoRemoveOn").GetString());
        var past = await PostAsync(client, csrf, "/api/v1/inventory/lots", new { productName = "Leite", quantity = new { measuredValue = 1000m, unit = "Milliliter" }, storageLocation = "Refrigerator", autoRemoveOn = today.AddDays(-5) }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, past.StatusCode);

        var expired = await CreateLotAsync(client, csrf, "Presunto", 200m);
        var lastDay = await CreateLotAsync(client, csrf, "Queijo", 200m);
        var later = body.GetProperty("lotId").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            // Dates already in the past are set directly; the API rightly refuses them.
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await database.Database.ExecuteSqlInterpolatedAsync($"""UPDATE inventory.lots SET "AutoRemoveOn" = {today.AddDays(-1)} WHERE "Id" = {expired}""");
            await database.Database.ExecuteSqlInterpolatedAsync($"""UPDATE inventory.lots SET "AutoRemoveOn" = {today} WHERE "Id" = {lastDay}""");
        }

        // The owner has no time zone, so their calendar is UTC: yesterday's lot goes, today's stays through today.
        var removed = await InventoryAutoRemovalWorker.RunOnceAsync(_factory.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, CancellationToken.None);

        Assert.Equal(1, removed);
        var lots = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/lots?status=active&pageSize=100");
        var ids = lots.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("lotId").GetGuid()).ToList();
        Assert.DoesNotContain(expired, ids);
        Assert.Contains(lastDay, ids);
        Assert.Contains(later, ids);
        using (var scope = _factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var deletion = await database.Transactions.AsNoTracking().SingleAsync(item => item.LotId == expired && item.Type == "Deleted");
            Assert.Equal("auto_remove_date", deletion.ReasonCode);
            Assert.True(await database.AuditEvents.AnyAsync(item => item.TargetId == expired && item.EventName == "inventory.lot.auto_removed"));
        }

        Assert.Equal(0, await InventoryAutoRemovalWorker.RunOnceAsync(_factory.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, CancellationToken.None));
    }
}
