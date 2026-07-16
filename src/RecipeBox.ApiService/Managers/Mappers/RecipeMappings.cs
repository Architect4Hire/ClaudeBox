using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Managers.Mappers;

/// <summary>
/// The Recipes feature's mapping seams, one per layer boundary:
/// <list type="bullet">
///   <item><b>ViewModel → domain</b> (business): a validated request becomes an entity to persist.</item>
///   <item><b>domain → service model</b> (business): a loaded entity becomes the wire response.</item>
/// </list>
/// Keeping these here is how each layer trades in only the types it owns — no EF entity ever reaches
/// the controller, and no view model ever reaches the database.
/// </summary>
public static class RecipeMappings
{
    // ── ViewModel → domain (business layer) ──────────────────────────────────────────────────────

    /// <summary>
    /// Translates the list request's view model into the domain criteria the data layer queries with,
    /// normalizing as it goes: each filter is trimmed, and a blank one becomes null ("any"). Doing it
    /// here means the data layer never sees a view model, and never has to re-trim.
    /// </summary>
    public static RecipeFilter ToFilter(this RecipeFilterViewModel viewModel) =>
        new(Normalize(viewModel.Category), Normalize(viewModel.Ingredient));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static Recipe ToEntity(this CreateRecipeViewModel viewModel) =>
        new()
        {
            Name = viewModel.Name,
            Description = viewModel.Description,
            Servings = viewModel.Servings,
            Ingredients = viewModel.Ingredients
                .Select(i => new Ingredient { Name = i.Name, Quantity = i.Quantity, Unit = i.Unit })
                .ToList(),
            Steps = viewModel.Steps
                .Select(s => new Step { Order = s.Order, Instruction = s.Instruction })
                .ToList(),
            // Taxonomy carries only names here; the repository resolves each to an existing row or a
            // new one on save. An omitted (null) list means "no categories/tags".
            Categories = ToCategories(viewModel.Categories),
            Tags = ToTags(viewModel.Tags),
        };

    // A detached carrier for the edited values: the repository reconciles these onto the tracked
    // entity (scalar overwrite + child-collection replace, including taxonomy), so no Id is set here.
    public static Recipe ToEntity(this UpdateRecipeViewModel viewModel) =>
        new()
        {
            Name = viewModel.Name,
            Description = viewModel.Description,
            Servings = viewModel.Servings,
            Ingredients = viewModel.Ingredients
                .Select(i => new Ingredient { Name = i.Name, Quantity = i.Quantity, Unit = i.Unit })
                .ToList(),
            Steps = viewModel.Steps
                .Select(s => new Step { Order = s.Order, Instruction = s.Instruction })
                .ToList(),
            Categories = ToCategories(viewModel.Categories),
            Tags = ToTags(viewModel.Tags),
        };

    // Names → carrier entities. Null (an omitted list) and empty both yield an empty collection.
    private static List<Category> ToCategories(IReadOnlyList<string>? names) =>
        (names ?? []).Select(name => new Category { Name = name }).ToList();

    private static List<Tag> ToTags(IReadOnlyList<string>? names) =>
        (names ?? []).Select(name => new Tag { Name = name }).ToList();

    // ── domain → service model (business layer) ──────────────────────────────────────────────────

    public static RecipeDetailServiceModel ToServiceModel(this Recipe recipe) =>
        new(
            recipe.Id,
            recipe.Name,
            recipe.Description,
            recipe.Servings,
            recipe.Ingredients.Select(i => new IngredientServiceModel(i.Name, i.Quantity, i.Unit)).ToList(),
            recipe.Steps.OrderBy(s => s.Order).Select(s => new StepServiceModel(s.Order, s.Instruction)).ToList(),
            recipe.Categories.Select(c => c.Name).ToList(),
            recipe.Tags.Select(t => t.Name).ToList(),
            // The blob key itself stays inside: the client is told an image exists, and builds the
            // address from the recipe id.
            recipe.ImageBlobName is not null);

    /// <summary>
    /// The image's blob-store form becomes its wire form. A pass-through today — but it's the seam
    /// that keeps the store's type out of the controller, exactly as the entity mapping above keeps
    /// EF's out.
    /// </summary>
    public static RecipeImageServiceModel ToServiceModel(this RecipeImage image) =>
        new(image.Content, image.ContentType, image.ETag);
}
