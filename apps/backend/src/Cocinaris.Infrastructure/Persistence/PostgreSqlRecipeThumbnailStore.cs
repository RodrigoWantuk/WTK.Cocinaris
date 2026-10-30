using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Recipes.Thumbnails;
using Microsoft.EntityFrameworkCore;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>PostgreSQL-backed thumbnail assets and job queue (PLAN-0030).</summary>
public sealed class PostgreSqlRecipeThumbnailStore(ApplicationDbContext database) : IRecipeThumbnailStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, RecipeThumbnailAsset>> FindAsync(IReadOnlyCollection<string> renderKeys, CancellationToken cancellationToken)
    {
        if (renderKeys.Count == 0)
        {
            return new Dictionary<string, RecipeThumbnailAsset>();
        }

        var records = await database.RecipeThumbnails.AsNoTracking().Where(item => renderKeys.Contains(item.RenderKey)).ToListAsync(cancellationToken);
        return records.ToDictionary(item => item.RenderKey, ToDomain, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(IReadOnlyCollection<RecipeThumbnailAsset> assets, CancellationToken cancellationToken)
    {
        // ON CONFLICT DO NOTHING collapses concurrent misses for one render key into one job.
        foreach (var asset in assets)
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO recipes.recipe_thumbnails ("RenderKey", "VisualIdentityKey", "DescriptorJson", "RequestedByUserId", "State", "Attempts", "NextAttemptAt", "CreatedAt", "UpdatedAt")
                VALUES ({asset.RenderKey}, {asset.VisualIdentityKey}, {asset.DescriptorJson}::jsonb, {asset.RequestedByUserId}, 'Pending', 0, {asset.NextAttemptAt}, {asset.NextAttemptAt}, {asset.NextAttemptAt})
                ON CONFLICT ("RenderKey") DO NOTHING
                """, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<RecipeThumbnailAsset?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        // SKIP LOCKED lets several workers drain the queue without claiming the same row; an
        // expired generation lease (a crashed worker) makes the row claimable again.
        var claimed = await database.RecipeThumbnails.FromSqlInterpolated($"""
            UPDATE recipes.recipe_thumbnails SET "State" = 'Generating', "NextAttemptAt" = {leaseUntil}, "UpdatedAt" = {now}
            WHERE "RenderKey" = (
                SELECT "RenderKey" FROM recipes.recipe_thumbnails
                WHERE "State" IN ('Pending', 'Generating') AND "NextAttemptAt" <= {now}
                ORDER BY "NextAttemptAt", "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(cancellationToken);
        return claimed.Count == 0 ? null : ToDomain(claimed[0]);
    }

    /// <inheritdoc />
    public async Task SaveAsync(RecipeThumbnailAsset asset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await database.RecipeThumbnails.Where(item => item.RenderKey == asset.RenderKey).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.State, asset.State.ToString())
            .SetProperty(item => item.Attempts, asset.Attempts)
            .SetProperty(item => item.NextAttemptAt, asset.NextAttemptAt)
            .SetProperty(item => item.FileName, asset.FileName)
            .SetProperty(item => item.MediaType, asset.MediaType)
            .SetProperty(item => item.Width, asset.Width)
            .SetProperty(item => item.Height, asset.Height)
            .SetProperty(item => item.FailureCode, asset.FailureCode)
            .SetProperty(item => item.UpdatedAt, now), cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountGeneratedSinceAsync(Guid? ownerUserId, DateTimeOffset since, CancellationToken cancellationToken) =>
        database.AiUsageLedgerEntries.AsNoTracking()
            .Where(item => item.Operation == AiOperationRegistry.ThumbnailGenerate && item.CreatedAt >= since && (ownerUserId == null || item.OwnerUserId == ownerUserId))
            .CountAsync(cancellationToken);

    private static RecipeThumbnailAsset ToDomain(RecipeThumbnailRecord record) => new(
        record.RenderKey,
        record.VisualIdentityKey,
        record.DescriptorJson,
        record.RequestedByUserId,
        Enum.Parse<RecipeThumbnailState>(record.State),
        record.Attempts,
        record.NextAttemptAt,
        record.FileName,
        record.MediaType,
        record.Width,
        record.Height,
        record.FailureCode);
}

/// <summary>
/// Stores generated images as files in one directory (PLAN-0030). On the single-host deployment
/// this is the service's state directory. The port allows an S3-compatible store later without
/// changing callers. Names are render keys plus an extension, so they never come from user input.
/// </summary>
public sealed class FileSystemRecipeThumbnailFileStore(string directory) : IRecipeThumbnailFileStore
{
    /// <inheritdoc />
    public async Task WriteAsync(string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var target = PathFor(fileName);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("n");
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        File.Move(temporary, target, overwrite: true);
    }

    /// <inheritdoc />
    public Stream? OpenRead(string fileName)
    {
        var path = PathFor(fileName);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, useAsync: true) : null;
    }

    /// <inheritdoc />
    public bool Exists(string fileName) => File.Exists(PathFor(fileName));

    private string PathFor(string fileName)
    {
        // Render keys are hex; reject anything else so a stored name can never escape the directory.
        if (fileName.Length > 80 || !fileName.All(character => char.IsAsciiHexDigitLower(character) || character == '.' || char.IsAsciiLetterLower(character)) || fileName.Contains(".."))
        {
            throw new ArgumentException("Invalid thumbnail file name.", nameof(fileName));
        }

        return Path.Combine(directory, fileName);
    }
}
