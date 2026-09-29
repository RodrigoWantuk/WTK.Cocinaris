using System.Text.Json;
using KitchenFlow.Modules.Ai.Usage;
using KitchenFlow.Modules.Recipes.Application;
using KitchenFlow.Modules.Recipes.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KitchenFlow.Infrastructure.Persistence;

/// <summary>PostgreSQL-backed owner-scoped recipe generation session store.</summary>
public sealed class PostgreSqlRecipeGenerationStore(ApplicationDbContext database) : IRecipeGenerationStore
{
    /// <inheritdoc />
    public async Task<RecipeGenerationSessionClaimResult> TryClaimNewSessionAsync(RecipeGenerationSession session, CancellationToken cancellationToken)
    {
        database.RecipeGenerationSessions.Add(ToRecord(session));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return new RecipeGenerationSessionClaimResult(WasCreated: true, session);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            database.ChangeTracker.Clear();
            var existing = await FindByIdempotencyKeyAsync(session.OwnerUserId, session.IdempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException("Generation session unique conflict occurred but the existing session could not be reloaded.");
            return new RecipeGenerationSessionClaimResult(WasCreated: false, existing);
        }
    }

    /// <inheritdoc />
    public async Task<RecipeSelectionClaimResult> TryClaimSelectionAsync(
        Guid ownerUserId,
        Guid sessionId,
        string candidateId,
        Guid selectIdempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var keyOwner = await database.RecipeGenerationSessions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.SelectIdempotencyKey == selectIdempotencyKey, cancellationToken);
        if (keyOwner is not null)
        {
            var keyedSession = ToDomain(keyOwner);
            if (keyedSession.Id != sessionId)
            {
                return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.Conflict, keyedSession);
            }

            if (keyedSession.Status == RecipeGenerationSessionStatus.Selected
                && string.Equals(keyedSession.SelectedCandidateId, candidateId, StringComparison.Ordinal)
                && keyedSession.SelectIdempotencyKey == selectIdempotencyKey)
            {
                return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.AlreadySelected, keyedSession);
            }

            if (keyedSession.Status == RecipeGenerationSessionStatus.Expanding
                && string.Equals(keyedSession.SelectedCandidateId, candidateId, StringComparison.Ordinal)
                && keyedSession.SelectIdempotencyKey == selectIdempotencyKey)
            {
                return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.InProgress, keyedSession);
            }

            if (!string.Equals(keyedSession.SelectedCandidateId, candidateId, StringComparison.Ordinal)
                || keyedSession.SelectIdempotencyKey != selectIdempotencyKey)
            {
                return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.Conflict, keyedSession);
            }
        }

        var record = await database.RecipeGenerationSessions
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.Id == sessionId, cancellationToken);
        if (record is null)
        {
            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.NotFound, null);
        }

        var session = ToDomain(record);
        if (session.IsExpired(now))
        {
            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.NotReady, session);
        }

        if (session.Status == RecipeGenerationSessionStatus.Selected
            && string.Equals(session.SelectedCandidateId, candidateId, StringComparison.Ordinal)
            && session.SelectIdempotencyKey == selectIdempotencyKey)
        {
            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.AlreadySelected, session);
        }

        if (session.Status == RecipeGenerationSessionStatus.Expanding)
        {
            if (string.Equals(session.SelectedCandidateId, candidateId, StringComparison.Ordinal)
                && session.SelectIdempotencyKey == selectIdempotencyKey)
            {
                return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.InProgress, session);
            }

            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.Conflict, session);
        }

        if (session.Status != RecipeGenerationSessionStatus.CandidatesReady)
        {
            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.NotReady, session);
        }

        var affected = await database.RecipeGenerationSessions
            .Where(item => item.OwnerUserId == ownerUserId
                && item.Id == sessionId
                && item.Status == nameof(RecipeGenerationSessionStatus.CandidatesReady))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, nameof(RecipeGenerationSessionStatus.Expanding))
                    .SetProperty(item => item.SelectedCandidateId, candidateId)
                    .SetProperty(item => item.SelectIdempotencyKey, selectIdempotencyKey)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);

        if (affected == 1)
        {
            var claimed = await FindByIdAsync(ownerUserId, sessionId, cancellationToken)
                ?? throw new InvalidOperationException("Selection claim succeeded but the session could not be reloaded.");
            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.Claimed, claimed);
        }

        var raced = await FindByIdAsync(ownerUserId, sessionId, cancellationToken);
        if (raced is null)
        {
            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.NotFound, null);
        }

        if (raced.Status == RecipeGenerationSessionStatus.Expanding
            && string.Equals(raced.SelectedCandidateId, candidateId, StringComparison.Ordinal)
            && raced.SelectIdempotencyKey == selectIdempotencyKey)
        {
            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.InProgress, raced);
        }

        if (raced.Status == RecipeGenerationSessionStatus.Selected
            && string.Equals(raced.SelectedCandidateId, candidateId, StringComparison.Ordinal)
            && raced.SelectIdempotencyKey == selectIdempotencyKey)
        {
            return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.AlreadySelected, raced);
        }

        return new RecipeSelectionClaimResult(RecipeSelectionClaimOutcome.Conflict, raced);
    }

    /// <inheritdoc />
    public async Task<RecipeGenerationSession?> FindByIdempotencyKeyAsync(Guid ownerUserId, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        var record = await database.RecipeGenerationSessions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.IdempotencyKey == idempotencyKey, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<RecipeGenerationSession?> FindBySelectIdempotencyKeyAsync(Guid ownerUserId, Guid selectIdempotencyKey, CancellationToken cancellationToken)
    {
        var record = await database.RecipeGenerationSessions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.SelectIdempotencyKey == selectIdempotencyKey, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task<RecipeGenerationSession?> FindByIdAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken)
    {
        var record = await database.RecipeGenerationSessions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.Id == sessionId, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public Task<RecipeGenerationSession?> FindActiveAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken) =>
        FindByIdAsync(ownerUserId, sessionId, cancellationToken);

    /// <inheritdoc />
    public async Task SaveAsync(RecipeGenerationSession session, CancellationToken cancellationToken)
    {
        var record = await database.RecipeGenerationSessions
            .SingleAsync(item => item.OwnerUserId == session.OwnerUserId && item.Id == session.Id, cancellationToken);
        ApplySession(record, session);
        await database.SaveChangesAsync(cancellationToken);
    }

    internal static void ApplySession(RecipeGenerationSessionRecord record, RecipeGenerationSession session)
    {
        record.Status = session.Status.ToString();
        record.CandidatesSnapshotJson = session.CandidatesSnapshotJson;
        record.SelectedCandidateId = session.SelectedCandidateId;
        record.SelectedRecipeId = session.SelectedRecipeId;
        record.SelectIdempotencyKey = session.SelectIdempotencyKey;
        record.FailureReason = session.FailureReason;
        record.UpdatedAt = session.UpdatedAt;
    }

    internal static RecipeGenerationSession ToDomain(RecipeGenerationSessionRecord record) =>
        RecipeGenerationSession.Rehydrate(
            record.Id,
            record.OwnerUserId,
            record.IdempotencyKey,
            record.ExecutionMode,
            Enum.Parse<RecipeGenerationSessionStatus>(record.Status),
            record.CandidatesSnapshotJson,
            record.SelectedCandidateId,
            record.SelectedRecipeId,
            record.SelectIdempotencyKey,
            record.FailureReason,
            record.CreatedAt,
            record.UpdatedAt,
            record.ExpiresAt);

    private static RecipeGenerationSessionRecord ToRecord(RecipeGenerationSession session) => new()
    {
        Id = session.Id,
        OwnerUserId = session.OwnerUserId,
        IdempotencyKey = session.IdempotencyKey,
        ExecutionMode = session.ExecutionMode,
        Status = session.Status.ToString(),
        CandidatesSnapshotJson = session.CandidatesSnapshotJson,
        SelectedCandidateId = session.SelectedCandidateId,
        SelectedRecipeId = session.SelectedRecipeId,
        SelectIdempotencyKey = session.SelectIdempotencyKey,
        FailureReason = session.FailureReason,
        CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt,
        ExpiresAt = session.ExpiresAt
    };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

/// <summary>
/// PostgreSQL unit of work that finalizes cook-now suggest/expand outcomes with usage settle or release
/// in a single EF transaction. Concurrent duplicates are prevented by session claims; a crash between
/// provider return and this commit remains an unavoidable window.
/// </summary>
public sealed class PostgreSqlRecipeCookNowUnitOfWork(ApplicationDbContext database) : IRecipeCookNowUnitOfWork
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task CompleteSuggestAsync(
        RecipeGenerationSession session,
        Guid reservationId,
        int settledUnits,
        string provider,
        string model,
        int? promptTokens,
        int? completionTokens,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (session.Status != RecipeGenerationSessionStatus.CandidatesReady || string.IsNullOrWhiteSpace(session.CandidatesSnapshotJson))
        {
            throw new InvalidOperationException("CompleteSuggestAsync requires a session already transitioned to CandidatesReady with a validated snapshot.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await ApplySessionAndSettleAsync(session, reservationId, settledUnits, provider, model, promptTokens, completionTokens, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task FailSuggestAsync(
        RecipeGenerationSession session,
        Guid reservationId,
        string failureReason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        session.MarkFailed(failureReason, now);
        await ApplySessionAndReleaseAsync(session, reservationId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task FinalizeExpansionAsync(
        Recipe recipe,
        RecipeRevision revision,
        RecipeGenerationSession session,
        Guid reservationId,
        int settledUnits,
        string provider,
        string model,
        int? promptTokens,
        int? completionTokens,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        session.CompleteSelection(recipe.Id, now);
        database.Recipes.Add(new RecipeRecord
        {
            Id = recipe.Id,
            OwnerUserId = recipe.OwnerUserId,
            CurrentRevisionNumber = recipe.CurrentRevisionNumber,
            CreatedAt = recipe.CreatedAt,
            UpdatedAt = recipe.UpdatedAt
        });
        database.RecipeRevisions.Add(new RecipeRevisionRecord
        {
            Id = revision.Id,
            RecipeId = revision.RecipeId,
            OwnerUserId = revision.OwnerUserId,
            RevisionNumber = revision.RevisionNumber,
            Name = revision.Name,
            MealTypesJson = JsonSerializer.Serialize(revision.MealTypes, SerializerOptions),
            Servings = revision.Servings,
            NormalizedRecipeJson = revision.NormalizedRecipeJson,
            ThumbnailVisualJson = revision.ThumbnailVisualJson,
            SourceCandidateId = revision.SourceCandidateId,
            SourceGenerationSessionId = revision.SourceGenerationSessionId,
            CreatedAt = revision.CreatedAt
        });
        await ApplySessionAndSettleAsync(session, reservationId, settledUnits, provider, model, promptTokens, completionTokens, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task FailExpansionAsync(
        RecipeGenerationSession session,
        Guid reservationId,
        string failureReason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        session.MarkFailed(failureReason, now);
        await ApplySessionAndReleaseAsync(session, reservationId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ApplySessionAndSettleAsync(
        RecipeGenerationSession session,
        Guid reservationId,
        int settledUnits,
        string provider,
        string model,
        int? promptTokens,
        int? completionTokens,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var record = await database.RecipeGenerationSessions
            .SingleAsync(item => item.OwnerUserId == session.OwnerUserId && item.Id == session.Id, cancellationToken);
        PostgreSqlRecipeGenerationStore.ApplySession(record, session);

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

    private async Task ApplySessionAndReleaseAsync(
        RecipeGenerationSession session,
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var record = await database.RecipeGenerationSessions
            .SingleAsync(item => item.OwnerUserId == session.OwnerUserId && item.Id == session.Id, cancellationToken);
        PostgreSqlRecipeGenerationStore.ApplySession(record, session);

        var entry = await database.AiUsageLedgerEntries.SingleAsync(item => item.Id == reservationId, cancellationToken);
        if (entry.Status != nameof(AiUsageEntryStatus.Reserved))
        {
            throw new InvalidOperationException("Only a reserved AI usage entry can be released.");
        }

        entry.Status = nameof(AiUsageEntryStatus.Released);
        entry.ClosedAt = now;
        await database.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>PostgreSQL-backed owner-scoped recipe identity and immutable revision store.</summary>
public sealed class PostgreSqlRecipeStore(ApplicationDbContext database) : IRecipeStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task SaveNewAsync(Recipe recipe, RecipeRevision revision, CancellationToken cancellationToken)
    {
        database.Recipes.Add(new RecipeRecord
        {
            Id = recipe.Id,
            OwnerUserId = recipe.OwnerUserId,
            CurrentRevisionNumber = recipe.CurrentRevisionNumber,
            CreatedAt = recipe.CreatedAt,
            UpdatedAt = recipe.UpdatedAt
        });
        database.RecipeRevisions.Add(new RecipeRevisionRecord
        {
            Id = revision.Id,
            RecipeId = revision.RecipeId,
            OwnerUserId = revision.OwnerUserId,
            RevisionNumber = revision.RevisionNumber,
            Name = revision.Name,
            MealTypesJson = JsonSerializer.Serialize(revision.MealTypes, SerializerOptions),
            Servings = revision.Servings,
            NormalizedRecipeJson = revision.NormalizedRecipeJson,
            ThumbnailVisualJson = revision.ThumbnailVisualJson,
            SourceCandidateId = revision.SourceCandidateId,
            SourceGenerationSessionId = revision.SourceGenerationSessionId,
            CreatedAt = revision.CreatedAt
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecipeSummary>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var recipes = await database.Recipes.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);
        if (recipes.Count == 0)
        {
            return [];
        }

        var recipeIds = recipes.Select(item => item.Id).ToList();
        var revisions = await database.RecipeRevisions.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId && recipeIds.Contains(item.RecipeId))
            .ToListAsync(cancellationToken);
        var currentByRecipe = revisions
            .GroupBy(item => item.RecipeId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.RevisionNumber).First());

        return recipes
            .Where(recipe => currentByRecipe.ContainsKey(recipe.Id))
            .Select(recipe =>
            {
                var revision = currentByRecipe[recipe.Id];
                var mealTypes = JsonSerializer.Deserialize<string[]>(revision.MealTypesJson, SerializerOptions) ?? [];
                return new RecipeSummary(recipe.Id, revision.Name, mealTypes, revision.Servings, revision.RevisionNumber, recipe.CreatedAt, recipe.UpdatedAt);
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<RecipeDetail?> FindDetailAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken)
    {
        var recipe = await database.Recipes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.Id == recipeId, cancellationToken);
        if (recipe is null)
        {
            return null;
        }

        var revision = await database.RecipeRevisions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.RecipeId == recipeId && item.RevisionNumber == recipe.CurrentRevisionNumber, cancellationToken);
        if (revision is null)
        {
            return null;
        }

        var mealTypes = JsonSerializer.Deserialize<string[]>(revision.MealTypesJson, SerializerOptions) ?? [];
        return new RecipeDetail(recipe.Id, revision.RevisionNumber, revision.Name, mealTypes, revision.Servings, revision.NormalizedRecipeJson, revision.ThumbnailVisualJson, revision.CreatedAt);
    }
}
