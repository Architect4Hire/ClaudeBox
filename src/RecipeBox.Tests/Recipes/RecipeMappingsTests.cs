using RecipeBox.ApiService.Managers.Mappers;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ViewModels;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Direct tests for the <see cref="RecipeMappings"/> layer-boundary mappings. These are only reached
/// indirectly through the business tests, so the edge cases below — null description round-trips, and
/// the update mapping deliberately producing an identity-less, taxonomy-less carrier — have no other
/// coverage.
/// </summary>
public class RecipeMappingsTests
{
    [Fact]
    public void CreateViewModel_ToEntity_maps_fields_and_children()
    {
        var viewModel = new CreateRecipeViewModel(
            Name: "Pancakes",
            Description: "Fluffy",
            Servings: 4,
            Ingredients: new List<CreateIngredientViewModel> { new("Flour", 2, "cups") },
            Steps: new List<CreateStepViewModel> { new(1, "Mix"), new(2, "Cook") });

        var entity = viewModel.ToEntity();

        Assert.Equal("Pancakes", entity.Name);
        Assert.Equal("Fluffy", entity.Description);
        Assert.Equal(4, entity.Servings);
        var ingredient = Assert.Single(entity.Ingredients);
        Assert.Equal("Flour", ingredient.Name);
        Assert.Equal(2, ingredient.Quantity);
        Assert.Equal("cups", ingredient.Unit);
        Assert.Equal(new[] { 1, 2 }, entity.Steps.Select(s => s.Order).ToArray());
    }

    [Fact]
    public void CreateViewModel_ToEntity_preserves_null_description()
    {
        var viewModel = new CreateRecipeViewModel(
            Name: "Salad",
            Description: null,
            Servings: 2,
            Ingredients: new List<CreateIngredientViewModel> { new("Lettuce", 1, null) },
            Steps: new List<CreateStepViewModel> { new(1, "Toss") });

        var entity = viewModel.ToEntity();

        Assert.Null(entity.Description);
        Assert.Null(Assert.Single(entity.Ingredients).Unit);
    }

    [Fact]
    public void UpdateViewModel_ToEntity_is_a_detached_carrier_without_id_or_taxonomy()
    {
        var viewModel = new UpdateRecipeViewModel(
            Name: "Sourdough",
            Description: "Tangy",
            Servings: 6,
            Ingredients: new List<UpdateIngredientViewModel> { new("Rye", 3, "cups") },
            Steps: new List<UpdateStepViewModel> { new(1, "Knead"), new(2, "Bake") });

        var entity = viewModel.ToEntity();

        // The repository — not the mapper — owns identity and taxonomy on an update.
        Assert.Equal(0, entity.Id);
        Assert.Empty(entity.Categories);
        Assert.Empty(entity.Tags);
        Assert.Equal("Sourdough", entity.Name);
        Assert.Equal(2, entity.Steps.Count);
    }

    [Fact]
    public void Recipe_ToServiceModel_orders_steps_and_maps_taxonomy_names()
    {
        var recipe = new Recipe
        {
            Id = 9,
            Name = "Cake",
            Description = null,
            Servings = 8,
            Ingredients = { new Ingredient { Name = "Flour", Quantity = 2, Unit = "cups" } },
            Steps =
            {
                new Step { Order = 3, Instruction = "Bake" },
                new Step { Order = 1, Instruction = "Mix" },
                new Step { Order = 2, Instruction = "Pour" },
            },
            Categories = { new Category { Name = "Dessert" } },
            Tags = { new Tag { Name = "Sweet" } },
        };

        var model = recipe.ToServiceModel();

        Assert.Equal(9, model.Id);
        Assert.Null(model.Description);
        Assert.Equal(new[] { 1, 2, 3 }, model.Steps.Select(s => s.Order).ToArray());
        Assert.Equal("Mix", model.Steps[0].Instruction);
        Assert.Equal(new[] { "Dessert" }, model.Categories);
        Assert.Equal(new[] { "Sweet" }, model.Tags);
    }
}
