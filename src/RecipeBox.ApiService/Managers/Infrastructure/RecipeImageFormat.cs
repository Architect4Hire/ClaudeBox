namespace RecipeBox.ApiService.Managers.Infrastructure;

/// <summary>
/// Decides what an uploaded recipe image actually is, by reading its magic number rather than
/// believing the request.
/// <para>A browser's <c>Content-Type</c> on a multipart part is attacker-controlled, and we echo the
/// stored type straight back on GET. Take the client's word for it and someone uploads PNG bytes
/// declared <c>text/html</c>, we serve their markup from our own origin, and the upload form has
/// become stored XSS. So the declared type is never read — <see cref="UploadRecipeImageViewModel"/>
/// doesn't even carry it — and this is the only thing that names the format.</para>
/// <para>Shared by the validator (which rejects anything unrecognised) and the business layer (which
/// stores what this returns). Both sniff independently: it's a 12-byte read, and the alternative —
/// threading the validator's finding through to the write — couples the two for no gain.</para>
/// </summary>
public static class RecipeImageFormat
{
    /// <summary>Enough for the longest signature checked here (WebP needs 12 bytes).</summary>
    private const int HeaderLength = 12;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";

    /// <summary>
    /// Returns the content type the bytes really are, or null if they aren't a format we accept —
    /// which the validator turns into a 400.
    /// <para>Restores the stream position before returning, so the caller can hand the same stream
    /// to the blob store afterwards. Skipping that would upload the image minus its header: a
    /// silently corrupt blob, written by a validation step that "passed".</para>
    /// </summary>
    public static async Task<string?> DetectAsync(Stream content, CancellationToken ct)
    {
        if (!content.CanSeek)
        {
            // Every caller passes a buffered form-file stream, which seeks. A non-seekable one can't be
            // sniffed and then re-read, and guessing isn't an option here, so treat it as unrecognised.
            return null;
        }

        var origin = content.Position;
        var header = new byte[HeaderLength];
        var read = await content.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, ct);
        content.Position = origin;

        return Detect(header.AsSpan(0, read));
    }

    private static string? Detect(ReadOnlySpan<byte> header) => header switch
    {
        // FF D8 FF — SOI marker followed by the first segment's marker.
        [0xFF, 0xD8, 0xFF, ..] => Jpeg,

        // 89 "PNG" CR LF SUB LF — the full 8-byte signature, including the bytes that exist to catch
        // corruption from newline-translating transfers.
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => Png,

        // "RIFF" ---- "WEBP": a RIFF container whose 4-byte form type at offset 8 is WEBP. Bytes 4-7 are
        // the file size, so they're skipped.
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50] => WebP,

        _ => null,
    };
}
