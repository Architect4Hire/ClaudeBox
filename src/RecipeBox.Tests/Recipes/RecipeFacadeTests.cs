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
    private readonly IValidator<CreateRecipeViewModel> _createValidator = new CreateRecipeViewModelValidator();
    private readonly IValidator<UpdateRecipeViewModel> _updateValidator = new UpdateRecipeViewModelValidator();
    private readonly IDistributedCache _cache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly RecipeFacade _sut;

    public RecipeFacadeTests() =>
        _sut = new RecipeFacade(_business, _createValidator, _updateValidator, _cache);

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
        Steps: new List<UpdateStepViewModel> { new(1, "Knead"), new(2, "Bake") });

    private static RecipeDetailServiceModel DetailModel(int id, string name) => new(
        id, name, "Reworked", 6,
        new List<IngredientServiceModel> { new("Rye", 3, "cups") },
        new List<StepServiceModel> { new(1, "Knead"), new(2, "Bake") },
        new List<string>(), new List<string>());

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

    [Fact]
    public async Task UpdateAsync_throws_validation_and_never_calls_business()
    {
        var invalid = new UpdateRecipeViewModel(
            Name: "",
            Description: null,
            Servings: 0,
            Ingredients: new List<UpdateIngredientViewModel>(),
            Steps: new List<UpdateStepViewModel>());

        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.UpdateAsync(5, invalid, CancellationToken.None));

        await _business.DidNotReceive().UpdateAsync(
            Arg.Any<int>(), Arg.Any<UpdateRecipeViewModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_returns_business_result_and_invalidates_detail_and_list_caches()
    {
        // Prime both caches so we can prove each is invalidated by a successful update.
        await _cache.SetStringAsync(ListAllKey, JsonSerializer.Serialize(new List<RecipeSummaryServiceModel>()));
        await _cache.SetStringAsync("recipe:5", JsonSerializer.Serialize(DetailModel(5, "Old")));

        _business.UpdateAsync(5, Arg.Any<UpdateRecipeViewModel>(), Arg.Any<CancellationToken>())
            .Returns(DetailModel(5, "Rye Loaf"));

        var result = await _sut.UpdateAsync(5, ValidUpdateViewModel("Rye Loaf"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Rye Loaf", result!.Name);
        Assert.Null(await _cache.GetStringAsync("recipe:5"));
        Assert.Null(await _cache.GetStringAsync(ListAllKey));
    }

    [Fact]
    public async Task UpdateAsync_returns_null_and_leaves_caches_intact_when_missing()
    {
        await _cache.SetStringAsync(ListAllKey, JsonSerializer.Serialize(new List<RecipeSummaryServiceModel>()));
        _business.UpdateAsync(999, Arg.Any<UpdateRecipeViewModel>(), Arg.Any<CancellationToken>())
            .Returns((RecipeDetailServiceModel?)null);

        var result = await _sut.UpdateAsync(999, ValidUpdateViewModel("Ghost"), CancellationToken.None);

        Assert.Null(result);
        // A no-op update must not blow away the cached list.
        Assert.NotNull(await _cache.GetStringAsync(ListAllKey));
    }

    [Fact]
    public async Task ListAsync_with_whitespace_category_uses_cached_unfiltered_path()
    {
        _business.ListAsync(null, Arg.Any<CancellationToken>()).Returns(new List<RecipeSummaryServiceModel>
        {
            new(1, "FromDb", null, 4, new[] { "Main" }, 2, 3),
        });

        var result = await _sut.ListAsync("   ", CancellationToken.None);

        Assert.Equal("FromDb", result[0].Name);
        // Whitespace is "no filter": the business layer is queried with null and the result is cached,
        // never queried with the raw whitespace string.
        await _business.Received(1).ListAsync(null, Arg.Any<CancellationToken>());
        await _business.DidNotReceive().ListAsync("   ", Arg.Any<CancellationToken>());
        Assert.NotNull(await _cache.GetStringAsync(ListAllKey));
    }

    [Fact]
    public async Task ListAsync_falls_back_to_business_when_cached_payload_is_corrupt()
    {
        const string corrupt = "{ this is not valid json";
        await _cache.SetStringAsync(ListAllKey, corrupt);
        _business.ListAsync(null, Arg.Any<CancellationToken>()).Returns(new List<RecipeSummaryServiceModel>
        {
            new(5, "FromDb", null, 4, new[] { "Main" }, 2, 3),
        });

        var result = await _sut.ListAsync(null, CancellationToken.None);

        // A corrupt cache entry must not throw: it is treated as a miss and served from the business
        // layer, which then overwrites the bad entry.
        Assert.Equal("FromDb", Assert.Single(result).Name);
        await _business.Received(1).ListAsync(null, Arg.Any<CancellationToken>());
        Assert.NotEqual(corrupt, await _cache.GetStringAsync(ListAllKey));
    }

    [Fact]
    public async Task DeleteAsync_invalidates_the_detail_and_list_entries()
    {
        await _cache.SetStringAsync(ListAllKey, JsonSerializer.Serialize(new List<RecipeSummaryServiceModel>
        {
            new(7, "Doomed", null, 2, Array.Empty<string>(), 1, 1),
        }));
        await _cache.SetStringAsync("recipe:7", JsonSerializer.Serialize(DetailModel(7, "Doomed")));
        _business.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteAsync(7, CancellationToken.None);

        Assert.True(result);
        // A stale entry here would serve a recipe that no longer exists.
        Assert.Null(await _cache.GetStringAsync("recipe:7"));
        Assert.Null(await _cache.GetStringAsync(ListAllKey));
    }

    [Fact]
    public async Task DeleteAsync_leaves_the_cache_intact_when_recipe_is_missing()
    {
        var cachedList = JsonSerializer.Serialize(new List<RecipeSummaryServiceModel>
        {
            new(1, "Untouched", null, 2, Array.Empty<string>(), 1, 1),
        });
        await _cache.SetStringAsync(ListAllKey, cachedList);
        _business.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteAsync(7, CancellationToken.None);

        Assert.False(result);
        // Nothing was deleted, so the cached list is still accurate — evicting it would be needless churn.
        Assert.Equal(cachedList, await _cache.GetStringAsync(ListAllKey));
    }
}
