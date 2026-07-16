namespace RecipeBox.ApiService.Managers.Models.ViewModels;

/// <summary>
/// An image being uploaded for a recipe. The controller builds this from the posted
/// <c>IFormFile</c>, which is where <c>IFormFile</c> stops — the layers below deal in a stream and a
/// length, not in an ASP.NET binding type.
/// <para>Note what's absent: the client's declared content type and filename. Neither is carried
/// because neither is trustworthy, and a field that exists is a field someone eventually trusts. The
/// format is established from the bytes by <c>RecipeImageFormat</c>, and the blob name is minted by
/// the business layer.</para>
/// <para><see cref="Length"/> is passed rather than read off the stream so the size rule doesn't
/// depend on the stream being seekable.</para>
/// </summary>
public record UploadRecipeImageViewModel(Stream Content, long Length);
