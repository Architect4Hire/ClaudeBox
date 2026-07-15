using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Persistence for the Recipes feature. Data access only — no validation, caching, or domain rules.
/// Detail reads and writes return the domain <see cref="Recipe"/> entity for the business layer to
/// map; the list read is projected straight to <see cref="RecipeSummaryServiceModel"/> in SQL so a
/// list view never materializes ingredient/step rows.
/// </summary>
public interface IRecipeRepository
{
    /// <summary>Summary rows, optionally filtered to recipes in the named category.</summary>
    Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(string? category, CancellationToken ct);

    /// <summary>
    /// One recipe with its ingredients, steps (ordered by <see cref="Step.Order"/>), categories, and
    /// tags eagerly loaded; <c>null</c> if no recipe has the given id.
    /// </summary>
    Task<Recipe?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>True if a recipe with the given name already exists (case-insensitive).</summary>
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct);

    /// <summary>Persists a new recipe (with its owned ingredients and steps) and returns it with its id.</summary>
    Task<Recipe> AddAsync(Recipe recipe, CancellationToken ct);
}
