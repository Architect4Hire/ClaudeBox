using RecipeBox.ApiService.Domain;
using RecipeBox.ApiService.Features.Recipes.Models;

namespace RecipeBox.ApiService.Features.Recipes.Business;

/// <summary>
/// Orchestrates the Recipes feature: sequences repository calls and applies data-dependent domain
/// rules. No request validation, caching, or direct EF access.
/// </summary>
public interface IRecipeBusiness
{
    Task<IReadOnlyList<RecipeListItem>> ListAsync(string? category, CancellationToken ct);

    Task<Recipe?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Persists a new recipe. Enforces the data-dependent rule that recipe names are unique,
    /// throwing <see cref="RecipeNameConflictException"/> when the name is already taken.
    /// </summary>
    Task<Recipe> CreateAsync(Recipe recipe, CancellationToken ct);
}
