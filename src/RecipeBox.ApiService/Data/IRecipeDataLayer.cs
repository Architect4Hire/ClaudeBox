using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// The business layer's view of persistence for the Recipes feature: it composes
/// <see cref="IRecipeRepository"/> calls into whole data operations, so a single logical read or
/// write is a single call from business no matter how many repository operations it takes. Most
/// methods pass an operation straight through; the ones that don't (see
/// <see cref="DeleteRecipeAsync"/>) are why this seam exists. No domain rules, mapping, caching, or
/// validation — those stay in business and the facade.
/// </summary>
public interface IRecipeDataLayer
{
    /// <summary>
    /// Summary rows matching <paramref name="filter"/> — its criteria combine with AND, and a
    /// <see cref="RecipeFilter.None"/> filter returns every recipe. Expects an already-normalized
    /// filter (see <c>RecipeFilterViewModel.ToFilter</c>).
    /// </summary>
    Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(RecipeFilter filter, CancellationToken ct);

    /// <summary>
    /// One recipe with its ingredients, steps (ordered by <see cref="Step.Order"/>), categories, and
    /// tags eagerly loaded; <c>null</c> if no recipe has the given id.
    /// </summary>
    Task<Recipe?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>True if a recipe with the given name already exists (case-insensitive).</summary>
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct);

    /// <summary>
    /// True if a recipe <em>other than</em> <paramref name="excludingId"/> already uses the given name
    /// (case-insensitive) — the uniqueness check for an update, which must not flag the row being edited.
    /// </summary>
    Task<bool> ExistsWithNameExceptAsync(string name, int excludingId, CancellationToken ct);

    /// <summary>Persists a new recipe (with its owned ingredients and steps) and returns it with its id.</summary>
    Task<Recipe> AddAsync(Recipe recipe, CancellationToken ct);

    /// <summary>
    /// Replaces the editable content (header, ingredients, ordered steps) of the recipe with the given
    /// id from <paramref name="incoming"/>, and returns the persisted entity. Taxonomy is left
    /// untouched. Returns <c>null</c> when no recipe has that id.
    /// </summary>
    Task<Recipe?> UpdateAsync(int id, Recipe incoming, CancellationToken ct);

    /// <summary>
    /// Deletes the recipe with the given id along with its owned ingredients and steps, reaps any
    /// category or tag the removal left without recipes, and discards its image. Returns <c>false</c>
    /// when no recipe has that id, in which case nothing is reaped.
    /// </summary>
    Task<bool> DeleteRecipeAsync(int id, CancellationToken ct);

    /// <summary>
    /// Opens the given recipe's image for reading, or <c>null</c> when the recipe doesn't exist, has
    /// no image, or names a blob that isn't there. Reading the bytes takes both stores — the row says
    /// which blob, the container has it — which is what makes this one call from business rather than two.
    /// </summary>
    Task<RecipeImage?> OpenImageAsync(int id, CancellationToken ct);

    /// <summary>
    /// Stores <paramref name="content"/> as the given recipe's image under <paramref name="blobName"/>,
    /// replacing any existing one and discarding the blob it supersedes. Returns <c>false</c> when no
    /// recipe has that id, leaving nothing behind.
    /// </summary>
    Task<bool> SetImageAsync(int id, string blobName, Stream content, string contentType, CancellationToken ct);

    /// <summary>
    /// Removes the given recipe's image and deletes its blob. Returns <c>false</c> when no recipe has
    /// that id or it had no image — either way there is nothing to remove.
    /// </summary>
    Task<bool> RemoveImageAsync(int id, CancellationToken ct);
}
