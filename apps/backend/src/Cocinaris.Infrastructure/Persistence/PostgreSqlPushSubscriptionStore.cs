using Cocinaris.Modules.Planning.Application;
using Microsoft.EntityFrameworkCore;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>PostgreSQL-backed push subscriptions (ADR-0013). Every write except the day claim's first insert is owner-scoped by its filter.</summary>
public sealed class PostgreSqlPushSubscriptionStore(ApplicationDbContext database) : IPushSubscriptionStore
{
    /// <inheritdoc />
    public async Task UpsertAsync(PushSubscriptionEntry subscription, DateTimeOffset now, int maxPerOwner, CancellationToken cancellationToken)
    {
        // An endpoint belongs to one browser, so a second owner subscribing on it takes it over. That
        // owner starts fresh; repeating a subscribe keeps its age, which decides what is pruned.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO planning.push_subscriptions ("Id", "OwnerUserId", "Endpoint", "P256dh", "Auth", "CreatedAt")
            VALUES ({Guid.CreateVersion7()}, {subscription.OwnerUserId}, {subscription.Endpoint}, {subscription.P256dh}, {subscription.Auth}, {now})
            ON CONFLICT ("Endpoint") DO UPDATE SET
                "P256dh" = EXCLUDED."P256dh",
                "Auth" = EXCLUDED."Auth",
                "CreatedAt" = CASE WHEN planning.push_subscriptions."OwnerUserId" = EXCLUDED."OwnerUserId" THEN planning.push_subscriptions."CreatedAt" ELSE EXCLUDED."CreatedAt" END,
                "LastSuccessAt" = CASE WHEN planning.push_subscriptions."OwnerUserId" = EXCLUDED."OwnerUserId" THEN planning.push_subscriptions."LastSuccessAt" END,
                "OwnerUserId" = EXCLUDED."OwnerUserId"
            """, cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM planning.push_subscriptions
            WHERE "OwnerUserId" = {subscription.OwnerUserId} AND "Id" NOT IN (
                SELECT "Id" FROM planning.push_subscriptions WHERE "OwnerUserId" = {subscription.OwnerUserId} ORDER BY "CreatedAt" DESC, "Id" DESC LIMIT {maxPerOwner})
            """, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid ownerUserId, string endpoint, CancellationToken cancellationToken) =>
        database.PushSubscriptions.Where(item => item.OwnerUserId == ownerUserId && item.Endpoint == endpoint).ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListOwnersAsync(int limit, CancellationToken cancellationToken) =>
        await database.PushSubscriptions.AsNoTracking().Select(item => item.OwnerUserId).Distinct().OrderBy(item => item).Take(limit).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PushSubscriptionEntry>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        await database.PushSubscriptions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new PushSubscriptionEntry(item.OwnerUserId, item.Endpoint, item.P256dh, item.Auth))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task MarkDeliveredAsync(Guid ownerUserId, string endpoint, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.PushSubscriptions.Where(item => item.OwnerUserId == ownerUserId && item.Endpoint == endpoint)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LastSuccessAt, now), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryClaimDayAsync(Guid ownerUserId, DateOnly day, CancellationToken cancellationToken)
    {
        // The filter on the update makes this the single point that prevents a second notification:
        // zero rows means another run, or an earlier one today, already took the day.
        var affected = await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO planning.push_delivery_days ("OwnerUserId", "LastSentOn")
            VALUES ({ownerUserId}, {day})
            ON CONFLICT ("OwnerUserId") DO UPDATE SET "LastSentOn" = EXCLUDED."LastSentOn"
            WHERE planning.push_delivery_days."LastSentOn" < EXCLUDED."LastSentOn"
            """, cancellationToken);
        return affected == 1;
    }
}
