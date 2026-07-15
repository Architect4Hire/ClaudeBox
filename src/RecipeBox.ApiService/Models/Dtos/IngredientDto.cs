namespace RecipeBox.ApiService.Features.Recipes.Dtos;

/// <summary>An ingredient line as returned across the API boundary.</summary>
public record IngredientDto(string Name, decimal Quantity, string? Unit);
