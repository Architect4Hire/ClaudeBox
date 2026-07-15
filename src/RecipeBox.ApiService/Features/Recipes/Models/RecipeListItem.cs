namespace RecipeBox.ApiService.Features.Recipes.Models;

/// <summary>
/// Internal summary projection returned by the data layer for list queries. Not an EF entity and
/// not a DTO — it never crosses the API boundary; the facade maps it to a
/// <see cref="Dtos.RecipeSummaryDto"/>. Projecting counts in SQL avoids loading ingredient/step rows.
/// </summary>
public record RecipeListItem(
    int Id,
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<string> Categories,
    int IngredientCount,
    int StepCount);
