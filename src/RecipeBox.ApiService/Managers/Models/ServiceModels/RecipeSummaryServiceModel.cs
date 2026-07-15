namespace RecipeBox.ApiService.Managers.Models.ServiceModels;

/// <summary>
/// Lightweight recipe projection for list views: header fields plus category names and counts,
/// without the full ingredient/step bodies. Service models are what the facade returns to the
/// controller and the only recipe shapes that cross the API boundary — DTOs never leave the manager.
/// </summary>
public record RecipeSummaryServiceModel(
    int Id,
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<string> Categories,
    int IngredientCount,
    int StepCount);
