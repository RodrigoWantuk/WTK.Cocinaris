namespace Cocinaris.Modules.Planning.Application;

/// <summary>One required ingredient of a card, scaled to one meal of the plan.</summary>
/// <param name="Key">Matching key and unit, the same identity as a shopping line.</param>
/// <param name="DisplayName">Name shown.</param>
/// <param name="Quantity">Quantity for one meal of the plan.</param>
/// <param name="Perishable">Whether its catalog category is perishable; names outside the catalog are not.</param>
/// <param name="Costly">Whether its catalog category usually costs more (meat, poultry, fish, seafood, nuts).</param>
public sealed record RankIngredient(string Key, string DisplayName, decimal Quantity, bool Perishable, bool Costly);

/// <summary>What ranking needs to know about a card.</summary>
/// <param name="Id">Card.</param>
/// <param name="Order">Order it joined the deck; breaks ties.</param>
/// <param name="DishFormat">Format.</param>
/// <param name="Technique">Main technique.</param>
/// <param name="LeadKey">Key of the ingredient the dish is mostly made of, if any.</param>
/// <param name="ActiveMinutes">Hands-on minutes.</param>
/// <param name="TotalMinutes">Total minutes.</param>
/// <param name="Ingredients">Required ingredients.</param>
/// <param name="DislikeMatches">How many of the person's dislikes it matches.</param>
public sealed record RankCard(Guid Id, int Order, string DishFormat, string Technique, string? LeadKey, int ActiveMinutes, int TotalMinutes, IReadOnlyList<RankIngredient> Ingredients, int DislikeMatches = 0);

/// <summary>An accepted card and the meals it fills.</summary>
/// <param name="Card">Card.</param>
/// <param name="Repeats">Meals it fills.</param>
/// <param name="InSession">Whether it counts toward the preparation session's time.</param>
public sealed record RankAccepted(RankCard Card, int Repeats, bool InSession);

/// <summary>A refused card and why.</summary>
/// <param name="Card">Card.</param>
/// <param name="Reason"><c>dislike</c>, <c>too_much_work</c>, <c>tired_of</c>, or null.</param>
/// <param name="IngredientKey">Key behind <c>tired_of</c>.</param>
public sealed record RankRejected(RankCard Card, string? Reason, string? IngredientKey);

/// <summary>Everything ranking reads.</summary>
/// <param name="Priority">The plan's goal.</param>
/// <param name="Accepted">Accepted cards, in the order they were accepted.</param>
/// <param name="Rejected">Refused cards.</param>
/// <param name="FreeStock">Pantry stock no other plan counts on, by key.</param>
/// <param name="AttentionKeys">Keys of lots needing attention soon.</param>
/// <param name="SessionMinutes">Time for the session, if the plan has one.</param>
public sealed record RankContext(string Priority, IReadOnlyList<RankAccepted> Accepted, IReadOnlyList<RankRejected> Rejected, IReadOnlyDictionary<string, decimal> FreeStock, IReadOnlySet<string> AttentionKeys, int? SessionMinutes);

/// <summary>How one card compares with the plan so far.</summary>
/// <param name="Card">Card.</param>
/// <param name="Score">Higher comes first.</param>
/// <param name="NewItems">Lines it would add to the list.</param>
/// <param name="Reused">Ingredients on the list or covered by free stock.</param>
/// <param name="AttentionHits">Ingredients that use food needing attention.</param>
/// <param name="OverBudget">Whether it needs more active time than the session has left.</param>
public sealed record RankedCard(RankCard Card, int Score, IReadOnlyList<RankIngredient> NewItems, IReadOnlyList<RankIngredient> Reused, int AttentionHits, bool OverBudget);

/// <summary>
/// What the accepted cards ask of the list, allocating free stock in acceptance order as
/// <see cref="ShoppingProjector"/> does. Pure and read-only. Keys include the unit, so "frango" in
/// grams and in units stay separate lines, as they are on the real list.
/// </summary>
public sealed class ShoppingPlanProjection
{
    private readonly Dictionary<string, decimal> _remaining;
    private readonly Dictionary<string, decimal> _toBuy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _uses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    /// <summary>Projects the accepted cards.</summary>
    /// <param name="accepted">Accepted cards, in order.</param>
    /// <param name="freeStock">Free stock by key.</param>
    public ShoppingPlanProjection(IEnumerable<RankAccepted> accepted, IReadOnlyDictionary<string, decimal> freeStock)
    {
        _remaining = new Dictionary<string, decimal>(freeStock, StringComparer.Ordinal);
        foreach (var item in accepted)
        {
            foreach (var ingredient in item.Card.Ingredients)
            {
                var need = ingredient.Quantity * item.Repeats;
                var covered = Math.Min(need, _remaining.GetValueOrDefault(ingredient.Key));
                _remaining[ingredient.Key] = _remaining.GetValueOrDefault(ingredient.Key) - covered;
                _toBuy[ingredient.Key] = _toBuy.GetValueOrDefault(ingredient.Key) + need - covered;
                _uses[ingredient.Key] = _uses.GetValueOrDefault(ingredient.Key) + 1;
                _names.TryAdd(ingredient.Key, ingredient.DisplayName);
            }
        }
    }

    /// <summary>Lines still to buy.</summary>
    public int ListItemCount => _toBuy.Count(item => item.Value > 0);

    /// <summary>Display names of the lines still to buy.</summary>
    public IEnumerable<string> ListNames => _toBuy.Where(item => item.Value > 0).Select(item => _names[item.Key]);

    /// <summary>Whether a key is already a line to buy.</summary>
    /// <param name="key">Ingredient key.</param>
    public bool IsOnList(string key) => _toBuy.GetValueOrDefault(key) > 0;

    /// <summary>Free stock left after the accepted cards.</summary>
    /// <param name="key">Ingredient key.</param>
    public decimal Remaining(string key) => _remaining.GetValueOrDefault(key);

    /// <summary>How many accepted cards use a key.</summary>
    /// <param name="key">Ingredient key.</param>
    public int Uses(string key) => _uses.GetValueOrDefault(key);
}

/// <summary>
/// Orders the unseen cards for the plan's goal (PLAN-0062). Deterministic integer arithmetic: the same
/// input always gives the same order, and it never reads or changes anything else. Restrictions are
/// not here: blocked cards never reach the deck, so no goal or penalty can relax them.
/// </summary>
public static class ShoppingPlanRanker
{
    /// <summary>Scores cards, highest first, ties by the order they joined the deck.</summary>
    /// <param name="unseen">Cards to rank.</param>
    /// <param name="context">Plan state.</param>
    public static IReadOnlyList<RankedCard> Rank(IEnumerable<RankCard> unseen, RankContext context)
    {
        var projection = new ShoppingPlanProjection(context.Accepted, context.FreeStock);
        return unseen.Select(card => Score(card, context, projection))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Card.Order)
            .ToList();
    }

    /// <summary>Active minutes the session has used.</summary>
    /// <param name="accepted">Accepted cards.</param>
    public static int SessionMinutesUsed(IEnumerable<RankAccepted> accepted) =>
        accepted.Where(item => item.InSession).Sum(item => item.Card.ActiveMinutes);

    private static RankedCard Score(RankCard card, RankContext context, ShoppingPlanProjection projection)
    {
        var newItems = new List<RankIngredient>();
        var reused = new List<RankIngredient>();
        foreach (var ingredient in card.Ingredients)
        {
            if (projection.IsOnList(ingredient.Key) || ingredient.Quantity <= projection.Remaining(ingredient.Key))
            {
                reused.Add(ingredient);
            }
            else
            {
                newItems.Add(ingredient);
            }
        }

        var attention = card.Ingredients.Count(ingredient => context.AttentionKeys.Contains(ingredient.Key));
        var newPerishable = newItems.Count(item => item.Perishable);
        var rescued = card.Ingredients.Count(item => item.Perishable && projection.Uses(item.Key) == 1);
        var goal = context.Priority switch
        {
            "waste" => 40 * rescued - 30 * newPerishable - 5 * (newItems.Count - newPerishable) + 5 * reused.Count,
            "short" => -30 * newItems.Count + 5 * reused.Count,
            "save" => 10 * reused.Count - 10 * newItems.Count - 25 * newItems.Count(item => item.Costly),
            "time" => -card.ActiveMinutes - (card.TotalMinutes - card.ActiveMinutes) / 4 - 2 * newItems.Count,
            _ => 0
        };

        var diversity = 0;
        if (context.Accepted.Any(item => item.Card.DishFormat == card.DishFormat))
        {
            diversity -= 40;
        }

        if (context.Accepted.Any(item => item.Card.Technique == card.Technique))
        {
            diversity -= 25;
        }

        if (card.LeadKey is not null && context.Accepted.Count(item => item.Card.LeadKey == card.LeadKey) >= 2)
        {
            diversity -= 60;
        }

        var feedback = card.DislikeMatches > 0 ? -200 : 0;
        foreach (var rejected in context.Rejected)
        {
            feedback += rejected.Reason switch
            {
                "dislike" when card.LeadKey is not null && rejected.Card.LeadKey == card.LeadKey => -80,
                "too_much_work" when card.ActiveMinutes >= rejected.Card.ActiveMinutes => -60,
                "tired_of" when rejected.IngredientKey is not null && card.Ingredients.Any(item => item.Key == rejected.IngredientKey) => -150,
                null when rejected.Card.DishFormat == card.DishFormat => -10,
                _ => 0
            };
        }

        var overBudget = context.SessionMinutes is { } minutes && card.ActiveMinutes > minutes - SessionMinutesUsed(context.Accepted);
        var score = 100 * attention + goal + diversity + feedback + (overBudget ? -200 : 0);
        return new RankedCard(card, score, newItems, reused, attention, overBudget);
    }
}

/// <summary>What sequencing needs to know about a menu entry.</summary>
/// <param name="EntryId">Entry.</param>
/// <param name="RecipeId">Recipe, so copies of a dish can be kept apart.</param>
/// <param name="Rank">Lowest perishability rank among its required ingredients not already covered by frozen stock.</param>
/// <param name="UsesAttention">Whether it uses food needing attention.</param>
/// <param name="TotalMinutes">Total minutes.</param>
/// <param name="CreatedAt">Creation instant.</param>
public sealed record SequenceEntry(Guid EntryId, Guid RecipeId, int Rank, bool UsesAttention, int TotalMinutes, DateTimeOffset CreatedAt);

/// <summary>
/// Suggests a cooking order (PLAN-0062): what spoils first comes first. This sets an order, not a
/// safety statement. It runs only before a menu is accepted, so accepted plans are never rewritten.
/// </summary>
public static class ShoppingPlanSequencer
{
    /// <summary>Orders entries and moves a repeated dish away from its copy when another entry exists.</summary>
    /// <param name="entries">Entries.</param>
    public static IReadOnlyList<Guid> Order(IEnumerable<SequenceEntry> entries)
    {
        var sorted = entries
            .OrderBy(item => item.Rank)
            .ThenByDescending(item => item.UsesAttention)
            .ThenBy(item => item.TotalMinutes)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.EntryId)
            .ToList();
        for (var index = 1; index < sorted.Count; index++)
        {
            if (sorted[index].RecipeId != sorted[index - 1].RecipeId)
            {
                continue;
            }

            var swap = sorted.FindIndex(index + 1, item => item.RecipeId != sorted[index].RecipeId);
            if (swap >= 0)
            {
                (sorted[index], sorted[swap]) = (sorted[swap], sorted[index]);
            }
        }

        return sorted.Select(item => item.EntryId).ToList();
    }
}
