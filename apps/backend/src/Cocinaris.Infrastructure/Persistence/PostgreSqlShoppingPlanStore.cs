using System.Text.Json;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Planning.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>PostgreSQL-backed shopping plan store (PLAN-0062). Every query is owner-scoped.</summary>
public sealed class PostgreSqlShoppingPlanStore(ApplicationDbContext database) : IShoppingPlanStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] InProgress = [nameof(ShoppingPlanStatus.Building), nameof(ShoppingPlanStatus.Reviewing)];

    /// <inheritdoc />
    public async Task<ShoppingPlanSession?> FindAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken)
    {
        var record = await database.ShoppingPlanSessions.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.Id == sessionId, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<ShoppingPlanSession?> FindByCreationKeyAsync(Guid ownerUserId, Guid creationKey, CancellationToken cancellationToken)
    {
        var record = await database.ShoppingPlanSessions.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.CreationKey == creationKey, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<ShoppingPlanSession?> FindCurrentAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var record = await database.ShoppingPlanSessions.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && InProgress.Contains(item.Status), cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<ShoppingPlanSession?> FindLatestAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var record = await database.ShoppingPlanSessions.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId).OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ShoppingPlanSession>> ListFinishedAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken)
    {
        var records = await database.ShoppingPlanSessions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.Status == nameof(ShoppingPlanStatus.Finished))
            .OrderByDescending(item => item.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<ShoppingPlanSession?> FindFinishedByMenuAsync(Guid ownerUserId, Guid menuPlanId, CancellationToken cancellationToken)
    {
        var record = await database.ShoppingPlanSessions.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.MenuPlanId == menuPlanId && item.Status == nameof(ShoppingPlanStatus.Finished), cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(ShoppingPlanSession session, CancellationToken cancellationToken)
    {
        database.ShoppingPlanSessions.Add(ToRecord(session));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(ShoppingPlanSession session, CancellationToken cancellationToken)
    {
        var routeProgress = session.RouteProgress is null ? null : JsonSerializer.Serialize(session.RouteProgress, SerializerOptions);
        var deckPreference = session.DeckPreference is null ? null : JsonSerializer.Serialize(session.DeckPreference, SerializerOptions);
        // Batch fields change only through their atomic methods, so a save cannot overwrite a batch taken meanwhile.
        await database.ShoppingPlanSessions.Where(item => item.OwnerUserId == session.OwnerUserId && item.Id == session.Id).ExecuteUpdateAsync(
            update => update
                .SetProperty(item => item.Status, session.Status.ToString())
                .SetProperty(item => item.ConsecutiveRejections, session.ConsecutiveRejections)
                .SetProperty(item => item.LastDecidedCardId, session.LastDecidedCardId)
                .SetProperty(item => item.RouteProgressJson, routeProgress)
                .SetProperty(item => item.DeckPreferenceJson, deckPreference)
                .SetProperty(item => item.UpdatedAt, session.UpdatedAt),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryTakeBatchAsync(Guid ownerUserId, Guid sessionId, Guid key, CancellationToken cancellationToken) =>
        await database.ShoppingPlanSessions
            .Where(item => item.OwnerUserId == ownerUserId && item.Id == sessionId && item.BatchCount < ShoppingPlanSession.MaxBatches && (item.LastBatchKey == null || item.LastBatchKey != key))
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.BatchCount, item => item.BatchCount + 1).SetProperty(item => item.LastBatchKey, key), cancellationToken) == 1;

    /// <inheritdoc />
    public async Task ReturnBatchAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken) =>
        await database.ShoppingPlanSessions
            .Where(item => item.OwnerUserId == ownerUserId && item.Id == sessionId && item.BatchCount > 0)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.BatchCount, item => item.BatchCount - 1).SetProperty(item => item.LastBatchKey, (Guid?)null), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ShoppingPlanCard>> ListCardsAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken)
    {
        var records = await database.ShoppingPlanCards.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && item.SessionId == sessionId)
            .OrderBy(item => item.FirstSeenOrder)
            .ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task AddCardsAsync(IReadOnlyList<ShoppingPlanCard> cards, CancellationToken cancellationToken)
    {
        database.ShoppingPlanCards.AddRange(cards.Select(ToRecord));
        await SaveAndClearAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveCardAsync(ShoppingPlanCard card, CancellationToken cancellationToken)
    {
        database.ShoppingPlanCards.Update(ToRecord(card));
        await SaveAndClearAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryDecideCardAsync(ShoppingPlanCard card, CancellationToken cancellationToken) =>
        await database.ShoppingPlanCards
            .Where(item => item.OwnerUserId == card.OwnerUserId && item.Id == card.Id && item.State == nameof(ShoppingPlanCardState.Unseen))
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.State, card.State.ToString())
                .SetProperty(item => item.AcceptMode, card.AcceptMode)
                .SetProperty(item => item.Repeats, card.Repeats)
                .SetProperty(item => item.ExpansionState, card.ExpansionState.ToString())
                .SetProperty(item => item.DecidedAt, card.DecidedAt),
                cancellationToken) == 1;

    /// <inheritdoc />
    public async Task DeleteCardsAsync(Guid ownerUserId, Guid sessionId, IReadOnlyCollection<ShoppingPlanCardState> states, CancellationToken cancellationToken)
    {
        var names = states.Select(state => state.ToString()).ToList();
        await database.ShoppingPlanCards.Where(item => item.OwnerUserId == ownerUserId && item.SessionId == sessionId && names.Contains(item.State)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAbandonedBeforeAsync(Guid ownerUserId, DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        await database.ShoppingPlanSessions
            .Where(item => item.OwnerUserId == ownerUserId && item.Status == nameof(ShoppingPlanStatus.Abandoned) && item.UpdatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

    private async Task SaveAndClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private static ShoppingPlanSession ToDomain(ShoppingPlanSessionRecord record) => new()
    {
        Id = record.Id,
        OwnerUserId = record.OwnerUserId,
        MenuPlanId = record.MenuPlanId,
        CreationKey = record.CreationKey,
        Setup = JsonSerializer.Deserialize<ShoppingPlanSetup>(record.SetupJson, SerializerOptions)!,
        Status = Enum.Parse<ShoppingPlanStatus>(record.Status),
        BatchCount = record.BatchCount,
        LastBatchKey = record.LastBatchKey,
        ConsecutiveRejections = record.ConsecutiveRejections,
        LastDecidedCardId = record.LastDecidedCardId,
        PrivateDirection = record.PrivateDirection,
        RouteProgress = record.RouteProgressJson is null ? null : JsonSerializer.Deserialize<ShoppingPlanRouteProgress>(record.RouteProgressJson, SerializerOptions),
        DeckPreference = record.DeckPreferenceJson is null ? null : JsonSerializer.Deserialize<ShoppingPlanDeckPreference>(record.DeckPreferenceJson, SerializerOptions),
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };

    private static ShoppingPlanSessionRecord ToRecord(ShoppingPlanSession session) => new()
    {
        Id = session.Id,
        OwnerUserId = session.OwnerUserId,
        MenuPlanId = session.MenuPlanId,
        CreationKey = session.CreationKey,
        Status = session.Status.ToString(),
        SetupJson = JsonSerializer.Serialize(session.Setup, SerializerOptions),
        BatchCount = session.BatchCount,
        LastBatchKey = session.LastBatchKey,
        ConsecutiveRejections = session.ConsecutiveRejections,
        LastDecidedCardId = session.LastDecidedCardId,
        PrivateDirection = session.PrivateDirection,
        CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt
    };

    private static ShoppingPlanCard ToDomain(ShoppingPlanCardRecord record) => new()
    {
        Id = record.Id,
        SessionId = record.SessionId,
        OwnerUserId = record.OwnerUserId,
        Source = Enum.Parse<ShoppingPlanCardSource>(record.Source),
        RecipeId = record.RecipeId,
        CandidateJson = record.CandidateJson,
        Snapshot = JsonSerializer.Deserialize<ShoppingPlanCardSnapshot>(record.SnapshotJson, SerializerOptions)!,
        State = Enum.Parse<ShoppingPlanCardState>(record.State),
        RejectReason = record.RejectReason,
        RejectIngredientKey = record.RejectIngredientKey,
        AcceptMode = record.AcceptMode,
        Repeats = record.Repeats,
        ExpansionState = Enum.Parse<ShoppingPlanExpansionState>(record.ExpansionState),
        ExpansionKey = record.ExpansionKey,
        FirstSeenOrder = record.FirstSeenOrder,
        DecidedAt = record.DecidedAt
    };

    private static ShoppingPlanCardRecord ToRecord(ShoppingPlanCard card) => new()
    {
        Id = card.Id,
        SessionId = card.SessionId,
        OwnerUserId = card.OwnerUserId,
        Source = card.Source.ToString(),
        RecipeId = card.RecipeId,
        CandidateJson = card.CandidateJson,
        SnapshotJson = JsonSerializer.Serialize(card.Snapshot, SerializerOptions),
        State = card.State.ToString(),
        RejectReason = card.RejectReason,
        RejectIngredientKey = card.RejectIngredientKey,
        AcceptMode = card.AcceptMode,
        Repeats = card.Repeats,
        ExpansionState = card.ExpansionState.ToString(),
        ExpansionKey = card.ExpansionKey,
        FirstSeenOrder = card.FirstSeenOrder,
        DecidedAt = card.DecidedAt
    };
}
