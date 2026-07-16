using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Persistence for the Recipes feature. Data access only — no validation, caching, or domain rules.
/// Detail reads and writes return the domain <see cref="Recipe"/> entity for the business layer to
/// map; the list read is projected straight to <see cref="RecipeSummaryServiceModel"/> in SQL so a
/// list view never materializes ingredient/step rows.
/// </summary>
public interface IRecipeRepository
{
    /// <summary>
    /// Runs <paramref name="operation"/> as one atomic unit, for a caller composing several repository
    /// calls that must land together or not at all. Every call made on this repository inside
    /// <paramref name="operation"/> enlists in the same transaction; it commits when the operation
    /// returns, and rolls back if it throws.
    /// </summary>
    /// <remarks>
    /// A callback rather than a "begin" that hands back a transaction, and that shape is forced by the
    /// database rather than chosen. Aspire's Npgsql integration enables retry-on-failure, and its
    /// execution strategy refuses to run inside a transaction the caller opened itself — it can't
    /// retry what it doesn't own the boundaries of. Passing the whole unit in lets the strategy
    /// re-run all of it on a transient fault, which is the only way the two can coexist.
    /// <para>Beware what that implies: <paramref name="operation"/> may run more than once, so it must
    /// be safe to repeat. Everything the operation does through this repository is inside the
    /// transaction and is rolled back before a retry — but anything it does to another store is not.
    /// Keep those outside.</para>
    /// </remarks>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct);

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
    /// Deletes the recipe with the given id along with its owned ingredients and steps. Returns
    /// <c>false</c> when no recipe has that id.
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// The blob name of the given recipe's image, or <c>null</c> if it has none — or if no recipe has
    /// that id. The two cases are deliberately not distinguished: every caller treats "there are no
    /// bytes to serve" and "there is no recipe" the same way.
    /// </summary>
    Task<string?> GetImageBlobNameAsync(int id, CancellationToken ct);

    /// <summary>
    /// Points the given recipe at <paramref name="blobName"/>, or at no image when it's <c>null</c>,
    /// and reports what the row named before (see <see cref="ImageAssignment"/>). Touches only that
    /// column, so it can't disturb a concurrent edit of the recipe's content.
    /// </summary>
    Task<ImageAssignment> SetImageBlobNameAsync(int id, string? blobName, CancellationToken ct);

    /// <summary>
    /// Deletes every category no recipe references any more, and returns how many were removed.
    /// Idempotent — a no-op when nothing is orphaned.
    /// </summary>
    Task<int> DeleteOrphanedCategoriesAsync(CancellationToken ct);

    /// <summary>
    /// Deletes every tag no recipe references any more, and returns how many were removed.
    /// Idempotent — a no-op when nothing is orphaned.
    /// </summary>
    Task<int> DeleteOrphanedTagsAsync(CancellationToken ct);
}
