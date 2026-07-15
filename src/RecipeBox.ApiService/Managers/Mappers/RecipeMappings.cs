using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Managers.Mappers;

/// <summary>
/// The Recipes feature's mapping seams, one per layer boundary:
/// <list type="bullet">
///   <item><b>ViewModel → domain</b> (business): a validated request becomes an entity to persist.</item>
///   <item><b>domain → service model</b> (business): a loaded entity becomes the wire response.</item>
/// </list>
/// Keeping these here is how each layer trades in only the types it owns — no EF entity ever reaches
/// the controller, and no view model ever reaches the database.
/// </summary>
public static class RecipeMappings
{
    // ── ViewModel → domain (business layer) ──────────────────────────────────────────────────────

    public static Recipe ToEntity(this CreateRecipeViewModel viewModel) =>
        new()
        {
            Name = viewModel.Name,
            Description = viewModel.Description,
            Servings = viewModel.Servings,
            Ingredients = viewModel.Ingredients
                .Select(i => new Ingredient { Name = i.Name, Quantity = i.Quantity, Unit = i.Unit })
                .ToList(),
            Steps = viewModel.Steps
                .Select(s => new Step { Order = s.Order, Instruction = s.Instruction })
                .ToList(),
        };

    // ── domain → service model (business layer) ──────────────────────────────────────────────────

    public static RecipeDetailServiceModel ToServiceModel(this Recipe recipe) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.Description,
            recipe.Servings,
            recipe.Ingredients.Select(i => new IngredientServiceModel(i.Name, i.Quantity, i.Unit)).ToList(),
            recipe.Steps.OrderBy(s => s.Order).Select(s => new StepServiceModel(s.Order, s.Instruction)).ToList(),
            recipe.Categories.Select(c => c.Name).ToList(),
            recipe.Tags.Select(t => t.Name).ToList());
}
