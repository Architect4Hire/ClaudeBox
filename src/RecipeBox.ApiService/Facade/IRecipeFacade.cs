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
    Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(string? category, CancellationToken ct);

    /// <summary><c>null</c> when no recipe has the given id (the controller turns that into a 404).</summary>
    Task<RecipeDetailServiceModel?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Validates and creates a recipe. Throws <see cref="FluentValidation.ValidationException"/> on
    /// invalid input and <see cref="Models.Domain.RecipeNameConflictException"/> when the name is taken.
    /// </summary>
    Task<RecipeDetailServiceModel> CreateAsync(CreateRecipeViewModel viewModel, CancellationToken ct);
}
