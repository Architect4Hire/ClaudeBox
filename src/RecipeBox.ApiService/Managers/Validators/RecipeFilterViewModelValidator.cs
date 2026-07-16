using FluentValidation;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Managers.Validators;

/// <summary>
/// Edge validation for <see cref="RecipeFilterViewModel"/>, run by the facade on every list request.
/// Both filters are optional, so there is no <c>NotEmpty</c> here — the only rules are length caps,
/// which bound the ingredient substring scan.
/// <para>The caps mirror the columns each filter is matched against (see
/// <see cref="CreateRecipeViewModelValidator"/>): a category name is at most 100 characters and an
/// ingredient name at most 200, so a longer term could never match a stored row anyway.</para>
/// </summary>
public class RecipeFilterViewModelValidator : AbstractValidator<RecipeFilterViewModel>
{
    public RecipeFilterViewModelValidator()
    {
        RuleFor(f => f.Category)
            .MaximumLength(100);

        RuleFor(f => f.Ingredient)
            .MaximumLength(200);
    }
}
