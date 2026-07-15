namespace RecipeBox.ApiService.Features.Recipes.Dtos;

/// <summary>
/// Lightweight recipe projection for list views: header fields plus category names and counts,
/// without the full ingredient/step bodies.
/// </summary>
public record RecipeSummaryDto(
    int Id,
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<string> Categories,
    int IngredientCount,
    int StepCount);
