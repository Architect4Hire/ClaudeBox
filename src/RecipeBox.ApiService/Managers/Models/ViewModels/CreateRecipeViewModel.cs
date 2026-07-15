namespace RecipeBox.ApiService.Managers.Models.ViewModels;

/// <summary>
/// Inbound request shape for creating a recipe with its ingredients and ordered steps. ViewModels
/// are the ONLY types the controller binds from the wire; they never travel below the facade — the
/// business layer translates a validated view model into a domain entity.
/// </summary>
public record CreateRecipeViewModel(
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<CreateIngredientViewModel> Ingredients,
    IReadOnlyList<CreateStepViewModel> Steps);

/// <summary>An ingredient line within a <see cref="CreateRecipeViewModel"/>.</summary>
public record CreateIngredientViewModel(string Name, decimal Quantity, string? Unit);

/// <summary>An ordered instruction within a <see cref="CreateRecipeViewModel"/>. <see cref="Order"/> is 1-based.</summary>
public record CreateStepViewModel(int Order, string Instruction);
