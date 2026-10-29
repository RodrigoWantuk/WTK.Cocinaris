using Cocinaris.Modules.Ai.Usage;
using Microsoft.EntityFrameworkCore;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed authoritative AI usage ledger store.
/// Reservations use a transaction-scoped advisory lock so concurrent ceiling checks cannot oversell budget.
/// </summary>
public sealed class PostgreSqlAiUsageLedgerStore(ApplicationDbContext database) : IAiUsageLedgerStore
{
    /// <summary>
    /// Dedicated <c>pg_advisory_xact_lock</c> key for Cocinaris AI usage reservation serialization.
    /// Must not collide with other advisory locks in this database.
    /// </summary>
    public const long AiUsageReservationAdvisoryLockKey = 728_401_628_001_028L;

    /// <inheritdoc />
    public async Task<AiUsageReservationResult> TryReserveAsync(
        Guid ownerUserId,
        string operation,
        int estimatedUnits,
        long estimatedCostMicroUsd,
        int estimatedTokens,
        string correlationId,
        DateTimeOffset now,
        AiUsageOptions ceilings,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AiUsageReservationAdvisoryLockKey})", cancellationToken);

        var openReservations = await database.AiUsageLedgerEntries
            .CountAsync(item => item.OwnerUserId == ownerUserId && item.Status == nameof(AiUsageEntryStatus.Reserved), cancellationToken);
        if (openReservations >= ceilings.UserConcurrencyCeiling)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AiUsageReservationResult.Rejected(AiUsageReservationOutcome.ConcurrencyExhausted);
        }

        var (dayStart, dayEnd) = UtcDayBounds(now);
        var globalUnitsToday = await SumUnitsInRangeAsync(null, dayStart, dayEnd, cancellationToken);
        if (globalUnitsToday + estimatedUnits > ceilings.GlobalDailyUnitCeiling)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AiUsageReservationResult.Rejected(AiUsageReservationOutcome.GlobalBudgetExhausted);
        }

        var ownerUnitsToday = await SumUnitsInRangeAsync(ownerUserId, dayStart, dayEnd, cancellationToken);
        if (ownerUnitsToday + estimatedUnits > ceilings.UserDailyUnitCeiling)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AiUsageReservationResult.Rejected(AiUsageReservationOutcome.UserBudgetExhausted);
        }

        // Monthly spend: real cost of closed calls plus the estimate of calls still running.
        var monthStart = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthSpent = await database.AiUsageLedgerEntries
            .Where(item => item.CreatedAt >= monthStart && item.CreatedAt < monthStart.AddMonths(1))
            .SumAsync(item => item.CostMicroUsd ?? (item.Status == nameof(AiUsageEntryStatus.Reserved) ? item.EstimatedCostMicroUsd : 0), cancellationToken);
        if (monthSpent + estimatedCostMicroUsd > ceilings.MonthlyCostCeilingMicroUsd)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AiUsageReservationResult.Rejected(AiUsageReservationOutcome.MonthlyCostExhausted);
        }

        // Rolling 24-hour token windows: reported tokens of closed calls plus estimates of open ones.
        var windowStart = now.AddHours(-24);
        var window = database.AiUsageLedgerEntries.Where(item => item.CreatedAt > windowStart && item.CreatedAt <= now);
        var globalTokens = await SumWindowTokensAsync(window, cancellationToken);
        if (globalTokens + estimatedTokens > ceilings.GlobalTokensPer24Hours)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AiUsageReservationResult.Rejected(AiUsageReservationOutcome.GlobalTokenWindowExhausted);
        }

        var ownerTokens = await SumWindowTokensAsync(window.Where(item => item.OwnerUserId == ownerUserId), cancellationToken);
        if (ownerTokens + estimatedTokens > ceilings.UserTokensPer24Hours)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AiUsageReservationResult.Rejected(AiUsageReservationOutcome.UserTokenWindowExhausted);
        }

        var id = Guid.NewGuid();
        database.AiUsageLedgerEntries.Add(new AiUsageLedgerRecord
        {
            Id = id,
            OwnerUserId = ownerUserId,
            Operation = operation,
            Status = nameof(AiUsageEntryStatus.Reserved),
            ReservedUnits = estimatedUnits,
            EstimatedCostMicroUsd = estimatedCostMicroUsd,
            EstimatedTokens = estimatedTokens,
            CorrelationId = correlationId,
            CreatedAt = now
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AiUsageReservationResult.Success(id);
    }

    /// <inheritdoc />
    public async Task<int> SumGlobalUnitsForDayAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var (dayStart, dayEnd) = UtcDayBounds(asOf);
        return await SumUnitsInRangeAsync(null, dayStart, dayEnd, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> SumOwnerUnitsForDayAsync(Guid ownerUserId, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var (dayStart, dayEnd) = UtcDayBounds(asOf);
        return await SumUnitsInRangeAsync(ownerUserId, dayStart, dayEnd, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountOpenReservationsAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        database.AiUsageLedgerEntries.AsNoTracking()
            .CountAsync(item => item.OwnerUserId == ownerUserId && item.Status == nameof(AiUsageEntryStatus.Reserved), cancellationToken);

    /// <inheritdoc />
    public async Task SettleAsync(Guid reservationId, int settledUnits, AiCallUsage usage, long costMicroUsd, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entry = await database.AiUsageLedgerEntries.SingleAsync(item => item.Id == reservationId, cancellationToken);
        ApplySettlement(entry, settledUnits, usage, costMicroUsd, now);
        await database.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid reservationId, AiCallUsage? usage, long costMicroUsd, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entry = await database.AiUsageLedgerEntries.SingleAsync(item => item.Id == reservationId, cancellationToken);
        ApplyRelease(entry, usage, costMicroUsd, now);
        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Marks a tracked reserved entry settled; shared with the recipe unit of work so both write the same fields.</summary>
    /// <exception cref="InvalidOperationException">The entry is not reserved.</exception>
    public static void ApplySettlement(AiUsageLedgerRecord entry, int settledUnits, AiCallUsage usage, long costMicroUsd, DateTimeOffset now)
    {
        if (entry.Status != nameof(AiUsageEntryStatus.Reserved))
        {
            throw new InvalidOperationException("Only a reserved AI usage entry can be settled.");
        }

        entry.Status = nameof(AiUsageEntryStatus.Settled);
        entry.SettledUnits = settledUnits;
        entry.Provider = usage.Provider;
        entry.Model = usage.Model ?? "unknown";
        ApplyUsage(entry, usage, costMicroUsd);
        entry.ClosedAt = now;
    }

    /// <summary>Marks a tracked reserved entry released, keeping what a failed call still cost.</summary>
    /// <exception cref="InvalidOperationException">The entry is not reserved.</exception>
    public static void ApplyRelease(AiUsageLedgerRecord entry, AiCallUsage? usage, long costMicroUsd, DateTimeOffset now)
    {
        if (entry.Status != nameof(AiUsageEntryStatus.Reserved))
        {
            throw new InvalidOperationException("Only a reserved AI usage entry can be released.");
        }

        entry.Status = nameof(AiUsageEntryStatus.Released);
        if (usage is not null)
        {
            entry.Provider = usage.Provider;
            entry.Model = usage.Model;
            ApplyUsage(entry, usage, costMicroUsd);
        }
        else
        {
            entry.CostMicroUsd = 0;
        }

        entry.ClosedAt = now;
    }

    private static void ApplyUsage(AiUsageLedgerRecord entry, AiCallUsage usage, long costMicroUsd)
    {
        entry.PromptTokens = usage.PromptTokens;
        entry.CompletionTokens = usage.CompletionTokens;
        entry.LatencyMs = usage.LatencyMs;
        entry.Attempts = usage.Attempts;
        entry.QualityFlags = usage.QualityFlags;
        entry.CostMicroUsd = costMicroUsd;
    }

    private static async Task<long> SumWindowTokensAsync(IQueryable<AiUsageLedgerRecord> window, CancellationToken cancellationToken) =>
        await window.SumAsync(item => item.Status == nameof(AiUsageEntryStatus.Reserved)
            ? (long)item.EstimatedTokens
            : (long)((item.PromptTokens ?? 0) + (item.CompletionTokens ?? 0)), cancellationToken);

    private async Task<int> SumUnitsInRangeAsync(Guid? ownerUserId, DateTimeOffset dayStart, DateTimeOffset dayEnd, CancellationToken cancellationToken)
    {
        var query = database.AiUsageLedgerEntries
            .Where(item => item.CreatedAt >= dayStart && item.CreatedAt < dayEnd && item.Status != nameof(AiUsageEntryStatus.Released));
        if (ownerUserId is not null)
        {
            query = query.Where(item => item.OwnerUserId == ownerUserId.Value);
        }

        var rows = await query.Select(item => new { item.Status, item.ReservedUnits, item.SettledUnits }).ToListAsync(cancellationToken);
        return rows.Sum(item => item.Status == nameof(AiUsageEntryStatus.Settled) ? item.SettledUnits ?? item.ReservedUnits : item.ReservedUnits);
    }

    private static (DateTimeOffset DayStart, DateTimeOffset DayEnd) UtcDayBounds(DateTimeOffset asOf)
    {
        var dayStart = new DateTimeOffset(asOf.UtcDateTime.Date, TimeSpan.Zero);
        return (dayStart, dayStart.AddDays(1));
    }
}
