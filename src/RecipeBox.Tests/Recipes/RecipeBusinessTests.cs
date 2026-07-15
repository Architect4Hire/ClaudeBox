using NSubstitute;
using RecipeBox.ApiService.Business;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Business orchestration with a mocked repository: the list pass-through of projected summaries,
/// domain-entity → service-model mapping on detail reads, and on create the view-model → domain
/// translation plus the data-dependent unique-name rule.
/// </summary>
public class RecipeBusinessTests
{
    private readonly IRecipeRepository _repository = Substitute.For<IRecipeRepository>();
    private readonly RecipeBusiness _sut;

    public RecipeBusinessTests() => _sut = new RecipeBusiness(_repository);

    private static CreateRecipeViewModel ValidViewModel(string name) => new(
        Name: name,
        Description: "Fluffy",
        Servings: 4,
        Ingredients: new List<CreateIngredientViewModel> { new("Flour", 2, "cups") },
        Steps: new List<CreateStepViewModel> { new(1, "Mix"), new(2, "Cook") });

    private static UpdateRecipeViewModel ValidUpdateViewModel(string name) => new(
        Name: name,
        Description: "Reworked",
        Servings: 6,
        Ingredients: new List<UpdateIngredientViewModel> { new("Rye", 3, "cups") },
        Steps: new List<UpdateStepViewModel> { new(1, "Knead"), new(2, "Prove"), new(3, "Bake") });

    [Fact]
    public async Task ListAsync_passes_repository_summaries_through()
    {
        var summaries = new List<RecipeSummaryServiceModel>
        {
            new(1, "Soup", "warm", 4, new[] { "Main" }, 3, 2),
        };
        _repository.ListAsync("Main", Arg.Any<CancellationToken>()).Returns(summaries);

        var result = await _sut.ListAsync("Main", CancellationToken.None);

        Assert.Same(summaries, result);
        await _repository.Received(1).ListAsync("Main", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_repository_has_no_recipe()
    {
        _repository.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns((Recipe?)null);

        var result = await _sut.GetByIdAsync(7, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_maps_domain_entity_to_service_model()
    {
        _repository.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(new Recipe
        {
            Id = 7,
            Name = "Stew",
            Servings = 6,
            Ingredients = { new Ingredient { Name = "Beef", Quantity = 500, Unit = "g" } },
            Steps =
            {
                new Step { Order = 2, Instruction = "Simmer" },
                new Step { Order = 1, Instruction = "Brown" },
            },
            Tags = { new Tag { Name = "hearty" } },
        });

        var result = await _sut.GetByIdAsync(7, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Stew", result!.Name);
        Assert.Equal(new[] { 1, 2 }, result.Steps.Select(s => s.Order).ToArray());
        Assert.Equal("Brown", result.Steps[0].Instruction);
        Assert.Equal(new[] { "hearty" }, result.Tags);
    }

    [Fact]
    public async Task CreateAsync_translates_view_model_persists_and_returns_service_model()
    {
        _repository.ExistsByNameAsync("Fresh Bread", Arg.Any<CancellationToken>()).Returns(false);
        _repository.AddAsync(Arg.Any<Recipe>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var recipe = ci.Arg<Recipe>();
            recipe.Id = 42; // the real repo assigns the id on save
            return recipe;
        });

        var result = await _sut.CreateAsync(ValidViewModel("Fresh Bread"), CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal("Fresh Bread", result.Name);
        Assert.Equal(2, result.Steps.Count);
        await _repository.Received(1).AddAsync(
            Arg.Is<Recipe>(r => r.Name == "Fresh Bread" && r.Ingredients.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_throws_and_does_not_persist_on_duplicate_name()
    {
        _repository.ExistsByNameAsync("Taken", Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<RecipeNameConflictException>(
            () => _sut.CreateAsync(ValidViewModel("Taken"), CancellationToken.None));

        await _repository.DidNotReceive().AddAsync(Arg.Any<Recipe>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_translates_view_model_persists_and_returns_service_model()
    {
        _repository.ExistsWithNameExceptAsync("Rye Loaf", 7, Arg.Any<CancellationToken>()).Returns(false);
        _repository.UpdateAsync(7, Arg.Any<Recipe>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var incoming = ci.ArgAt<Recipe>(1);
            incoming.Id = 7; // the real repo returns the persisted (tracked) entity, which carries the id
            return incoming;
        });

        var result = await _sut.UpdateAsync(7, ValidUpdateViewModel("Rye Loaf"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(7, result!.Id);
        Assert.Equal("Rye Loaf", result.Name);
        Assert.Equal(3, result.Steps.Count);
        await _repository.Received(1).UpdateAsync(
            7,
            Arg.Is<Recipe>(r => r.Name == "Rye Loaf" && r.Ingredients.Count == 1 && r.Steps.Count == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_returns_null_when_repository_has_no_recipe()
    {
        _repository.ExistsWithNameExceptAsync(Arg.Any<string>(), 7, Arg.Any<CancellationToken>()).Returns(false);
        _repository.UpdateAsync(7, Arg.Any<Recipe>(), Arg.Any<CancellationToken>()).Returns((Recipe?)null);

        var result = await _sut.UpdateAsync(7, ValidUpdateViewModel("Ghost"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_throws_and_does_not_persist_when_name_taken_by_another()
    {
        _repository.ExistsWithNameExceptAsync("Taken", 7, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<RecipeNameConflictException>(
            () => _sut.UpdateAsync(7, ValidUpdateViewModel("Taken"), CancellationToken.None));

        await _repository.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<Recipe>(), Arg.Any<CancellationToken>());
    }
}
