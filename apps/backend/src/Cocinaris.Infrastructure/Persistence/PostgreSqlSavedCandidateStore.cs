using Cocinaris.Modules.Recipes.Application;
using Microsoft.EntityFrameworkCore;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>A candidate an owner saved for later (PLAN-0056 Phase 3).</summary>
public sealed class SavedCandidateEntryRecord
{
    /// <summary>Gets or sets the saved entry id.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the owner.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the generation session the candidate came from.</summary>
    public Guid SessionId { get; set; }
    /// <summary>Gets or sets the candidate id within the session.</summary>
    public required string CandidateId { get; set; }
    /// <summary>Gets or sets the validated candidate snapshot.</summary>
    public required string CandidateJson { get; set; }
    /// <summary>Gets or sets the UTC instant it was saved.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the recipe it was cooked into; null while it is still saved for later.</summary>
    public Guid? CookedRecipeId { get; set; }
}

/// <summary>PostgreSQL store of saved candidates; every query is scoped to the owner.</summary>
public sealed class PostgreSqlSavedCandidateStore(ApplicationDbContext database) : ISavedCandidateStore
{
    /// <inheritdoc />
    public async Task<SavedCandidateRecord> AddAsync(Guid ownerUserId, Guid sessionId, string candidateId, string candidateJson, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Saving the same candidate twice keeps the first entry (unique owner, session, candidate).
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO recipes.saved_candidates ("Id", "OwnerUserId", "SessionId", "CandidateId", "CandidateJson", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {ownerUserId}, {sessionId}, {candidateId}, {candidateJson}::jsonb, {now})
            ON CONFLICT ("OwnerUserId", "SessionId", "CandidateId") DO NOTHING
            """, cancellationToken);
        var record = await database.SavedCandidates.AsNoTracking()
            .SingleAsync(item => item.OwnerUserId == ownerUserId && item.SessionId == sessionId && item.CandidateId == candidateId, cancellationToken);
        return ToRecord(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedCandidateRecord>> ListAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken) =>
        (await database.SavedCandidates.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId && item.CookedRecipeId == null).OrderByDescending(item => item.CreatedAt).Take(limit).ToListAsync(cancellationToken))
            .Select(ToRecord).ToList();

    /// <inheritdoc />
    public async Task<SavedCandidateRecord?> FindAsync(Guid ownerUserId, Guid savedId, CancellationToken cancellationToken)
    {
        var record = await database.SavedCandidates.AsNoTracking().FirstOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.Id == savedId, cancellationToken);
        return record is null ? null : ToRecord(record);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid ownerUserId, Guid savedId, CancellationToken cancellationToken) =>
        await database.SavedCandidates.Where(item => item.OwnerUserId == ownerUserId && item.Id == savedId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <inheritdoc />
    public Task MarkCookedAsync(Guid ownerUserId, Guid savedId, Guid recipeId, CancellationToken cancellationToken) =>
        database.SavedCandidates.Where(item => item.OwnerUserId == ownerUserId && item.Id == savedId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.CookedRecipeId, recipeId), cancellationToken);

    private static SavedCandidateRecord ToRecord(SavedCandidateEntryRecord record) => new(record.Id, record.SessionId, record.CandidateJson, record.CreatedAt);
}
