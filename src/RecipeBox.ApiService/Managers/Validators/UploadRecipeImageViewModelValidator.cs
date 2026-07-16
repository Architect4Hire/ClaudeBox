using FluentValidation;
using RecipeBox.ApiService.Managers.Infrastructure;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Managers.Validators;

/// <summary>
/// Edge validation for <see cref="UploadRecipeImageViewModel"/>, run by the facade. Enforces that the
/// upload is a non-empty, reasonably sized file whose bytes really are an image we serve; whether the
/// recipe exists is the business layer's question, not this one's.
/// </summary>
public class UploadRecipeImageViewModelValidator : AbstractValidator<UploadRecipeImageViewModel>
{
    /// <summary>
    /// Generous for a photo, mean enough that the container isn't a dumping ground. Mirrored by the
    /// controller's RequestSizeLimit, which rejects an oversized body before it's ever buffered — this
    /// rule only sees uploads that already fit, and exists so the limit is stated where the other
    /// rules are.
    /// </summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    public UploadRecipeImageViewModelValidator()
    {
        RuleFor(i => i.Length)
            .GreaterThan(0).WithMessage("The image file is empty.")
            .LessThanOrEqualTo(MaxBytes)
            .WithMessage($"The image must be {MaxBytes / 1024 / 1024} MB or smaller.");

        RuleFor(i => i.Content)
            .NotNull()
            .MustAsync(BeASupportedImage)
            .WithMessage("The file must be a JPEG, PNG, or WebP image.");
    }

    // Reads the magic number rather than any declared type — see RecipeImageFormat for why the
    // request's own Content-Type is not consulted. DetectAsync restores the stream position, so the
    // upload that follows still gets whole bytes; that matters because FluentValidation makes no
    // promise about how many times a rule runs.
    private static async Task<bool> BeASupportedImage(Stream content, CancellationToken ct) =>
        await RecipeImageFormat.DetectAsync(content, ct) is not null;
}
