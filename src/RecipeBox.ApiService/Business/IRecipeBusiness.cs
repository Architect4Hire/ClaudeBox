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
    Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(string? category, CancellationToken ct);

    Task<RecipeDetailServiceModel?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Translates the view model to a domain entity and persists it. Enforces the data-dependent rule
    /// that recipe names are unique, throwing <see cref="Models.Domain.RecipeNameConflictException"/>
    /// when the name is already taken.
    /// </summary>
    Task<RecipeDetailServiceModel> CreateAsync(CreateRecipeViewModel viewModel, CancellationToken ct);
}
