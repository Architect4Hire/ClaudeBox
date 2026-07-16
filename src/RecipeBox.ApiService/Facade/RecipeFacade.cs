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
    IValidator<RecipeFilterViewModel> filterValidator,
    IValidator<UploadRecipeImageViewModel> uploadImageValidator,
    IDistributedCache cache) : IRecipeFacade
{
    private readonly IRecipeBusiness _business = business;
    private readonly IValidator<CreateRecipeViewModel> _createValidator = createValidator;
    private readonly IValidator<UpdateRecipeViewModel> _updateValidator = updateValidator;
    private readonly IValidator<RecipeFilterViewModel> _filterValidator = filterValidator;
    private readonly IValidator<UploadRecipeImageViewModel> _uploadImageValidator = uploadImageValidator;
    private readonly IDistributedCache _cache = cache;

    // The unfiltered list is the only cached list. A create/update can affect filtered lists too (it
    // can attach categories and change ingredients) — but those are never cached (they bypass to the
    // business layer, see ListAsync), so invalidating this one unfiltered key on write is sufficient.
    private const string ListAllKey = "recipes:list:all";

    private static string DetailKey(int id) => $"recipe:{id}";

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    public async Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(
        RecipeFilterViewModel filter, CancellationToken ct)
    {
        await _filterValidator.ValidateAndThrowAsync(filter, ct);

        // The view model goes down to business exactly as it arrived: normalizing it (trim, blank →
        // null) is the business layer's VM→domain step, not the facade's. Nothing is lost by waiting —
        // HasFilter already treats a blank filter as "no filter", so a whitespace-only request takes
        // the cached unfiltered path below and gets the same result a truly absent filter would.
        //
        // Filtered queries are not cached (see ListAllKey note) — an ingredient search is open-ended
        // free text, so caching per term would fill the cache with near-unrepeatable keys.
        if (filter.HasFilter)
        {
            return await _business.ListAsync(filter, ct);
        }

        var cached = await GetCachedAsync<List<RecipeSummaryServiceModel>>(ListAllKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        var models = await _business.ListAsync(filter, ct);
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
        await InvalidateAsync(id, ct);

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
        await InvalidateAsync(id, ct);

        return true;
    }

    // The image itself is never cached: a service model here owns an open stream, which reads once and
    // can't be serialized to Redis or replayed to a second caller. It doesn't need to be — the response
    // carries the blob's ETag, so a browser revalidates with If-None-Match and an unchanged image costs
    // a 304 rather than a resend. The summary/detail JSON is cached, which is why the write paths below
    // must invalidate it.
    public Task<RecipeImageServiceModel?> GetImageAsync(int id, CancellationToken ct) =>
        _business.GetImageAsync(id, ct);

    public async Task<bool> SetImageAsync(int id, UploadRecipeImageViewModel viewModel, CancellationToken ct)
    {
        await _uploadImageValidator.ValidateAndThrowAsync(viewModel, ct);

        var stored = await _business.SetImageAsync(id, viewModel, ct);
        if (!stored)
        {
            // No recipe was touched, so the cached copies are still accurate — leave them alone.
            return false;
        }

        // Both cached shapes carry HasImage, and it has just flipped. Miss this and the new image stays
        // invisible for up to the cache TTL: the client is told the recipe has no image, so it never
        // requests one, and no amount of ETag revalidation helps a URL nobody fetches.
        await InvalidateAsync(id, ct);

        return true;
    }

    public async Task<bool> RemoveImageAsync(int id, CancellationToken ct)
    {
        // No validation step: there is no view model to validate, only a route id.
        var removed = await _business.RemoveImageAsync(id, ct);
        if (!removed)
        {
            return false;
        }

        // HasImage has flipped the other way; the same staleness argument as SetImageAsync applies, and
        // here the cached copy would point the client at bytes that no longer exist.
        await InvalidateAsync(id, ct);

        return true;
    }

    // Both cached shapes describe the recipe, so any write to it drops both: its own detail, and the
    // unfiltered list it appears in.
    private async Task InvalidateAsync(int id, CancellationToken ct)
    {
        await _cache.RemoveAsync(DetailKey(id), ct);
        await _cache.RemoveAsync(ListAllKey, ct);
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
