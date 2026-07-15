namespace RecipeBox.ApiService.Managers.Models.ViewModels;

/// <summary>
/// Inbound request shape for replacing a recipe's editable content — its header, ingredients, and
/// ordered steps. Like <see cref="CreateRecipeViewModel"/>, it carries no taxonomy: categories and
/// tags are managed elsewhere, so an update leaves them untouched. ViewModels are the ONLY types the
/// controller binds from the wire; the business layer translates a validated one onto the existing
/// domain entity.
/// </summary>
public record UpdateRecipeViewModel(
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<UpdateIngredientViewModel> Ingredients,
    IReadOnlyList<UpdateStepViewModel> Steps);

/// <summary>An ingredient line within an <see cref="UpdateRecipeViewModel"/>.</summary>
public record UpdateIngredientViewModel(string Name, decimal Quantity, string? Unit);

/// <summary>An ordered instruction within an <see cref="UpdateRecipeViewModel"/>. <see cref="Order"/> is 1-based.</summary>
public record UpdateStepViewModel(int Order, string Instruction);
