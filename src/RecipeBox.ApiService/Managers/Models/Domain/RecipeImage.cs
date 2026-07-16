namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>
/// A recipe's image as it comes back out of the blob store: the bytes, plus what's needed to serve
/// them correctly.
/// <para><see cref="ContentType"/> is the blob's own, recorded at upload from the bytes' magic number
/// rather than from whatever the client claimed — see <c>RecipeImageSniffer</c>. Trusting the client
/// here would let an attacker store PNG bytes under <c>text/html</c> and have us serve their markup
/// back same-origin.</para>
/// <para><see cref="ETag"/> is the blob's version. It's what makes the image cacheable without a
/// versioned URL: the response carries it, the browser sends it back as <c>If-None-Match</c>, and an
/// unchanged image answers 304 instead of resending the bytes.</para>
/// <para>Owns an open stream from the blob store, so callers must dispose it. Handing it to
/// <c>ControllerBase.File(...)</c> does that.</para>
/// </summary>
public record RecipeImage(Stream Content, string ContentType, string ETag);
