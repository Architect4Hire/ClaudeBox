namespace RecipeBox.ApiService.Features.Recipes.Dtos;

/// <summary>One instruction step as returned across the API boundary. <see cref="Order"/> is 1-based.</summary>
public record StepDto(int Order, string Instruction);
