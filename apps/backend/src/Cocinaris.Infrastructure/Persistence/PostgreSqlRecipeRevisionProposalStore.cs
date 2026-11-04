using System.Text.Json;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>A proposed AI revision of a recipe (PLAN-0060).</summary>
public sealed class RecipeRevisionProposalRecord
{
    /// <summary>Gets or sets the proposal id.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the owner.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the recipe being revised.</summary>
    public Guid RecipeId { get; set; }
    /// <summary>Gets or sets the revision it was written from.</summary>
    public int BaseRevisionNumber { get; set; }
    /// <summary>Gets or sets the request key.</summary>
    public Guid IdempotencyKey { get; set; }
    /// <summary>Gets or sets the owner's normalized request.</summary>
    public required string Instruction { get; set; }
    /// <summary>Gets or sets the status.</summary>
    public required string Status { get; set; }
    /// <summary>Gets or sets the proposed name.</summary>
    public string? Name { get; set; }
    /// <summary>Gets or sets the proposed meal types as JSON.</summary>
    public required string MealTypesJson { get; set; }
    /// <summary>Gets or sets the proposed servings.</summary>
    public int? Servings { get; set; }
    /// <summary>Gets or sets the validated proposed content; null once decided.</summary>
    public string? NormalizedRecipeJson { get; set; }
    /// <summary>Gets or sets the validated proposed visual descriptor; null once decided.</summary>
    public string? ThumbnailVisualJson { get; set; }
    /// <summary>Gets or sets the revision created by accepting.</summary>
    public int? AcceptedRevisionNumber { get; set; }
    /// <summary>Gets or sets the failure classification.</summary>
    public string? FailureReason { get; set; }
    /// <summary>Gets or sets the creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the instant of the last transition.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>PostgreSQL store of revision proposals; every query is scoped to the owner.</summary>
public sealed class PostgreSqlRecipeRevisionProposalStore(ApplicationDbContext database) : IRecipeRevisionProposalStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<(bool WasCreated, RecipeRevisionProposal Proposal)> TryClaimAsync(RecipeRevisionProposal proposal, CancellationToken cancellationToken)
    {
        var record = new RecipeRevisionProposalRecord
        {
            Id = proposal.Id,
            OwnerUserId = proposal.OwnerUserId,
            RecipeId = proposal.RecipeId,
            BaseRevisionNumber = proposal.BaseRevisionNumber,
            IdempotencyKey = proposal.IdempotencyKey,
            Instruction = proposal.Instruction,
            Status = proposal.Status.ToString(),
            MealTypesJson = "[]",
            CreatedAt = proposal.CreatedAt,
            UpdatedAt = proposal.UpdatedAt
        };
        database.RecipeRevisionProposals.Add(record);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return (true, proposal);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            database.ChangeTracker.Clear();
            var existing = await database.RecipeRevisionProposals.AsNoTracking()
                .SingleAsync(item => item.OwnerUserId == proposal.OwnerUserId && item.IdempotencyKey == proposal.IdempotencyKey, cancellationToken);
            return (false, ToDomain(existing));
        }
    }

    /// <inheritdoc />
    public async Task<RecipeRevisionProposal?> FindAsync(Guid ownerUserId, Guid recipeId, Guid proposalId, CancellationToken cancellationToken)
    {
        var record = await database.RecipeRevisionProposals.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId && item.RecipeId == recipeId && item.Id == proposalId, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public async Task SaveAsync(RecipeRevisionProposal proposal, CancellationToken cancellationToken)
    {
        await ApplyAsync(proposal, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(RecipeRevisionProposal proposal, Guid reservationId, int settledUnits, AiCallUsage usage, long costMicroUsd, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await ApplyAsync(proposal, cancellationToken);
        var entry = await database.AiUsageLedgerEntries.SingleAsync(item => item.Id == reservationId, cancellationToken);
        PostgreSqlAiUsageLedgerStore.ApplySettlement(entry, settledUnits, usage, costMicroUsd, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task FailAsync(RecipeRevisionProposal proposal, Guid reservationId, AiCallUsage usage, long costMicroUsd, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await ApplyAsync(proposal, cancellationToken);
        var entry = await database.AiUsageLedgerEntries.SingleAsync(item => item.Id == reservationId, cancellationToken);
        PostgreSqlAiUsageLedgerStore.ApplyRelease(entry, usage, costMicroUsd, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcceptAsync(Recipe recipe, RecipeRevision revision, int expectedRevisionNumber, RecipeRevisionProposal proposal, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var advanced = await database.Recipes
            .Where(item => item.OwnerUserId == recipe.OwnerUserId && item.Id == recipe.Id && item.CurrentRevisionNumber == expectedRevisionNumber)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.CurrentRevisionNumber, recipe.CurrentRevisionNumber)
                .SetProperty(item => item.UpdatedAt, recipe.UpdatedAt), cancellationToken);
        if (advanced != 1)
        {
            return false;
        }

        database.RecipeRevisions.Add(PostgreSqlRecipeLibraryStore.ToRecord(revision));
        await ApplyAsync(proposal, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task ApplyAsync(RecipeRevisionProposal proposal, CancellationToken cancellationToken)
    {
        var record = await database.RecipeRevisionProposals
            .SingleAsync(item => item.OwnerUserId == proposal.OwnerUserId && item.Id == proposal.Id, cancellationToken);
        record.Status = proposal.Status.ToString();
        record.Name = proposal.Name;
        record.MealTypesJson = JsonSerializer.Serialize(proposal.MealTypes, SerializerOptions);
        record.Servings = proposal.Servings;
        record.NormalizedRecipeJson = proposal.NormalizedRecipeJson;
        record.ThumbnailVisualJson = proposal.ThumbnailVisualJson;
        record.AcceptedRevisionNumber = proposal.AcceptedRevisionNumber;
        record.FailureReason = proposal.FailureReason;
        record.UpdatedAt = proposal.UpdatedAt;
    }

    private static RecipeRevisionProposal ToDomain(RecipeRevisionProposalRecord record) => RecipeRevisionProposal.Restore(
        record.Id,
        record.OwnerUserId,
        record.RecipeId,
        record.BaseRevisionNumber,
        record.IdempotencyKey,
        record.Instruction,
        Enum.Parse<RecipeRevisionProposalStatus>(record.Status),
        record.Name,
        JsonSerializer.Deserialize<string[]>(record.MealTypesJson, SerializerOptions) ?? [],
        record.Servings,
        record.NormalizedRecipeJson,
        record.ThumbnailVisualJson,
        record.AcceptedRevisionNumber,
        record.FailureReason,
        record.CreatedAt,
        record.UpdatedAt);
}
