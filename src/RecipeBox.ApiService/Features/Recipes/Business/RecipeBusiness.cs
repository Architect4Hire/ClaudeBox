using RecipeBox.ApiService.Domain;
using RecipeBox.ApiService.Features.Recipes.Data;
using RecipeBox.ApiService.Features.Recipes.Models;

namespace RecipeBox.ApiService.Features.Recipes.Business;

/// <summary>
/// Orchestration for the Recipes feature. Depends only on <see cref="IRecipeRepository"/>; reads are
/// thin pass-throughs, and create applies the unique-name domain rule before persisting.
/// </summary>
public class RecipeBusiness(IRecipeRepository repository) : IRecipeBusiness
{
    private readonly IRecipeRepository _repository = repository;

    public Task<IReadOnlyList<RecipeListItem>> ListAsync(string? category, CancellationToken ct) =>
        _repository.ListAsync(category, ct);

    public Task<Recipe?> GetByIdAsync(int id, CancellationToken ct) =>
        _repository.GetByIdAsync(id, ct);

    public async Task<Recipe> CreateAsync(Recipe recipe, CancellationToken ct)
    {
        // Data-dependent rule: name uniqueness needs the DB, so it lives here, not in the facade.
        if (await _repository.ExistsByNameAsync(recipe.Name, ct))
        {
            throw new RecipeNameConflictException(recipe.Name);
        }

        return await _repository.AddAsync(recipe, ct);
    }
}
