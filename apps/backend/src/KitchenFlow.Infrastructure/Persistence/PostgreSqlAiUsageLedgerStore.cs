using KitchenFlow.Modules.Ai.Usage;
using Microsoft.EntityFrameworkCore;

namespace KitchenFlow.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-backed authoritative AI usage ledger store.
/// Reservations use a transaction-scoped advisory lock so concurrent ceiling checks cannot oversell budget.
/// </summary>
public sealed class PostgreSqlAiUsageLedgerStore(ApplicationDbContext database) : IAiUsageLedgerStore
{
    /// <summary>
    /// Dedicated <c>pg_advisory_xact_lock</c> key for KitchenFlow AI usage reservation serialization.
    /// Must not collide with other advisory locks in this database.
    /// </summary>
    public const long AiUsageReservationAdvisoryLockKey = 728_401_628_001_028L;

    /// <inheritdoc />
    public async Task<AiUsageReservationResult> TryReserveAsync(
        Guid ownerUserId,
        string operation,
        int estimatedUnits,
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

        var id = Guid.NewGuid();
        database.AiUsageLedgerEntries.Add(new AiUsageLedgerRecord
        {
            Id = id,
            OwnerUserId = ownerUserId,
            Operation = operation,
            Status = nameof(AiUsageEntryStatus.Reserved),
            ReservedUnits = estimatedUnits,
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
    public async Task SettleAsync(
        Guid reservationId,
        int settledUnits,
        string provider,
        string model,
        int? promptTokens,
        int? completionTokens,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entry = await database.AiUsageLedgerEntries.SingleAsync(item => item.Id == reservationId, cancellationToken);
        if (entry.Status != nameof(AiUsageEntryStatus.Reserved))
        {
            throw new InvalidOperationException("Only a reserved AI usage entry can be settled.");
        }

        entry.Status = nameof(AiUsageEntryStatus.Settled);
        entry.SettledUnits = settledUnits;
        entry.Provider = provider;
        entry.Model = model;
        entry.PromptTokens = promptTokens;
        entry.CompletionTokens = completionTokens;
        entry.ClosedAt = now;
        await database.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid reservationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entry = await database.AiUsageLedgerEntries.SingleAsync(item => item.Id == reservationId, cancellationToken);
        if (entry.Status != nameof(AiUsageEntryStatus.Reserved))
        {
            throw new InvalidOperationException("Only a reserved AI usage entry can be released.");
        }

        entry.Status = nameof(AiUsageEntryStatus.Released);
        entry.ClosedAt = now;
        await database.SaveChangesAsync(cancellationToken);
    }

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
