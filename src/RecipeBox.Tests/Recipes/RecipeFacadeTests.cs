using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using RecipeBox.ApiService.Business;
using RecipeBox.ApiService.Facade;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;
using RecipeBox.ApiService.Managers.Validators;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Facade behaviour: view-model validation, read-through caching of service models (hit / miss), and
/// write invalidation — with a mocked business layer and a real in-memory <see cref="IDistributedCache"/>.
/// </summary>
public class RecipeFacadeTests
{
    private const string ListAllKey = "recipes:list:all";

    private readonly IRecipeBusiness _business = Substitute.For<IRecipeBusiness>();
    private readonly IValidator<CreateRecipeViewModel> _validator = new CreateRecipeViewModelValidator();
    private readonly IDistributedCache _cache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly RecipeFacade _sut;

    public RecipeFacadeTests() => _sut = new RecipeFacade(_business, _validator, _cache);

    private static CreateRecipeViewModel ValidViewModel(string name) => new(
        Name: name,
        Description: "Fluffy",
        Servings: 4,
        Ingredients: new List<CreateIngredientViewModel> { new("Flour", 2, "cups") },
        Steps: new List<CreateStepViewModel> { new(1, "Mix"), new(2, "Cook") });

    [Fact]
    public async Task ListAsync_returns_cached_result_without_calling_business_on_hit()
    {
        var cached = new List<RecipeSummaryServiceModel>
        {
            new(1, "Cached", null, 2, new[] { "Main" }, 1, 1),
        };
        await _cache.SetStringAsync(ListAllKey, JsonSerializer.Serialize(cached));

        var result = await _sut.ListAsync(null, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Cached", result[0].Name);
        await _business.DidNotReceive().ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_calls_business_and_populates_cache_on_miss()
    {
        _business.ListAsync(null, Arg.Any<CancellationToken>()).Returns(new List<RecipeSummaryServiceModel>
        {
            new(5, "FromDb", null, 4, new[] { "Main" }, 2, 3),
        });

        var first = await _sut.ListAsync(null, CancellationToken.None);
        var second = await _sut.ListAsync(null, CancellationToken.None);

        Assert.Equal("FromDb", first[0].Name);
        Assert.Equal("FromDb", second[0].Name);
        // Second call is served from cache: business is hit exactly once.
        await _business.Received(1).ListAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_with_category_bypasses_cache()
    {
        _business.ListAsync("Dessert", Arg.Any<CancellationToken>()).Returns(new List<RecipeSummaryServiceModel>
        {
            new(9, "Cake", null, 8, new[] { "Dessert" }, 4, 5),
        });

        await _sut.ListAsync("Dessert", CancellationToken.None);
        await _sut.ListAsync("Dessert", CancellationToken.None);

        // No caching for filtered lists — business is called every time.
        await _business.Received(2).ListAsync("Dessert", Arg.Any<CancellationToken>());
        Assert.Null(await _cache.GetStringAsync(ListAllKey));
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_and_does_not_cache_when_missing()
    {
        _business.GetByIdAsync(123, Arg.Any<CancellationToken>()).Returns((RecipeDetailServiceModel?)null);

        var result = await _sut.GetByIdAsync(123, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(await _cache.GetStringAsync("recipe:123"));
    }

    [Fact]
    public async Task CreateAsync_throws_validation_and_never_calls_business()
    {
        var invalid = new CreateRecipeViewModel(
            Name: "",
            Description: null,
            Servings: 0,
            Ingredients: new List<CreateIngredientViewModel>(),
            Steps: new List<CreateStepViewModel>());

        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.CreateAsync(invalid, CancellationToken.None));

        await _business.DidNotReceive().CreateAsync(Arg.Any<CreateRecipeViewModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_requires_at_least_one_step()
    {
        var noSteps = new CreateRecipeViewModel(
            Name: "Salad",
            Description: null,
            Servings: 2,
            Ingredients: new List<CreateIngredientViewModel> { new("Lettuce", 1, "head") },
            Steps: new List<CreateStepViewModel>());

        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.CreateAsync(noSteps, CancellationToken.None));

        await _business.DidNotReceive().CreateAsync(Arg.Any<CreateRecipeViewModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_returns_business_result_and_invalidates_list_cache()
    {
        // Prime the unfiltered-list cache so we can prove it gets invalidated.
        await _cache.SetStringAsync(ListAllKey, JsonSerializer.Serialize(new List<RecipeSummaryServiceModel>()));

        _business.CreateAsync(Arg.Any<CreateRecipeViewModel>(), Arg.Any<CancellationToken>()).Returns(
            new RecipeDetailServiceModel(
                77, "Pancakes", "Fluffy", 4,
                new List<IngredientServiceModel> { new("Flour", 2, "cups") },
                new List<StepServiceModel> { new(1, "Mix"), new(2, "Cook") },
                new List<string>(), new List<string>()));

        var result = await _sut.CreateAsync(ValidViewModel("Pancakes"), CancellationToken.None);

        Assert.Equal(77, result.Id);
        Assert.Equal("Pancakes", result.Name);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(1, result.Steps[0].Order);
        Assert.Null(await _cache.GetStringAsync(ListAllKey));
    }
}
