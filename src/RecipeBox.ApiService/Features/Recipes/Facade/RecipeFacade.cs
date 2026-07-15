using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using RecipeBox.ApiService.Features.Recipes.Business;
using RecipeBox.ApiService.Features.Recipes.Dtos;

namespace RecipeBox.ApiService.Features.Recipes.Facade;

/// <summary>
/// Cross-cutting boundary for the Recipes feature: FluentValidation on writes, read-through caching
/// via the Aspire-provided <see cref="IDistributedCache"/> (keyed to the "cache" resource), and DTO
/// mapping. No orchestration or EF access — it delegates to <see cref="IRecipeBusiness"/>.
/// </summary>
public class RecipeFacade(
    IRecipeBusiness business,
    IValidator<CreateRecipeRequest> validator,
    IDistributedCache cache) : IRecipeFacade
{
    private readonly IRecipeBusiness _business = business;
    private readonly IValidator<CreateRecipeRequest> _validator = validator;
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

    public async Task<IReadOnlyList<RecipeSummaryDto>> ListAsync(string? category, CancellationToken ct)
    {
        // Category-filtered queries are not cached (see ListAllKey note).
        if (!string.IsNullOrWhiteSpace(category))
        {
            var filtered = await _business.ListAsync(category, ct);
            return filtered.Select(i => i.ToSummaryDto()).ToList();
        }

        var cached = await GetCachedAsync<List<RecipeSummaryDto>>(ListAllKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        var items = await _business.ListAsync(null, ct);
        var dtos = items.Select(i => i.ToSummaryDto()).ToList();
        await SetCachedAsync(ListAllKey, dtos, ct);
        return dtos;
    }

    public async Task<RecipeDetailDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var key = DetailKey(id);

        var cached = await GetCachedAsync<RecipeDetailDto>(key, ct);
        if (cached is not null)
        {
            return cached;
        }

        var recipe = await _business.GetByIdAsync(id, ct);
        if (recipe is null)
        {
            return null;
        }

        var dto = recipe.ToDetailDto();
        await SetCachedAsync(key, dto, ct);
        return dto;
    }

    public async Task<RecipeDetailDto> CreateAsync(CreateRecipeRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);

        var created = await _business.CreateAsync(request.ToEntity(), ct);

        // The new recipe now belongs in the unfiltered list; drop its cached copy.
        await _cache.RemoveAsync(ListAllKey, ct);

        return created.ToDetailDto();
    }

    private async Task<T?> GetCachedAsync<T>(string key, CancellationToken ct)
    {
        var json = await _cache.GetStringAsync(key, ct);
        return json is null ? default : JsonSerializer.Deserialize<T>(json);
    }

    private Task SetCachedAsync<T>(string key, T value, CancellationToken ct) =>
        _cache.SetStringAsync(key, JsonSerializer.Serialize(value), CacheOptions, ct);
}
