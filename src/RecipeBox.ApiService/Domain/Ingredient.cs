namespace RecipeBox.ApiService.Domain;

/// <summary>
/// A single ingredient line belonging to one recipe (e.g. 1.5 "cups" of flour).
/// </summary>
public class Ingredient
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Unit of measure (e.g. "cups", "g"). Optional for "to taste" style items.</summary>
    public string? Unit { get; set; }

    public int RecipeId { get; set; }

    public Recipe? Recipe { get; set; }
}
