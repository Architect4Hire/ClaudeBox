using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Business;

/// <summary>
/// Orchestrates the Recipes feature: sequences data-layer calls, applies data-dependent domain
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
    /// Deletes the recipe with the given id, along with any category or tag its removal left without
    /// recipes and its image. Returns <c>false</c> when no recipe has that id (the controller turns
    /// that into a 404).
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// The given recipe's image, ready to stream. <c>null</c> when there are no bytes to serve — no
    /// such recipe, no image, or a blob that has gone missing (the controller turns all three into a
    /// 404). The caller owns the returned stream.
    /// </summary>
    Task<RecipeImageServiceModel?> GetImageAsync(int id, CancellationToken ct);

    /// <summary>
    /// Stores the validated upload as the given recipe's image, replacing any existing one. The format
    /// is established from the bytes, and the blob name minted here. Returns <c>false</c> when no
    /// recipe has that id (the controller turns that into a 404).
    /// </summary>
    Task<bool> SetImageAsync(int id, UploadRecipeImageViewModel viewModel, CancellationToken ct);

    /// <summary>
    /// Removes the given recipe's image. Returns <c>false</c> when no recipe has that id or it had no
    /// image (the controller turns that into a 404).
    /// </summary>
    Task<bool> RemoveImageAsync(int id, CancellationToken ct);
}
