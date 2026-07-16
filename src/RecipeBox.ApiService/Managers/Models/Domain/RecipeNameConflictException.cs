using RecipeBox.ApiService.Managers.Infrastructure;

namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>
/// Raised by the business layer when a recipe cannot be created because its name is already taken.
/// Surfaced to the client as HTTP 409 by the global exception handler, by virtue of deriving from
/// <see cref="DomainConflictException"/>.
/// </summary>
public class RecipeNameConflictException(string name)
    : DomainConflictException($"A recipe named '{name}' already exists.")
{
    public string RecipeName { get; } = name;

    public override string Title => "Recipe name already exists.";
}
