namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>
/// One ordered instruction within a recipe. <see cref="Order"/> is 1-based and unique per recipe.
/// </summary>
public class Step
{
    public int Id { get; set; }

    /// <summary>1-based position of this step within its recipe.</summary>
    public int Order { get; set; }

    public required string Instruction { get; set; }

    public int RecipeId { get; set; }

    public Recipe? Recipe { get; set; }
}
