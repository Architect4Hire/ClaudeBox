namespace RecipeBox.ApiService.Managers.Models.ServiceModels;

/// <summary>
/// A recipe's image on its way out to the client: the bytes, the content type to serve them as, and
/// the ETag the browser revalidates against.
/// <para>Unlike the other service models this isn't JSON — the controller streams it as the response
/// body. It's still a service model because it's what the facade hands the controller, and the
/// controller must no more see a blob-store type than it sees an EF entity.</para>
/// <para>Carries an open stream; the controller disposes it by passing it to <c>File(...)</c>. It is
/// never cached, for the same reason — a stream can only be read once.</para>
/// </summary>
public record RecipeImageServiceModel(Stream Content, string ContentType, string ETag);
