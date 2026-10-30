using System.Text.Json;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Recipes.Ideas;
using Microsoft.EntityFrameworkCore;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// One shared recipe idea (PLAN-0055 Phase 2). Global: no owner column, and the content is generic
/// by construction (<see cref="RecipeIdeaLibrary"/>).
/// </summary>
public sealed class RecipeIdeaRecord
{
    /// <summary>Gets or sets the idea id.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the duplicate-detection signature (SHA-256 hex).</summary>
    public required string Signature { get; set; }
    /// <summary>Gets or sets the normalized output language.</summary>
    public required string Language { get; set; }
    /// <summary>Gets or sets the instruction detail level, if any.</summary>
    public string? InstructionDetail { get; set; }
    /// <summary>Gets or sets the cooking skill, if any.</summary>
    public string? CookingSkill { get; set; }
    /// <summary>Gets or sets the restriction codes the idea was generated under.</summary>
    public required string[] Restrictions { get; set; }
    /// <summary>Gets or sets the meal type.</summary>
    public required string MealType { get; set; }
    /// <summary>Gets or sets the suggestion strategy.</summary>
    public required string Strategy { get; set; }
    /// <summary>Gets or sets the slugs of required, non-assumed ingredients.</summary>
    public required string[] IngredientSlugs { get; set; }
    /// <summary>Gets or sets the equipment stable codes.</summary>
    public required string[] EquipmentCodes { get; set; }
    /// <summary>Gets or sets the generic content as JSON.</summary>
    public required string ContentJson { get; set; }
    /// <summary>Gets or sets the times offered.</summary>
    public int TimesOffered { get; set; }
    /// <summary>Gets or sets the times selected.</summary>
    public int TimesSelected { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>PostgreSQL store of the shared recipe-idea library; the hard filters run in SQL on array columns.</summary>
public sealed class PostgreSqlRecipeIdeaStore(ApplicationDbContext database) : IRecipeIdeaStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task AddAsync(RecipeIdea idea, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var restrictions = idea.Restrictions.ToArray();
        var slugs = idea.IngredientSlugs.ToArray();
        var codes = idea.EquipmentCodes.ToArray();
        var content = JsonSerializer.Serialize(idea.Content, SerializerOptions);
        // DO NOTHING: the same idea written twice (same signature) is kept once, with its counters.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO recipes.recipe_ideas ("Id", "Signature", "Language", "InstructionDetail", "CookingSkill", "Restrictions", "MealType", "Strategy", "IngredientSlugs", "EquipmentCodes", "ContentJson", "TimesOffered", "TimesSelected", "CreatedAt")
            VALUES ({idea.Id}, {idea.Signature}, {idea.Language}, {idea.InstructionDetail}::text, {idea.CookingSkill}::text, {restrictions}, {idea.MealType}, {idea.Strategy}, {slugs}, {codes}, {content}::jsonb, 0, 0, {now})
            ON CONFLICT ("Signature") DO NOTHING
            """, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecipeIdea>> FindCandidatesAsync(RecipeIdeaQuery query, CancellationToken cancellationToken)
    {
        var restrictions = query.Restrictions.ToArray();
        var slugs = query.PantrySlugs.ToArray();
        var codes = query.EquipmentCodes.ToArray();
        // @> : the idea was generated under every restriction the person has; && : it uses at least
        // one pantry ingredient; <@ : it needs only equipment the person has.
        var records = await database.RecipeIdeas.FromSqlInterpolated($"""
            SELECT * FROM recipes.recipe_ideas
            WHERE "Language" = {query.Language}
              AND "InstructionDetail" IS NOT DISTINCT FROM {query.InstructionDetail}::text
              AND "CookingSkill" IS NOT DISTINCT FROM {query.CookingSkill}::text
              AND "Restrictions" @> {restrictions}::text[]
              AND ({query.MealType}::text IS NULL OR "MealType" = {query.MealType}::text)
              AND "IngredientSlugs" && {slugs}::text[]
              AND "EquipmentCodes" <@ {codes}::text[]
            ORDER BY "TimesSelected" DESC, "CreatedAt" DESC
            LIMIT {query.Limit}
            """).AsNoTracking().ToListAsync(cancellationToken);
        return records.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<RecipeIdea?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await database.RecipeIdeas.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    /// <inheritdoc />
    public Task MarkOfferedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        database.RecipeIdeas.Where(item => ids.Contains(item.Id)).ExecuteUpdateAsync(update => update.SetProperty(item => item.TimesOffered, item => item.TimesOffered + 1), cancellationToken);

    /// <inheritdoc />
    public Task MarkSelectedAsync(Guid id, CancellationToken cancellationToken) =>
        database.RecipeIdeas.Where(item => item.Id == id).ExecuteUpdateAsync(update => update.SetProperty(item => item.TimesSelected, item => item.TimesSelected + 1), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> OwnerRecipeNamesAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var names = await database.RecipeRevisions.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId).Select(item => item.Name).Distinct().ToListAsync(cancellationToken);
        return names.Select(IngredientText.Normalize).ToHashSet(StringComparer.Ordinal);
    }

    private static RecipeIdea ToDomain(RecipeIdeaRecord record) => new(
        record.Id,
        record.Signature,
        record.Language,
        record.InstructionDetail,
        record.CookingSkill,
        record.Restrictions,
        record.MealType,
        record.Strategy,
        record.IngredientSlugs,
        record.EquipmentCodes,
        JsonSerializer.Deserialize<RecipeIdeaContent>(record.ContentJson, SerializerOptions)!,
        record.TimesOffered,
        record.TimesSelected);
}
