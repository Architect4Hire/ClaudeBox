using NSubstitute;
using RecipeBox.ApiService.Domain;
using RecipeBox.ApiService.Features.Recipes;
using RecipeBox.ApiService.Features.Recipes.Business;
using RecipeBox.ApiService.Features.Recipes.Data;
using RecipeBox.ApiService.Features.Recipes.Models;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>Business orchestration + the data-dependent unique-name rule, with a mocked repository.</summary>
public class RecipeBusinessTests
{
    private readonly IRecipeRepository _repository = Substitute.For<IRecipeRepository>();
    private readonly RecipeBusiness _sut;

    public RecipeBusinessTests() => _sut = new RecipeBusiness(_repository);

    [Fact]
    public async Task ListAsync_passes_category_through_to_repository()
    {
        var expected = new List<RecipeListItem>
        {
            new(1, "Soup", null, 4, new[] { "Main" }, 3, 2),
        };
        _repository.ListAsync("Main", Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.ListAsync("Main", CancellationToken.None);

        Assert.Same(expected, result);
        await _repository.Received(1).ListAsync("Main", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_passes_through_to_repository()
    {
        var recipe = new Recipe { Id = 7, Name = "Stew" };
        _repository.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(recipe);

        var result = await _sut.GetByIdAsync(7, CancellationToken.None);

        Assert.Same(recipe, result);
    }

    [Fact]
    public async Task CreateAsync_persists_when_name_is_unique()
    {
        var recipe = new Recipe { Id = 0, Name = "Fresh Bread" };
        _repository.ExistsByNameAsync("Fresh Bread", Arg.Any<CancellationToken>()).Returns(false);
        _repository.AddAsync(recipe, Arg.Any<CancellationToken>()).Returns(ci =>
        {
            recipe.Id = 42;
            return recipe;
        });

        var result = await _sut.CreateAsync(recipe, CancellationToken.None);

        Assert.Equal(42, result.Id);
        await _repository.Received(1).AddAsync(recipe, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_throws_and_does_not_persist_on_duplicate_name()
    {
        var recipe = new Recipe { Name = "Taken" };
        _repository.ExistsByNameAsync("Taken", Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<RecipeNameConflictException>(
            () => _sut.CreateAsync(recipe, CancellationToken.None));

        await _repository.DidNotReceive().AddAsync(Arg.Any<Recipe>(), Arg.Any<CancellationToken>());
    }
}
