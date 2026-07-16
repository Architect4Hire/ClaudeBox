using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Composes <see cref="IRecipeRepository"/> operations into the whole data operations the business
/// layer asks for. Depends only on the repository interface — it holds no <c>DbContext</c> of its
/// own, so every query still belongs to the repository.
/// </summary>
public class RecipeDataLayer(IRecipeRepository repository) : IRecipeDataLayer
{
    private readonly IRecipeRepository _repository = repository;

    public Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(RecipeFilter filter, CancellationToken ct) =>
        _repository.ListAsync(filter, ct);

    public Task<Recipe?> GetByIdAsync(int id, CancellationToken ct) =>
        _repository.GetByIdAsync(id, ct);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct) =>
        _repository.ExistsByNameAsync(name, ct);

    public Task<bool> ExistsWithNameExceptAsync(string name, int excludingId, CancellationToken ct) =>
        _repository.ExistsWithNameExceptAsync(name, excludingId, ct);

    public Task<Recipe> AddAsync(Recipe recipe, CancellationToken ct) =>
        _repository.AddAsync(recipe, ct);

    public Task<Recipe?> UpdateAsync(int id, Recipe incoming, CancellationToken ct) =>
        _repository.UpdateAsync(id, incoming, ct);

    // Deleting a recipe is three repository calls, not one: neither taxonomy outlives the last recipe
    // that named it. Sequencing them here keeps the repository a set of pure data operations and
    // leaves business with a single call. The transaction makes those three calls one atomic
    // operation — a failed sweep takes the recipe delete back with it, so the store is never left
    // holding taxonomy no recipe references. Disposal without the commit below rolls back.
    public async Task<bool> DeleteRecipeAsync(int id, CancellationToken ct)
    {
        await using var transaction = await _repository.BeginTransactionAsync(ct);

        if (!await _repository.DeleteAsync(id, ct))
        {
            // Nothing was removed, so nothing can have been orphaned — neither sweep needs to run, and
            // the empty transaction rolls back on dispose with nothing to undo.
            return false;
        }

        await _repository.DeleteOrphanedCategoriesAsync(ct);
        await _repository.DeleteOrphanedTagsAsync(ct);

        await transaction.CommitAsync(ct);
        return true;
    }
}
