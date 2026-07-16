namespace RecipeBox.ApiService.Data;

/// <summary>
/// What happened when a recipe's image blob name was written.
/// <para>Two facts, because the caller needs both and a single nullable string can't carry them:
/// whether the recipe existed at all (no row means a 404, not a failure), and which blob it named
/// before (the one now superseded, which the data layer deletes once the row is safely updated).
/// Without <see cref="PreviousBlobName"/>, replacing an image would leak the old blob every time.
/// </para>
/// </summary>
public record ImageAssignment(bool RecipeFound, string? PreviousBlobName)
{
    public static readonly ImageAssignment RecipeNotFound = new(false, null);
}
