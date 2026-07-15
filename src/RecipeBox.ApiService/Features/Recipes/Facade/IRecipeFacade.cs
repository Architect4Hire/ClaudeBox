using RecipeBox.ApiService.Features.Recipes.Dtos;

namespace RecipeBox.ApiService.Features.Recipes.Facade;

/// <summary>
/// Boundary for the Recipes feature: validates requests, applies caching, and maps between DTOs and
/// the internal model. The controller talks only to this interface.
/// </summary>
public interface IRecipeFacade
{
    Task<IReadOnlyList<RecipeSummaryDto>> ListAsync(string? category, CancellationToken ct);

    /// <summary><c>null</c> when no recipe has the given id (the controller turns that into a 404).</summary>
    Task<RecipeDetailDto?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Validates and creates a recipe. Throws <see cref="FluentValidation.ValidationException"/> on
    /// invalid input and <see cref="RecipeNameConflictException"/> when the name is taken.
    /// </summary>
    Task<RecipeDetailDto> CreateAsync(CreateRecipeRequest request, CancellationToken ct);
}
