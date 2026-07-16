using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Models.Domain;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// An in-memory <see cref="IRecipeImageStore"/>: the whole reason that seam exists. Lets every layer
/// above the store be driven without Azurite running, the same way the SQLite context stands in for
/// Postgres.
/// <para>Uploads are copied to a byte array rather than held as the caller's stream, so a test can
/// assert on what was actually written — including whether the full bytes arrived, which is how a
/// validator that consumed the stream's header without rewinding gets caught.</para>
/// </summary>
public sealed class FakeRecipeImageStore : IRecipeImageStore
{
    private readonly Dictionary<string, StoredBlob> _blobs = [];
    private int _etagSequence;

    /// <summary>Blob names currently held — an empty set is how a test asserts nothing was orphaned.</summary>
    public IReadOnlyCollection<string> BlobNames => _blobs.Keys;

    /// <summary>Every name ever uploaded, including ones since deleted, in order.</summary>
    public List<string> Uploaded { get; } = [];

    /// <summary>Every name ever passed to <see cref="DeleteAsync"/>, in order.</summary>
    public List<string> Deleted { get; } = [];

    /// <summary>Set to make the next upload throw, standing in for the blob store being unreachable.</summary>
    public Exception? UploadFailure { get; set; }

    public byte[] BytesOf(string blobName) => _blobs[blobName].Content;

    public string ContentTypeOf(string blobName) => _blobs[blobName].ContentType;

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct)
    {
        if (UploadFailure is not null)
        {
            throw UploadFailure;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        // A fresh ETag per write, so a replaced image never reuses the old one — that's what the
        // conditional-GET behaviour depends on. Quoted because EntityTagHeaderValue rejects bare values.
        _blobs[blobName] = new StoredBlob(buffer.ToArray(), contentType, $"\"fake-etag-{++_etagSequence}\"");
        Uploaded.Add(blobName);
    }

    public Task<RecipeImage?> OpenAsync(string blobName, CancellationToken ct)
    {
        if (!_blobs.TryGetValue(blobName, out var blob))
        {
            return Task.FromResult<RecipeImage?>(null);
        }

        return Task.FromResult<RecipeImage?>(
            new RecipeImage(new MemoryStream(blob.Content), blob.ContentType, blob.ETag));
    }

    public Task DeleteAsync(string blobName, CancellationToken ct)
    {
        Deleted.Add(blobName);
        _blobs.Remove(blobName);
        // Mirrors DeleteIfExists: removing an absent blob is success, not a fault.
        return Task.CompletedTask;
    }

    private record StoredBlob(byte[] Content, string ContentType, string ETag);
}
