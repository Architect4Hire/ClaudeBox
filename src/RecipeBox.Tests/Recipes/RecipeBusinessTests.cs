using NSubstitute;
using RecipeBox.ApiService.Business;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Business orchestration with a mocked data layer: the list pass-through of projected summaries,
/// domain-entity → service-model mapping on detail reads, and on create the view-model → domain
/// translation plus the data-dependent unique-name rule. The delete composition belongs to the data
/// layer now — see <see cref="RecipeDataLayerTests"/>.
/// </summary>
public class RecipeBusinessTests
{
    private readonly IRecipeDataLayer _data = Substitute.For<IRecipeDataLayer>();
    private readonly RecipeBusiness _sut;

    public RecipeBusinessTests() => _sut = new RecipeBusiness(_data);

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
    public async Task ListAsync_translates_the_view_model_and_passes_summaries_through()
    {
        var summaries = new List<RecipeSummaryServiceModel>
        {
            new(1, "Soup", "warm", 4, new[] { "Main" }, 3, 2),
        };
        _data.ListAsync(new RecipeFilter("Main", null), Arg.Any<CancellationToken>())
            .Returns(summaries);

        var result = await _sut.ListAsync(
            new RecipeFilterViewModel { Category = "Main" }, CancellationToken.None);

        Assert.Same(summaries, result);
        // The data layer is reached with domain criteria, never the view model.
        await _data.Received(1)
            .ListAsync(new RecipeFilter("Main", null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_normalizes_the_filter_before_it_reaches_the_data_layer()
    {
        // Trimmed, and blank → null ("any"), so the data layer never has to defend against either.
        await _sut.ListAsync(
            new RecipeFilterViewModel { Category = "   ", Ingredient = "  flour  " },
            CancellationToken.None);

        await _data.Received(1)
            .ListAsync(new RecipeFilter(null, "flour"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_data_layer_has_no_recipe()
    {
        _data.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns((Recipe?)null);

        var result = await _sut.GetByIdAsync(7, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_maps_domain_entity_to_service_model()
    {
        _data.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(new Recipe
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
        _data.ExistsByNameAsync("Fresh Bread", Arg.Any<CancellationToken>()).Returns(false);
        _data.AddAsync(Arg.Any<Recipe>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var recipe = ci.Arg<Recipe>();
            recipe.Id = 42; // the real repo assigns the id on save
            return recipe;
        });

        var result = await _sut.CreateAsync(ValidViewModel("Fresh Bread"), CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal("Fresh Bread", result.Name);
        Assert.Equal(2, result.Steps.Count);
        await _data.Received(1).AddAsync(
            Arg.Is<Recipe>(r => r.Name == "Fresh Bread" && r.Ingredients.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_throws_and_does_not_persist_on_duplicate_name()
    {
        _data.ExistsByNameAsync("Taken", Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<RecipeNameConflictException>(
            () => _sut.CreateAsync(ValidViewModel("Taken"), CancellationToken.None));

        await _data.DidNotReceive().AddAsync(Arg.Any<Recipe>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_translates_view_model_persists_and_returns_service_model()
    {
        _data.ExistsWithNameExceptAsync("Rye Loaf", 7, Arg.Any<CancellationToken>()).Returns(false);
        _data.UpdateAsync(7, Arg.Any<Recipe>(), Arg.Any<CancellationToken>()).Returns(ci =>
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
        await _data.Received(1).UpdateAsync(
            7,
            Arg.Is<Recipe>(r => r.Name == "Rye Loaf" && r.Ingredients.Count == 1 && r.Steps.Count == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_returns_null_when_data_layer_has_no_recipe()
    {
        _data.ExistsWithNameExceptAsync(Arg.Any<string>(), 7, Arg.Any<CancellationToken>()).Returns(false);
        _data.UpdateAsync(7, Arg.Any<Recipe>(), Arg.Any<CancellationToken>()).Returns((Recipe?)null);

        var result = await _sut.UpdateAsync(7, ValidUpdateViewModel("Ghost"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_throws_and_does_not_persist_when_name_taken_by_another()
    {
        _data.ExistsWithNameExceptAsync("Taken", 7, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<RecipeNameConflictException>(
            () => _sut.UpdateAsync(7, ValidUpdateViewModel("Taken"), CancellationToken.None));

        await _data.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<Recipe>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteAsync_delegates_to_the_data_layer_and_returns_its_answer(bool deleted)
    {
        // Delete carries no domain rule of its own: the recipe and the taxonomy it orphaned come off
        // together as one data operation, so business has nothing to sequence. The reaping itself is
        // asserted in RecipeDataLayerTests.
        _data.DeleteRecipeAsync(7, Arg.Any<CancellationToken>()).Returns(deleted);

        var result = await _sut.DeleteAsync(7, CancellationToken.None);

        Assert.Equal(deleted, result);
        await _data.Received(1).DeleteRecipeAsync(7, Arg.Any<CancellationToken>());
    }
}
