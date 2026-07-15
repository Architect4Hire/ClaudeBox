using RecipeBox.ApiService.Managers.Models.ViewModels;
using RecipeBox.ApiService.Managers.Validators;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Rule-by-rule tests for <see cref="UpdateRecipeViewModelValidator"/>. It mirrors the create
/// validator (shape/format only; the data-dependent unique-name rule lives in the business layer),
/// so this covers the same boundaries against the Update view models to guard against the two
/// validators drifting apart.
/// </summary>
public class UpdateRecipeViewModelValidatorTests
{
    private readonly UpdateRecipeViewModelValidator _sut = new();

    private static UpdateRecipeViewModel Valid(
        string name = "Sourdough",
        string? description = "Tangy",
        int servings = 6,
        IReadOnlyList<UpdateIngredientViewModel>? ingredients = null,
        IReadOnlyList<UpdateStepViewModel>? steps = null) =>
        new(
            name,
            description,
            servings,
            ingredients ?? new List<UpdateIngredientViewModel> { new("Rye", 3, "cups") },
            steps ?? new List<UpdateStepViewModel> { new(1, "Knead"), new(2, "Bake") });

    private bool FailsFor(UpdateRecipeViewModel model, string propertyName) =>
        _sut.Validate(model).Errors.Exists(e => e.PropertyName == propertyName);

    [Fact]
    public void Valid_model_passes()
    {
        Assert.True(_sut.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Name_is_required()
    {
        Assert.True(FailsFor(Valid(name: ""), "Name"));
    }

    [Fact]
    public void Name_over_200_chars_fails()
    {
        Assert.True(FailsFor(Valid(name: new string('a', 201)), "Name"));
    }

    [Fact]
    public void Description_over_2000_chars_fails()
    {
        Assert.True(FailsFor(Valid(description: new string('a', 2001)), "Description"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Servings_below_1_fails(int servings)
    {
        Assert.True(FailsFor(Valid(servings: servings), "Servings"));
    }

    [Fact]
    public void At_least_one_ingredient_is_required()
    {
        Assert.True(FailsFor(Valid(ingredients: new List<UpdateIngredientViewModel>()), "Ingredients"));
    }

    [Fact]
    public void Negative_ingredient_quantity_fails()
    {
        var model = Valid(ingredients: new List<UpdateIngredientViewModel> { new("Rye", -1, "g") });
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void At_least_one_step_is_required()
    {
        Assert.True(FailsFor(Valid(steps: new List<UpdateStepViewModel>()), "Steps"));
    }

    [Fact]
    public void Step_order_below_1_fails()
    {
        var model = Valid(steps: new List<UpdateStepViewModel> { new(0, "Knead") });
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Duplicate_step_orders_fail_with_message()
    {
        var model = Valid(steps: new List<UpdateStepViewModel> { new(1, "Knead"), new(1, "Bake") });

        var result = _sut.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Step orders must be unique within a recipe.");
    }

    [Fact]
    public void Unique_step_orders_pass()
    {
        var model = Valid(steps: new List<UpdateStepViewModel> { new(1, "Knead"), new(2, "Prove"), new(3, "Bake") });

        Assert.True(_sut.Validate(model).IsValid);
    }
}
