namespace RecipeBox.ApiService.Managers.Models.ViewModels;

/// <summary>
/// Inbound request shape for replacing a recipe's editable content — its header, ingredients, ordered
/// steps, and taxonomy. Like <see cref="CreateRecipeViewModel"/>, an update replaces the recipe's
/// categories and tags wholesale with the supplied names (the data layer resolves each to an existing
/// row or creates one). ViewModels are the ONLY types the controller binds from the wire; the business
/// layer translates a validated one onto the existing domain entity.
/// <para><see cref="Categories"/> and <see cref="Tags"/> are optional — an omitted list binds to
/// <c>null</c> and is treated as empty (i.e. clears that taxonomy on the recipe).</para>
/// </summary>
public record UpdateRecipeViewModel(
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<UpdateIngredientViewModel> Ingredients,
    IReadOnlyList<UpdateStepViewModel> Steps,
    IReadOnlyList<string> Categories = null!,
    IReadOnlyList<string> Tags = null!);

/// <summary>An ingredient line within an <see cref="UpdateRecipeViewModel"/>.</summary>
public record UpdateIngredientViewModel(string Name, decimal Quantity, string? Unit);

/// <summary>An ordered instruction within an <see cref="UpdateRecipeViewModel"/>. <see cref="Order"/> is 1-based.</summary>
public record UpdateStepViewModel(int Order, string Instruction);
