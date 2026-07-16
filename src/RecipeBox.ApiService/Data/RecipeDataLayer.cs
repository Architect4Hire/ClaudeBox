using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Composes <see cref="IRecipeRepository"/> and <see cref="IRecipeImageStore"/> operations into the
/// whole data operations the business layer asks for. Depends only on their interfaces — it holds no
/// <c>DbContext</c> or blob client of its own, so every query still belongs to the repository and
/// every blob call to the store.
/// <para>This is the one layer that sees both stores, and so the only one that has to reconcile them.
/// A recipe's image is two facts in two places — a column naming a blob, and the blob — and nothing
/// makes them atomic: no transaction spans Postgres and blob storage. So the ordering below is the
/// design, not an implementation detail, and it's chosen so the failure that survives is always an
/// orphaned blob (invisible, sweepable) rather than a row naming bytes that aren't there (a broken
/// image on the page).</para>
/// </summary>
public class RecipeDataLayer(IRecipeRepository repository, IRecipeImageStore images) : IRecipeDataLayer
{
    private readonly IRecipeRepository _repository = repository;
    private readonly IRecipeImageStore _images = images;

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
        // Which blob was this recipe's. Captured out of the transaction rather than returned from it,
        // because the delete below has to answer a different question (was there a row at all).
        string? blobName = null;

        var deleted = await _repository.ExecuteInTransactionAsync(
            async token =>
            {
                // Read inside the transaction, and immediately before the delete. Once the row is gone
                // nothing records which blob was this recipe's, so it must be read first — but reading
                // it back out at the top of the method would open a window where a concurrent image
                // upload could swap the blob after the read and have its new one orphaned here. In
                // here the read and the delete are the same transaction, which narrows that to the gap
                // between them.
                //
                // Narrows, not closes: a replace that commits in that gap still leaves its new blob
                // unreferenced. That's the failure this whole design steers towards on purpose (an
                // orphan blob, not a recipe pointing at nothing), and the racing upload is no worse
                // off — SetImageAsync sees RecipeFound=false against the row we just deleted and takes
                // its own blob back.
                //
                // Re-read on a retry too, which is why it's in here: the value must come from the
                // attempt that actually commits.
                blobName = await _repository.GetImageBlobNameAsync(id, token);

                if (!await _repository.DeleteAsync(id, token))
                {
                    // Nothing was removed, so nothing can have been orphaned — neither sweep needs to
                    // run, and the transaction commits having done nothing.
                    return false;
                }

                await _repository.DeleteOrphanedCategoriesAsync(token);
                await _repository.DeleteOrphanedTagsAsync(token);
                return true;
            },
            ct);

        // Deliberately outside the transaction, and after it. Two reasons, and both bite:
        // a rollback can't undo a blob delete, so deleting inside would leave a restored recipe
        // pointing at bytes that are gone — and the operation above may be re-run on a transient
        // fault, which would delete the same blob twice. Out here the recipe is definitely gone, and
        // the worst case is a blob nobody references.
        if (deleted && blobName is not null)
        {
            await _images.DeleteAsync(blobName, ct);
        }

        return deleted;
    }

    public async Task<RecipeImage?> OpenImageAsync(int id, CancellationToken ct)
    {
        var blobName = await _repository.GetImageBlobNameAsync(id, ct);

        // No row, or a row with no image: either way there are no bytes, and the caller 404s.
        return blobName is null ? null : await _images.OpenAsync(blobName, ct);
    }

    public async Task<bool> SetImageAsync(
        int id, string blobName, Stream content, string contentType, CancellationToken ct)
    {
        // Blob first, row second — the reverse of the intuitive order, and deliberately so.
        //
        // Writing the row first would mean opening a transaction and holding a lock on that row across
        // a multi-megabyte upload to another service; and it wouldn't even buy atomicity, since a
        // commit that failed after the upload would orphan the blob anyway. Uploading first costs
        // nothing to hold: the blob name is freshly minted per upload (see RecipeBusiness), so it
        // collides with nothing and is invisible until the row below points at it. The row therefore
        // only ever names bytes that are already fully written.
        await _images.UploadAsync(blobName, content, contentType, ct);

        var assignment = await _repository.SetImageBlobNameAsync(id, blobName, ct);
        if (!assignment.RecipeFound)
        {
            // The recipe was deleted between the request arriving and this write. Take back the blob we
            // just uploaded rather than leaving it for a recipe that no longer exists.
            await _images.DeleteAsync(blobName, ct);
            return false;
        }

        await DeleteSupersededAsync(assignment, blobName, ct);
        return true;
    }

    public async Task<bool> RemoveImageAsync(int id, CancellationToken ct)
    {
        var assignment = await _repository.SetImageBlobNameAsync(id, null, ct);
        if (!assignment.RecipeFound || assignment.PreviousBlobName is null)
        {
            // No recipe, or one that had no image: nothing was removed, and the caller 404s.
            return false;
        }

        await DeleteSupersededAsync(assignment, newBlobName: null, ct);
        return true;
    }

    // Reaps the blob the row used to name, once it definitely doesn't name it any more. Ordered after
    // the row write for the same reason as in DeleteRecipeAsync: a failure here leaves an unreferenced
    // blob, whereas deleting first and then failing to write would leave a live recipe pointing at
    // deleted bytes.
    private async Task DeleteSupersededAsync(ImageAssignment assignment, string? newBlobName, CancellationToken ct)
    {
        if (assignment.PreviousBlobName is null || assignment.PreviousBlobName == newBlobName)
        {
            return;
        }

        await _images.DeleteAsync(assignment.PreviousBlobName, ct);
    }
}
