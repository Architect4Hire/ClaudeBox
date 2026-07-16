using RecipeBox.ApiService.Managers.Models.ViewModels;
using RecipeBox.ApiService.Managers.Validators;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Edge validation of the list filter. Both criteria are optional, so the interesting cases are the
/// permissive ones (an absent filter is valid) and the length caps that bound the ingredient scan.
/// </summary>
public class RecipeFilterViewModelValidatorTests
{
    private readonly RecipeFilterViewModelValidator _sut = new();

    [Fact]
    public void Empty_filter_is_valid()
    {
        // The unfiltered list request — the common case.
        Assert.True(_sut.Validate(new RecipeFilterViewModel()).IsValid);
    }

    [Fact]
    public void Filter_with_both_criteria_is_valid()
    {
        var result = _sut.Validate(new RecipeFilterViewModel
        {
            Category = "Dessert",
            Ingredient = "flour",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Ingredient_at_the_length_cap_is_valid()
    {
        var result = _sut.Validate(new RecipeFilterViewModel { Ingredient = new string('x', 200) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Ingredient_over_the_length_cap_fails()
    {
        var result = _sut.Validate(new RecipeFilterViewModel { Ingredient = new string('x', 201) });

        Assert.False(result.IsValid);
        Assert.Equal(nameof(RecipeFilterViewModel.Ingredient), Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void Category_over_the_length_cap_fails()
    {
        var result = _sut.Validate(new RecipeFilterViewModel { Category = new string('x', 101) });

        Assert.False(result.IsValid);
        Assert.Equal(nameof(RecipeFilterViewModel.Category), Assert.Single(result.Errors).PropertyName);
    }
}
