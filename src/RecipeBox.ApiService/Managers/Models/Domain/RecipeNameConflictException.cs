namespace RecipeBox.ApiService.Features.Recipes;

/// <summary>
/// Raised by the business layer when a recipe cannot be created because its name is already taken.
/// Surfaced to the client as HTTP 409 by the global exception handler.
/// </summary>
public class RecipeNameConflictException(string name)
    : Exception($"A recipe named '{name}' already exists.")
{
    public string RecipeName { get; } = name;
}
