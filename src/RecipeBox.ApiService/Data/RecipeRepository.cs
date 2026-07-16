using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// EF Core implementation of <see cref="IRecipeRepository"/> against the Aspire-provided
/// <see cref="RecipeDbContext"/>. Queries only — no business rules, caching, or validation.
/// </summary>
public class RecipeRepository(RecipeDbContext db) : IRecipeRepository
{
    private readonly RecipeDbContext _db = db;

    /// <summary>
    /// Case-insensitive unique index on recipe name, created via raw SQL in the
    /// <c>AddRecipeNameUniqueIndex</c> migration (EF's fluent API can't express a <c>LOWER(...)</c>
    /// functional index). It is the authoritative enforcement of the unique-name rule.
    /// </summary>
    public const string RecipeNameUniqueIndex = "IX_Recipes_Name_Lower";

    public async Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(string? category, CancellationToken ct)
    {
        var query = _db.Recipes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(r => r.Categories.Any(c => c.Name == category));
        }

        // Project counts in SQL straight into the summary service model, so a list view never
        // materializes ingredient/step rows. This is the one place the data layer builds an outbound
        // model — the alternative (return entities and count in memory) would defeat the projection.
        return await query
            .OrderBy(r => r.Name)
            .Select(r => new RecipeSummaryServiceModel(
                r.Id,
                r.Name,
                r.Description,
                r.Servings,
                r.Categories.Select(c => c.Name).ToList(),
                r.Ingredients.Count,
                r.Steps.Count))
            .ToListAsync(ct);
    }

    public async Task<Recipe?> GetByIdAsync(int id, CancellationToken ct)
    {
        // Four collection includes in one query would cartesian-explode (ingredients × steps ×
        // categories × tags); AsSplitQuery emits one query per collection instead.
        return await _db.Recipes
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps.OrderBy(s => s.Order))
            .Include(r => r.Categories)
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    // LOWER(Name) = LOWER(@name) matches the IX_Recipes_Name_Lower functional index, so this is a
    // sargable index lookup rather than a full scan.
    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct) =>
        _db.Recipes.AnyAsync(r => r.Name.ToLower() == name.ToLower(), ct);

    // Same sargable LOWER(Name) lookup as ExistsByNameAsync, but ignores the row being edited so a
    // recipe keeping its own name doesn't collide with itself.
    public Task<bool> ExistsWithNameExceptAsync(string name, int excludingId, CancellationToken ct) =>
        _db.Recipes.AnyAsync(r => r.Id != excludingId && r.Name.ToLower() == name.ToLower(), ct);

    public async Task<Recipe> AddAsync(Recipe recipe, CancellationToken ct)
    {
        // Resolve taxonomy by name against existing rows so a shared category/tag (e.g. "Dessert")
        // is reused rather than re-inserted — the unique-name indexes would otherwise reject a duplicate.
        recipe.Categories = await ResolveCategoriesAsync(recipe.Categories, ct);
        recipe.Tags = await ResolveTagsAsync(recipe.Tags, ct);

        _db.Recipes.Add(recipe);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
                  {
                      SqlState: PostgresErrorCodes.UniqueViolation,
                      ConstraintName: RecipeNameUniqueIndex
                  })
        {
            // The unique index is the real backstop: a concurrent create can slip past the
            // business-layer pre-check, so translate the DB violation to the domain exception here
            // (persistence-error translation is a data concern; the outcome stays 409).
            throw new RecipeNameConflictException(recipe.Name);
        }

        return recipe;
    }

    public async Task<Recipe?> UpdateAsync(int id, Recipe incoming, CancellationToken ct)
    {
        // Load the recipe tracked, with every collection an update replaces: the owned children
        // (ingredients, steps) and both taxonomies. AsSplitQuery keeps the four collection includes from
        // cartesian-exploding into one flattened result set.
        var existing = await _db.Recipes
            .AsSplitQuery()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Categories)
            .Include(r => r.Tags)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (existing is null)
        {
            return null;
        }

        existing.Name = incoming.Name;
        existing.Description = incoming.Description;
        existing.Servings = incoming.Servings;

        // Replace the owned children wholesale. Clearing the tracked collections orphans the old rows;
        // the required Recipe FK + cascade means EF deletes them, then inserts the incoming set. Deletes
        // are ordered before inserts in the same SaveChanges, so the (RecipeId, Order) unique index is
        // never transiently violated when steps are renumbered.
        existing.Ingredients.Clear();
        foreach (var ingredient in incoming.Ingredients)
        {
            existing.Ingredients.Add(new Ingredient
            {
                Name = ingredient.Name,
                Quantity = ingredient.Quantity,
                Unit = ingredient.Unit,
            });
        }

        existing.Steps.Clear();
        foreach (var step in incoming.Steps)
        {
            existing.Steps.Add(new Step { Order = step.Order, Instruction = step.Instruction });
        }

        // Replace taxonomy wholesale, the same "clear + re-add" shape as the owned children. Clearing a
        // many-to-many collection drops only the join rows, not the Category/Tag rows; the resolve step
        // then reuses existing rows by name (or creates missing ones), so no duplicates are inserted.
        existing.Categories.Clear();
        foreach (var category in await ResolveCategoriesAsync(incoming.Categories, ct))
        {
            existing.Categories.Add(category);
        }

        existing.Tags.Clear();
        foreach (var tag in await ResolveTagsAsync(incoming.Tags, ct))
        {
            existing.Tags.Add(tag);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
                  {
                      SqlState: PostgresErrorCodes.UniqueViolation,
                      ConstraintName: RecipeNameUniqueIndex
                  })
        {
            // Same backstop as AddAsync: a concurrent rename can slip past the business-layer check, so
            // translate the unique-index violation to the domain exception (still surfaces as 409).
            throw new RecipeNameConflictException(incoming.Name);
        }

        return existing;
    }

    // ── Taxonomy resolution ──────────────────────────────────────────────────────────────────────
    // Map incoming carrier entities (name only) onto persisted rows: reuse the existing row for any
    // name already in the table, create a new one for the rest. De-duplicates by name so a repeated
    // name in the request can't attach the same taxonomy twice. Names are matched exactly (the unique
    // index on Category/Tag name is case-sensitive), so callers should normalise casing upstream.

    private async Task<List<Category>> ResolveCategoriesAsync(
        ICollection<Category> incoming, CancellationToken ct)
    {
        var names = incoming.Select(c => c.Name).Distinct().ToList();
        if (names.Count == 0)
        {
            return [];
        }

        var existing = await _db.Categories
            .Where(c => names.Contains(c.Name))
            .ToDictionaryAsync(c => c.Name, ct);

        return names
            .Select(name => existing.TryGetValue(name, out var found) ? found : new Category { Name = name })
            .ToList();
    }

    private async Task<List<Tag>> ResolveTagsAsync(ICollection<Tag> incoming, CancellationToken ct)
    {
        var names = incoming.Select(t => t.Name).Distinct().ToList();
        if (names.Count == 0)
        {
            return [];
        }

        var existing = await _db.Tags
            .Where(t => names.Contains(t.Name))
            .ToDictionaryAsync(t => t.Name, ct);

        return names
            .Select(name => existing.TryGetValue(name, out var found) ? found : new Tag { Name = name })
            .ToList();
    }
}
