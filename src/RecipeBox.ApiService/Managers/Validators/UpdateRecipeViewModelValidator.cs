using FluentValidation;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Managers.Validators;

/// <summary>
/// Edge validation for <see cref="UpdateRecipeViewModel"/>, run by the facade. Mirrors
/// <see cref="CreateRecipeViewModelValidator"/>: shape/format rules only; the data-dependent
/// unique-name rule (excluding the recipe being edited) lives in the business layer.
/// </summary>
public class UpdateRecipeViewModelValidator : AbstractValidator<UpdateRecipeViewModel>
{
    public UpdateRecipeViewModelValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(2000);

        RuleFor(r => r.Servings)
            .GreaterThanOrEqualTo(1);

        RuleFor(r => r.Ingredients)
            .NotEmpty().WithMessage("A recipe must have at least one ingredient.");

        RuleForEach(r => r.Ingredients).ChildRules(ingredient =>
        {
            ingredient.RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
            ingredient.RuleFor(i => i.Quantity).GreaterThanOrEqualTo(0);
            ingredient.RuleFor(i => i.Unit).MaximumLength(50);
        });

        RuleFor(r => r.Steps)
            .NotEmpty().WithMessage("A recipe must have at least one step.");

        RuleForEach(r => r.Steps).ChildRules(step =>
        {
            step.RuleFor(s => s.Order).GreaterThanOrEqualTo(1);
            step.RuleFor(s => s.Instruction).NotEmpty().MaximumLength(2000);
        });

        RuleFor(r => r.Steps)
            .Must(HaveUniqueOrders).WithMessage("Step orders must be unique within a recipe.");
    }

    private static bool HaveUniqueOrders(IReadOnlyList<UpdateStepViewModel> steps) =>
        steps is null || steps.Select(s => s.Order).Distinct().Count() == steps.Count;
}
