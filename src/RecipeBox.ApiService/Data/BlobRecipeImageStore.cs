using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using RecipeBox.ApiService.Managers.Models.Domain;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// <see cref="IRecipeImageStore"/> over the Aspire-provided <see cref="BlobContainerClient"/>, keyed
/// to the "recipe-images" AppHost resource (see Program.cs). Blob calls only — no business rules,
/// caching, or validation, mirroring how <see cref="RecipeRepository"/> holds only EF queries.
/// <para>The container itself is created by the AppHost: <c>RunAsEmulator</c> provisions it on
/// startup, so there is deliberately no CreateIfNotExists call here.</para>
/// </summary>
public class BlobRecipeImageStore(BlobContainerClient container) : IRecipeImageStore
{
    private readonly BlobContainerClient _container = container;

    /// <summary>
    /// Fallback for a blob stored without a content type. Shouldn't happen — <see cref="UploadAsync"/>
    /// always records one — but serving unknown bytes as a generic download beats letting the browser
    /// guess, which is the sniffing behaviour the endpoint's nosniff header exists to prevent.
    /// </summary>
    private const string FallbackContentType = "application/octet-stream";

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct)
    {
        // Overwrites by default: replacing an image writes a fresh blob name anyway (see
        // RecipeBusiness), so an overwrite here only happens when the seeder re-runs over its own blobs.
        await _container.GetBlobClient(blobName).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);
    }

    public async Task<RecipeImage?> OpenAsync(string blobName, CancellationToken ct)
    {
        try
        {
            // Streaming, not buffered: the response returns once the headers are in, and the bytes flow
            // as the caller reads them. That keeps a multi-megabyte image off the API's heap, and lets a
            // 304 abandon the body without ever transferring it.
            var download = await _container.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: ct);

            return new RecipeImage(
                download.Value.Content,
                download.Value.Details.ContentType ?? FallbackContentType,
                // "H" renders the quoted HTTP form (\"0x8D...\"); plain ToString() doesn't, and
                // EntityTagHeaderValue rejects an unquoted value.
                download.Value.Details.ETag.ToString("H"));
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            // Expected, not exceptional: a row can name a blob that a failed upload never wrote, or that
            // a compensating delete removed. Translate to null rather than leaking a storage exception
            // past this seam — the caller decides that means 404.
            return null;
        }
    }

    public async Task DeleteAsync(string blobName, CancellationToken ct)
    {
        // DeleteIfExists, not Delete: every caller is discarding a blob it believes is superseded or
        // orphaned, so "already gone" is success, not a fault to propagate.
        await _container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
    }
}
