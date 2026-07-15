using FluentValidation;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Managers.Validators;

/// <summary>
/// Edge validation for <see cref="CreateRecipeViewModel"/>, run by the facade. Enforces shape/format
/// rules only; the data-dependent unique-name rule lives in the business layer.
/// </summary>
public class CreateRecipeViewModelValidator : AbstractValidator<CreateRecipeViewModel>
{
    public CreateRecipeViewModelValidator()
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

    private static bool HaveUniqueOrders(IReadOnlyList<CreateStepViewModel> steps) =>
        steps is null || steps.Select(s => s.Order).Distinct().Count() == steps.Count;
}
