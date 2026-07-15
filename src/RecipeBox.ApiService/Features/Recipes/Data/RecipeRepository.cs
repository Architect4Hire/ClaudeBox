using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Domain;
using RecipeBox.ApiService.Features.Recipes;
using RecipeBox.ApiService.Features.Recipes.Models;

namespace RecipeBox.ApiService.Features.Recipes.Data;

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

    public async Task<IReadOnlyList<RecipeListItem>> ListAsync(string? category, CancellationToken ct)
    {
        var query = _db.Recipes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(r => r.Categories.Any(c => c.Name == category));
        }

        // Project counts in SQL so we never materialize ingredient/step rows for a list view.
        return await query
            .OrderBy(r => r.Name)
            .Select(r => new RecipeListItem(
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

    public async Task<Recipe> AddAsync(Recipe recipe, CancellationToken ct)
    {
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
}
