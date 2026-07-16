namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>
/// A recipe: its descriptive header plus its ingredients, ordered steps, and taxonomy.
/// </summary>
public class Recipe
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public int Servings { get; set; }

    /// <summary>
    /// Name of this recipe's image in the "uploads" blob container, or null when it has none.
    /// Only the key is stored, never a URL: the address of the blob store is Aspire's to inject, and a
    /// persisted URL would bake a host into the database. The content type isn't stored either — the
    /// blob carries its own, so there is nothing here to drift out of sync with it.
    /// </summary>
    public string? ImageBlobName { get; set; }

    public ICollection<Ingredient> Ingredients { get; set; } = new List<Ingredient>();

    public ICollection<Step> Steps { get; set; } = new List<Step>();

    public ICollection<Category> Categories { get; set; } = new List<Category>();

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
