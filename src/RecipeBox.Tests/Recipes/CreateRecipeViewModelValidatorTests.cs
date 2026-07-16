using RecipeBox.ApiService.Managers.Models.ViewModels;
using RecipeBox.ApiService.Managers.Validators;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Rule-by-rule tests for <see cref="CreateRecipeViewModelValidator"/>: the shape/format rules the
/// facade enforces at the edge. Each case starts from a valid model and breaks exactly one rule, so a
/// failure points at the specific rule that regressed (the facade/endpoint tests only exercise these
/// coarsely, via "everything blank" / "no steps").
/// </summary>
public class CreateRecipeViewModelValidatorTests
{
    private readonly CreateRecipeViewModelValidator _sut = new();

    private static CreateRecipeViewModel Valid(
        string name = "Pancakes",
        string? description = "Fluffy",
        int servings = 4,
        IReadOnlyList<CreateIngredientViewModel>? ingredients = null,
        IReadOnlyList<CreateStepViewModel>? steps = null) =>
        new(
            name,
            description,
            servings,
            ingredients ?? new List<CreateIngredientViewModel> { new("Flour", 2, "cups") },
            steps ?? new List<CreateStepViewModel> { new(1, "Mix"), new(2, "Cook") });

    private bool FailsFor(CreateRecipeViewModel model, string propertyName) =>
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
    public void Name_at_200_chars_passes()
    {
        Assert.False(FailsFor(Valid(name: new string('a', 200)), "Name"));
    }

    [Fact]
    public void Description_over_2000_chars_fails()
    {
        Assert.True(FailsFor(Valid(description: new string('a', 2001)), "Description"));
    }

    [Fact]
    public void Null_description_is_allowed()
    {
        Assert.True(_sut.Validate(Valid(description: null)).IsValid);
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
        Assert.True(FailsFor(Valid(ingredients: new List<CreateIngredientViewModel>()), "Ingredients"));
    }

    [Fact]
    public void Ingredient_name_is_required()
    {
        var model = Valid(ingredients: new List<CreateIngredientViewModel> { new("", 1, "g") });
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Negative_ingredient_quantity_fails()
    {
        var model = Valid(ingredients: new List<CreateIngredientViewModel> { new("Flour", -1, "g") });
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Ingredient_unit_over_50_chars_fails()
    {
        var model = Valid(ingredients: new List<CreateIngredientViewModel> { new("Flour", 1, new string('u', 51)) });
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void At_least_one_step_is_required()
    {
        Assert.True(FailsFor(Valid(steps: new List<CreateStepViewModel>()), "Steps"));
    }

    [Fact]
    public void Step_order_below_1_fails()
    {
        var model = Valid(steps: new List<CreateStepViewModel> { new(0, "Mix") });
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Step_instruction_is_required()
    {
        var model = Valid(steps: new List<CreateStepViewModel> { new(1, "") });
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Duplicate_step_orders_fail_with_message()
    {
        var model = Valid(steps: new List<CreateStepViewModel> { new(1, "Mix"), new(1, "Cook") });

        var result = _sut.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Step orders must be unique within a recipe.");
    }

    [Fact]
    public void Unique_step_orders_pass()
    {
        var model = Valid(steps: new List<CreateStepViewModel> { new(1, "Mix"), new(2, "Cook"), new(3, "Serve") });

        Assert.True(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Omitted_taxonomy_is_allowed()
    {
        // Valid() leaves Categories/Tags at their default (null); that must pass.
        Assert.True(_sut.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Supplied_taxonomy_passes()
    {
        var model = Valid() with
        {
            Categories = new List<string> { "Dessert" },
            Tags = new List<string> { "quick", "vegetarian" },
        };

        Assert.True(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Blank_category_name_fails()
    {
        var model = Valid() with { Categories = new List<string> { "" } };
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Category_over_100_chars_fails()
    {
        var model = Valid() with { Categories = new List<string> { new('c', 101) } };
        Assert.False(_sut.Validate(model).IsValid);
    }

    [Fact]
    public void Duplicate_categories_fail_case_insensitively_with_message()
    {
        var model = Valid() with { Categories = new List<string> { "Dessert", "dessert" } };

        var result = _sut.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Categories must be unique within a recipe.");
    }

    [Fact]
    public void Duplicate_tags_fail_with_message()
    {
        var model = Valid() with { Tags = new List<string> { "quick", "quick" } };

        var result = _sut.Validate(model);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Tags must be unique within a recipe.");
    }
}
