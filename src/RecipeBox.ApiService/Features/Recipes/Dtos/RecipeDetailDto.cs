namespace RecipeBox.ApiService.Features.Recipes.Dtos;

/// <summary>
/// Full recipe projection: header fields, ingredients, ordered steps, and taxonomy names.
/// <see cref="Steps"/> are ordered by <see cref="StepDto.Order"/>.
/// </summary>
public record RecipeDetailDto(
    int Id,
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<IngredientDto> Ingredients,
    IReadOnlyList<StepDto> Steps,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags);
