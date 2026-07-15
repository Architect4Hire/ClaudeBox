namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>
/// A structured taxonomy value (e.g. "Main Course", "Dessert"). Many-to-many with recipes.
/// </summary>
public class Category
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public ICollection<Recipe> Recipes { get; set; } = new List<Recipe>();
}
