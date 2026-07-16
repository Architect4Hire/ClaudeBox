namespace RecipeBox.ApiService.Managers.Models.ViewModels;

/// <summary>
/// Inbound filter for the recipe list, bound from the query string. Both filters are optional and
/// combine with AND: a category and an ingredient together mean "recipes in this category that also
/// contain this ingredient".
/// <para>Unlike the other view models this one is property-style rather than positional, because it
/// binds from the <em>query string</em>: MVC's query binder needs a parameterless constructor, so a
/// positional record would bind every property to null instead. (The body-bound view models get away
/// with positional records because System.Text.Json can use a parameterized constructor.)</para>
/// </summary>
public record RecipeFilterViewModel
{
    /// <summary>Restrict to recipes in this category, matched by exact name. Null/blank means "any".</summary>
    public string? Category { get; init; }

    /// <summary>
    /// Restrict to recipes with an ingredient whose name contains this text (case-insensitive
    /// substring). Null/blank means "any".
    /// </summary>
    public string? Ingredient { get; init; }

    /// <summary>
    /// True when at least one filter is active. The facade uses this to decide cacheability — only the
    /// wholly unfiltered list is cached.
    /// </summary>
    public bool HasFilter =>
        !string.IsNullOrWhiteSpace(Category) || !string.IsNullOrWhiteSpace(Ingredient);
}
