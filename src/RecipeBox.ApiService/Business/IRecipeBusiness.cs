using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Business;

/// <summary>
/// Orchestrates the Recipes feature: sequences repository calls, applies data-dependent domain
/// rules, translates validated view models into domain entities, and maps loaded domain entities up
/// into service models. No request validation, caching, or direct EF access.
/// </summary>
public interface IRecipeBusiness
{
    /// <summary>
    /// Translates the validated filter view model into domain criteria and returns the matching recipe
    /// summaries. The filter's criteria combine with AND.
    /// </summary>
    Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(
        RecipeFilterViewModel filter, CancellationToken ct);

    Task<RecipeDetailServiceModel?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Translates the view model to a domain entity and persists it. Enforces the data-dependent rule
    /// that recipe names are unique, throwing <see cref="Models.Domain.RecipeNameConflictException"/>
    /// when the name is already taken.
    /// </summary>
    Task<RecipeDetailServiceModel> CreateAsync(CreateRecipeViewModel viewModel, CancellationToken ct);

    /// <summary>
    /// Translates the view model onto the existing recipe with the given id and persists it. Enforces
    /// the unique-name rule against <em>other</em> recipes, throwing
    /// <see cref="Models.Domain.RecipeNameConflictException"/> when another recipe already owns the
    /// name. Returns <c>null</c> when no recipe has that id (the controller turns that into a 404).
    /// </summary>
    Task<RecipeDetailServiceModel?> UpdateAsync(int id, UpdateRecipeViewModel viewModel, CancellationToken ct);

    /// <summary>
    /// Deletes the recipe with the given id and reaps any category or tag its removal left without
    /// recipes. Returns <c>false</c> when no recipe has that id (the controller turns that into a 404).
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}
