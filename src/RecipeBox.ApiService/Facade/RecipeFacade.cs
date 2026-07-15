using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using RecipeBox.ApiService.Business;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Facade;

/// <summary>
/// Cross-cutting boundary for the Recipes feature: FluentValidation of the incoming view model on
/// writes, read-through caching of service models via the Aspire-provided <see cref="IDistributedCache"/>
/// (keyed to the "cache" resource). No orchestration, mapping, or EF access — it delegates to
/// <see cref="IRecipeBusiness"/>.
/// </summary>
public class RecipeFacade(
    IRecipeBusiness business,
    IValidator<CreateRecipeViewModel> validator,
    IDistributedCache cache) : IRecipeFacade
{
    private readonly IRecipeBusiness _business = business;
    private readonly IValidator<CreateRecipeViewModel> _validator = validator;
    private readonly IDistributedCache _cache = cache;

    // The unfiltered list is the only cached list: a newly created recipe carries no categories yet
    // (create v1 takes ingredients + steps only), so category-filtered lists cannot be affected by a
    // create and are served straight from the business layer.
    private const string ListAllKey = "recipes:list:all";

    private static string DetailKey(int id) => $"recipe:{id}";

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    public async Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(string? category, CancellationToken ct)
    {
        // Category-filtered queries are not cached (see ListAllKey note).
        if (!string.IsNullOrWhiteSpace(category))
        {
            return await _business.ListAsync(category, ct);
        }

        var cached = await GetCachedAsync<List<RecipeSummaryServiceModel>>(ListAllKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        var models = await _business.ListAsync(null, ct);
        await SetCachedAsync(ListAllKey, models, ct);
        return models;
    }

    public async Task<RecipeDetailServiceModel?> GetByIdAsync(int id, CancellationToken ct)
    {
        var key = DetailKey(id);

        var cached = await GetCachedAsync<RecipeDetailServiceModel>(key, ct);
        if (cached is not null)
        {
            return cached;
        }

        var model = await _business.GetByIdAsync(id, ct);
        if (model is null)
        {
            return null;
        }

        await SetCachedAsync(key, model, ct);
        return model;
    }

    public async Task<RecipeDetailServiceModel> CreateAsync(CreateRecipeViewModel viewModel, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(viewModel, ct);

        var created = await _business.CreateAsync(viewModel, ct);

        // The new recipe now belongs in the unfiltered list; drop its cached copy.
        await _cache.RemoveAsync(ListAllKey, ct);

        return created;
    }

    private async Task<T?> GetCachedAsync<T>(string key, CancellationToken ct)
    {
        var json = await _cache.GetStringAsync(key, ct);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    private Task SetCachedAsync<T>(string key, T value, CancellationToken ct) =>
        _cache.SetStringAsync(key, JsonSerializer.Serialize(value), CacheOptions, ct);
}
