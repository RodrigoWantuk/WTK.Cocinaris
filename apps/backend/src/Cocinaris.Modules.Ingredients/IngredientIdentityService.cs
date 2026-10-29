namespace Cocinaris.Modules.Ingredients;

/// <summary>Application contract other modules use to match ingredient names (PLAN-0055).</summary>
public interface IIngredientMatching
{
    /// <summary>
    /// Returns matching keys for <paramref name="names"/>. Curated catalog names and names resolved
    /// earlier get their ingredient key at once. Any other name keeps its normalized text and is
    /// queued for the background resolution job, so the call never waits for a provider.
    /// </summary>
    /// <param name="ownerUserId">Owner whose names these are; charged for the provider calls a new name needs.</param>
    /// <param name="names">Pantry product names and recipe ingredient names, in any language.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IngredientMatchKeys> KeysAsync(Guid ownerUserId, IEnumerable<string> names, CancellationToken cancellationToken);
}

/// <summary>Default <see cref="IIngredientMatching"/>: catalog in memory, learned aliases in the store.</summary>
public sealed class IngredientIdentityService(IIngredientStore store, IngredientCatalog catalog, TimeProvider timeProvider) : IIngredientMatching
{
    /// <summary>Most distinct names one call resolves; more are matched by text only.</summary>
    private const int MaxNamesPerCall = 1_000;

    /// <inheritdoc />
    public async Task<IngredientMatchKeys> KeysAsync(Guid ownerUserId, IEnumerable<string> names, CancellationToken cancellationToken)
    {
        // The exact text kept for the queue is the first spelling seen for each normalized name.
        var exactByNormalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || exactByNormalized.Count >= MaxNamesPerCall)
            {
                continue;
            }

            var normalized = IngredientText.Normalize(name);
            var exact = name.Trim();
            if (normalized.Length >= 2)
            {
                exactByNormalized.TryAdd(normalized, exact.Length > IngredientText.MaxExactLength ? exact[..IngredientText.MaxExactLength] : exact);
            }
        }

        var slugs = new Dictionary<string, string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var normalized in exactByNormalized.Keys)
        {
            if (catalog.Aliases.TryGetValue(normalized, out var item))
            {
                slugs[normalized] = item.Slug;
            }
            else
            {
                unknown.Add(normalized);
            }
        }

        if (unknown.Count == 0)
        {
            return new IngredientMatchKeys(slugs);
        }

        var learned = await store.FindAliasSlugsAsync(unknown, cancellationToken);
        foreach (var (normalized, slug) in learned)
        {
            slugs[normalized] = slug;
        }

        var toQueue = unknown.Where(normalized => !learned.ContainsKey(normalized)).Select(normalized => (normalized, exactByNormalized[normalized])).ToList();
        if (toQueue.Count > 0)
        {
            await store.EnqueueAsync(toQueue, ownerUserId, timeProvider.GetUtcNow(), cancellationToken);
        }

        return new IngredientMatchKeys(slugs);
    }
}
