namespace RecipeBox.ApiService.Domain;

/// <summary>
/// A recipe: its descriptive header plus its ingredients, ordered steps, and taxonomy.
/// </summary>
public class Recipe
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int Servings { get; set; }

    public ICollection<Ingredient> Ingredients { get; set; } = new List<Ingredient>();

    public ICollection<Step> Steps { get; set; } = new List<Step>();

    public ICollection<Category> Categories { get; set; } = new List<Category>();

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
