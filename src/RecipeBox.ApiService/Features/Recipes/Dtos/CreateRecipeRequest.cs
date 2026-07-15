namespace RecipeBox.ApiService.Features.Recipes.Dtos;

/// <summary>Payload for creating a recipe together with its ingredients and ordered steps.</summary>
public record CreateRecipeRequest(
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<CreateIngredientRequest> Ingredients,
    IReadOnlyList<CreateStepRequest> Steps);

/// <summary>An ingredient line within a <see cref="CreateRecipeRequest"/>.</summary>
public record CreateIngredientRequest(string Name, decimal Quantity, string? Unit);

/// <summary>An ordered instruction within a <see cref="CreateRecipeRequest"/>. <see cref="Order"/> is 1-based.</summary>
public record CreateStepRequest(int Order, string Instruction);
