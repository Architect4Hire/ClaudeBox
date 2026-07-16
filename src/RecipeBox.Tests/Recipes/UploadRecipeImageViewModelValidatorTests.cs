using System.Text;
using FluentValidation.TestHelper;
using RecipeBox.ApiService.Managers.Models.ViewModels;
using RecipeBox.ApiService.Managers.Validators;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Edge validation for an image upload: non-empty, within the size ceiling, and actually an image.
/// </summary>
public class UploadRecipeImageViewModelValidatorTests
{
    private readonly UploadRecipeImageViewModelValidator _sut = new();

    private static readonly byte[] JpegBytes =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    private static UploadRecipeImageViewModel Upload(byte[] bytes, long? length = null) =>
        new(new MemoryStream(bytes), length ?? bytes.Length);

    [Fact]
    public async Task Accepts_a_real_image_within_the_size_limit()
    {
        var result = await _sut.TestValidateAsync(Upload(JpegBytes));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task Rejects_an_empty_file()
    {
        var result = await _sut.TestValidateAsync(Upload([], length: 0));

        result.ShouldHaveValidationErrorFor(u => u.Length);
    }

    [Fact]
    public async Task Rejects_a_file_over_the_size_limit()
    {
        // Length is what's judged, not the stream — so this doesn't have to allocate 5MB to test 5MB.
        var result = await _sut.TestValidateAsync(
            Upload(JpegBytes, length: UploadRecipeImageViewModelValidator.MaxBytes + 1));

        result.ShouldHaveValidationErrorFor(u => u.Length);
    }

    [Fact]
    public async Task Accepts_a_file_exactly_at_the_size_limit()
    {
        var result = await _sut.TestValidateAsync(
            Upload(JpegBytes, length: UploadRecipeImageViewModelValidator.MaxBytes));

        result.ShouldNotHaveValidationErrorFor(u => u.Length);
    }

    [Fact]
    public async Task Rejects_a_file_whose_bytes_are_not_an_image()
    {
        var result = await _sut.TestValidateAsync(Upload(Encoding.UTF8.GetBytes("<script>alert(1)</script>")));

        // The rule is on the content, because that's the only thing the request can't lie about.
        result.ShouldHaveValidationErrorFor(u => u.Content);
    }

    [Fact]
    public async Task Leaves_the_stream_readable_from_the_start_for_the_upload_that_follows()
    {
        var viewModel = Upload(JpegBytes);

        await _sut.ValidateAsync(viewModel);

        // Validation runs before the blob write, on the same stream. If it consumed the header, the
        // upload would store a headless file and the recipe would show a broken image — with every
        // test still green. So the contract is: validating must leave the bytes whole.
        Assert.Equal(0, viewModel.Content.Position);
        var readBack = new byte[JpegBytes.Length];
        await viewModel.Content.ReadExactlyAsync(readBack);
        Assert.Equal(JpegBytes, readBack);
    }
}
