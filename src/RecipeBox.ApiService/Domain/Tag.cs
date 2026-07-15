namespace RecipeBox.ApiService.Domain;

/// <summary>
/// A freeform label (e.g. "quick", "gluten-free"). Many-to-many with recipes.
/// </summary>
public class Tag
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public ICollection<Recipe> Recipes { get; set; } = new List<Recipe>();
}
