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
    IValidator<CreateRecipeViewModel> createValidator,
    IValidator<UpdateRecipeViewModel> updateValidator,
    IDistributedCache cache) : IRecipeFacade
{
    private readonly IRecipeBusiness _business = business;
    private readonly IValidator<CreateRecipeViewModel> _createValidator = createValidator;
    private readonly IValidator<UpdateRecipeViewModel> _updateValidator = updateValidator;
    private readonly IDistributedCache _cache = cache;

    // The unfiltered list is the only cached list. A create/update can now attach categories, so it
    // can affect category-filtered lists too — but those are never cached (they bypass to the business
    // layer, see ListAsync), so invalidating this one unfiltered key on write is still sufficient.
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
        await _createValidator.ValidateAndThrowAsync(viewModel, ct);

        var created = await _business.CreateAsync(viewModel, ct);

        // The new recipe now belongs in the unfiltered list; drop its cached copy.
        await _cache.RemoveAsync(ListAllKey, ct);

        return created;
    }

    public async Task<RecipeDetailServiceModel?> UpdateAsync(int id, UpdateRecipeViewModel viewModel, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(viewModel, ct);

        var updated = await _business.UpdateAsync(id, viewModel, ct);
        if (updated is null)
        {
            return null;
        }

        // The edit can change fields shown in the unfiltered list (name, servings, description) and
        // certainly changes this recipe's detail, so drop both cached copies.
        await _cache.RemoveAsync(DetailKey(id), ct);
        await _cache.RemoveAsync(ListAllKey, ct);

        return updated;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        // No validation step: there is no view model to validate, only a route id.
        var deleted = await _business.DeleteAsync(id, ct);
        if (!deleted)
        {
            // Nothing changed, so the cached copies are still accurate — leave them alone.
            return false;
        }

        // The recipe is gone from both its own detail view and the unfiltered list.
        await _cache.RemoveAsync(DetailKey(id), ct);
        await _cache.RemoveAsync(ListAllKey, ct);

        return true;
    }

    private async Task<T?> GetCachedAsync<T>(string key, CancellationToken ct)
    {
        var json = await _cache.GetStringAsync(key, ct);
        if (json is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            // A corrupt or version-incompatible cached payload must not fail the request. Treat it as
            // a miss so the caller falls through to the business layer (and overwrites the bad entry).
            return default;
        }
    }

    private Task SetCachedAsync<T>(string key, T value, CancellationToken ct) =>
        _cache.SetStringAsync(key, JsonSerializer.Serialize(value), CacheOptions, ct);
}
