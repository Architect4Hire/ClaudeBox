namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>
/// Normalized, internal criteria for a recipe list query — the domain-side counterpart of
/// <c>RecipeFilterViewModel</c>. The business layer translates the validated view model into this
/// (see <c>RecipeFilterMappings</c>), so no view model travels below the facade, exactly as with the
/// write paths that translate into a <see cref="Recipe"/>.
/// <para>Both properties are already normalized by the translation: trimmed, and null when the caller
/// supplied nothing meaningful. The data layer can therefore null-check and use them directly rather
/// than re-trimming.</para>
/// </summary>
/// <param name="Category">
/// Category name to restrict to, or null for "any". Matched <em>exactly</em> and case-sensitively,
/// mirroring the case-sensitive unique index on category name — unlike <paramref name="Ingredient"/>.
/// That asymmetry is safe while the only categories a client can send are ones it read back from this
/// API; free-typed category input would need the same LOWER(...) treatment as the ingredient.
/// </param>
/// <param name="Ingredient">
/// Ingredient name fragment to restrict to, or null for "any". Matched as a case-insensitive
/// <em>substring</em>, so "flo" finds "Plain Flour".
/// </param>
public record RecipeFilter(string? Category, string? Ingredient)
{
    /// <summary>The unfiltered query — every recipe.</summary>
    public static readonly RecipeFilter None = new(null, null);
}
