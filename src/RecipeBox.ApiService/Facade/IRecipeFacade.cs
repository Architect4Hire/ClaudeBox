using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Facade;

/// <summary>
/// Application boundary for the Recipes feature: validates the incoming view model, applies caching,
/// and returns service models. The controller talks only to this interface and only ever sees view
/// models (in) and service models (out) — DTOs and EF entities never reach it.
/// </summary>
public interface IRecipeFacade
{
    /// <summary>
    /// Validates the filter and returns the matching recipe summaries; its criteria combine with AND.
    /// Throws <see cref="FluentValidation.ValidationException"/> on an invalid filter. Only the wholly
    /// unfiltered list is cached.
    /// </summary>
    Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(
        RecipeFilterViewModel filter, CancellationToken ct);

    /// <summary><c>null</c> when no recipe has the given id (the controller turns that into a 404).</summary>
    Task<RecipeDetailServiceModel?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Validates and creates a recipe. Throws <see cref="FluentValidation.ValidationException"/> on
    /// invalid input and <see cref="Models.Domain.RecipeNameConflictException"/> when the name is taken.
    /// </summary>
    Task<RecipeDetailServiceModel> CreateAsync(CreateRecipeViewModel viewModel, CancellationToken ct);

    /// <summary>
    /// Validates and updates the recipe with the given id. Throws
    /// <see cref="FluentValidation.ValidationException"/> on invalid input and
    /// <see cref="Models.Domain.RecipeNameConflictException"/> when another recipe owns the name.
    /// Returns <c>null</c> when no recipe has that id (the controller turns that into a 404).
    /// </summary>
    Task<RecipeDetailServiceModel?> UpdateAsync(int id, UpdateRecipeViewModel viewModel, CancellationToken ct);

    /// <summary>
    /// Deletes the recipe with the given id and invalidates its cached copies. Nothing is validated —
    /// the id arrives as a route value, not a view model. Returns <c>false</c> when no recipe has that
    /// id (the controller turns that into a 404).
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct);
}
