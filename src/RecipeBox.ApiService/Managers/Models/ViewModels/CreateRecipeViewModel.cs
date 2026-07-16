namespace RecipeBox.ApiService.Managers.Models.ViewModels;

/// <summary>
/// Inbound request shape for creating a recipe with its ingredients, ordered steps, and taxonomy.
/// ViewModels are the ONLY types the controller binds from the wire; they never travel below the
/// facade — the business layer translates a validated view model into a domain entity.
/// <para><see cref="Categories"/> and <see cref="Tags"/> are free-text names; the data layer resolves
/// each to an existing taxonomy row or creates one. Both are optional — an omitted list binds to
/// <c>null</c> and is treated as empty.</para>
/// </summary>
public record CreateRecipeViewModel(
    string Name,
    string? Description,
    int Servings,
    IReadOnlyList<CreateIngredientViewModel> Ingredients,
    IReadOnlyList<CreateStepViewModel> Steps,
    IReadOnlyList<string> Categories = null!,
    IReadOnlyList<string> Tags = null!);

/// <summary>An ingredient line within a <see cref="CreateRecipeViewModel"/>.</summary>
public record CreateIngredientViewModel(string Name, decimal Quantity, string? Unit);

/// <summary>An ordered instruction within a <see cref="CreateRecipeViewModel"/>. <see cref="Order"/> is 1-based.</summary>
public record CreateStepViewModel(int Order, string Instruction);
