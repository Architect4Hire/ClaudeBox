using RecipeBox.ApiService.Domain;
using RecipeBox.ApiService.Features.Recipes.Dtos;
using RecipeBox.ApiService.Features.Recipes.Models;

namespace RecipeBox.ApiService.Features.Recipes;

/// <summary>
/// Boundary mapping between EF entities / internal models and the API DTOs. Keeping this in one
/// place is how the facade guarantees no EF entity is ever returned to a client.
/// </summary>
public static class RecipeMappings
{
    public static RecipeSummaryDto ToSummaryDto(this RecipeListItem item) =>
        new(item.Id, item.Name, item.Description, item.Servings, item.Categories, item.IngredientCount, item.StepCount);

    public static RecipeDetailDto ToDetailDto(this Recipe recipe) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.Description,
            recipe.Servings,
            recipe.Ingredients.Select(i => new IngredientDto(i.Name, i.Quantity, i.Unit)).ToList(),
            recipe.Steps.OrderBy(s => s.Order).Select(s => new StepDto(s.Order, s.Instruction)).ToList(),
            recipe.Categories.Select(c => c.Name).ToList(),
            recipe.Tags.Select(t => t.Name).ToList());

    public static Recipe ToEntity(this CreateRecipeRequest request) =>
        new()
        {
            Name = request.Name,
            Description = request.Description,
            Servings = request.Servings,
            Ingredients = request.Ingredients
                .Select(i => new Ingredient { Name = i.Name, Quantity = i.Quantity, Unit = i.Unit })
                .ToList(),
            Steps = request.Steps
                .Select(s => new Step { Order = s.Order, Instruction = s.Instruction })
                .ToList(),
        };
}
