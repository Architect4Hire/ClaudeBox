namespace RecipeBox.ApiService.Managers.Models.ServiceModels;

/// <summary>
/// Full recipe projection returned across the API boundary: header fields, ingredients, ordered
/// steps, and taxonomy names. <see cref="Steps"/> are ordered by <see cref="StepServiceModel.Order"/>.
/// </summary>
public record RecipeDetailServiceModel(
    int Id,
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<IngredientServiceModel> Ingredients,
    IReadOnlyList<StepServiceModel> Steps,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags);

/// <summary>An ingredient line as returned across the API boundary.</summary>
public record IngredientServiceModel(string Name, decimal Quantity, string? Unit);

/// <summary>One instruction step as returned across the API boundary. <see cref="Order"/> is 1-based.</summary>
public record StepServiceModel(int Order, string Instruction);
