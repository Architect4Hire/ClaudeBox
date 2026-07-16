namespace RecipeBox.ApiService.Managers.Models.ServiceModels;

/// <summary>
/// Lightweight recipe projection for list views: header fields plus category names and counts,
/// without the full ingredient/step bodies. Service models are what the facade returns to the
/// controller and the only recipe shapes that cross the API boundary — DTOs never leave the manager.
/// </summary>
/// <param name="HasImage">
/// Whether this recipe has an image, not where to get it. The address is the client's to build from
/// the id — an ImageUrl here would copy the controller's route into a layer that knows nothing about
/// HTTP, and would then rot silently the first time that route changed.
/// </param>
public record RecipeSummaryServiceModel(
    int Id,
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<string> Categories,
    int IngredientCount,
    int StepCount,
    bool HasImage);
