using System.Text;
using RecipeBox.ApiService.Managers.Infrastructure;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// The magic-number sniffing that decides what an upload really is. This is the security boundary for
/// image uploads — everything downstream trusts its answer — so the tests here are about what it
/// refuses and what it leaves behind, not just what it recognises.
/// </summary>
public class RecipeImageFormatTests
{
    // Real signatures, padded out past the 12 bytes the sniffer reads.
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x02, 0x03];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48];
    private static readonly byte[] WebPBytes = [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50];

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task DetectAsync_recognises_the_formats_we_serve(string format)
    {
        var (bytes, expected) = format switch
        {
            "jpeg" => (JpegBytes, RecipeImageFormat.Jpeg),
            "png" => (PngBytes, RecipeImageFormat.Png),
            _ => (WebPBytes, RecipeImageFormat.WebP),
        };
        using var stream = new MemoryStream(bytes);

        Assert.Equal(expected, await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task DetectAsync_rejects_a_file_that_is_not_an_image()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<html><script>alert(1)</script>"));

        // The attack this exists to stop: markup renamed .jpg and posted as image/jpeg. Nothing about
        // the request says it isn't an image — only the bytes do.
        Assert.Null(await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task DetectAsync_rejects_a_file_that_only_starts_to_look_like_an_image()
    {
        // First two bytes of a JPEG SOI, third byte wrong — a partial match must not pass.
        using var stream = new MemoryStream([0xFF, 0xD8, 0x00, 0x00, 0x00, 0x00]);

        Assert.Null(await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task DetectAsync_rejects_a_file_shorter_than_a_signature()
    {
        using var stream = new MemoryStream([0xFF, 0xD8]);

        // Must not read past the end or throw — a two-byte upload is a 400, not a 500.
        Assert.Null(await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task DetectAsync_rejects_an_empty_file()
    {
        using var stream = new MemoryStream([]);

        Assert.Null(await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task DetectAsync_leaves_the_stream_where_it_found_it()
    {
        using var stream = new MemoryStream(JpegBytes);

        await RecipeImageFormat.DetectAsync(stream, CancellationToken.None);

        // The bug this guards: sniffing advances the position, and the upload that follows then writes
        // everything *after* the header — a silently truncated blob, produced by a check that passed.
        Assert.Equal(0, stream.Position);
        var readBack = new byte[JpegBytes.Length];
        await stream.ReadExactlyAsync(readBack);
        Assert.Equal(JpegBytes, readBack);
    }

    [Fact]
    public async Task DetectAsync_can_be_called_twice_and_still_leave_whole_bytes_behind()
    {
        // FluentValidation makes no promise a rule runs once, and the business layer sniffs again to
        // decide what to store. Two sniffs in a row must be as harmless as one.
        using var stream = new MemoryStream(PngBytes);

        Assert.Equal(RecipeImageFormat.Png, await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
        Assert.Equal(RecipeImageFormat.Png, await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task DetectAsync_restores_a_non_zero_position_rather_than_rewinding_to_the_start()
    {
        var padded = new byte[4].Concat(JpegBytes).ToArray();
        using var stream = new MemoryStream(padded) { Position = 4 };

        Assert.Equal(RecipeImageFormat.Jpeg, await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
        Assert.Equal(4, stream.Position);
    }

    [Fact]
    public async Task DetectAsync_gives_up_on_a_stream_it_cannot_rewind()
    {
        // It can't sniff and then hand back whole bytes, and guessing is exactly the failure mode this
        // class exists to prevent — so it refuses rather than trusting anything else.
        using var stream = new NonSeekableStream(JpegBytes);

        Assert.Null(await RecipeImageFormat.DetectAsync(stream, CancellationToken.None));
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
