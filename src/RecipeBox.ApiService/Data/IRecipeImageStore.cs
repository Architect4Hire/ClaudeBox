using RecipeBox.ApiService.Managers.Models.Domain;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// The recipe-images blob container, as the layers above it are allowed to see it. Sits beside
/// <see cref="IRecipeRepository"/> as the second store this feature persists to: the repository owns
/// the row, this owns the bytes.
/// <para>It exists to keep a vendor SDK off the surface. No <c>BlobContainerClient</c>,
/// <c>BlobClient</c>, or <c>RequestFailedException</c> crosses this seam, so nothing above the data
/// layer knows the bytes live in Azure-shaped storage, and tests can swap the whole store for an
/// in-memory fake without an emulator running.</para>
/// </summary>
public interface IRecipeImageStore
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="blobName"/>, overwriting any existing
    /// blob, and records <paramref name="contentType"/> on it so a later read can serve it back.
    /// </summary>
    Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct);

    /// <summary>
    /// Opens the blob for reading, or returns null if it doesn't exist. A missing blob is an expected
    /// outcome, not a fault: the row can name a blob a failed upload never wrote.
    /// </summary>
    Task<RecipeImage?> OpenAsync(string blobName, CancellationToken ct);

    /// <summary>
    /// Removes the blob. Deleting one that isn't there succeeds — callers delete blobs they believe
    /// are orphaned, and "already gone" is the outcome they wanted.
    /// </summary>
    Task DeleteAsync(string blobName, CancellationToken ct);
}
