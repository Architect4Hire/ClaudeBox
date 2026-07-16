using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using RecipeBox.ApiService.Facade;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;
using RecipeBox.ApiService.Managers.Validators;

namespace RecipeBox.ApiService.Controllers;

/// <summary>
/// HTTP surface for recipes. Thin by design: binds the view model, calls the facade, and shapes the
/// result. It deals only in view models (in) and service models (out) — no validation, caching,
/// business logic, or data access, and never a DTO or EF entity.
/// </summary>
[ApiController]
[Route("api/recipes")]
public class RecipesController(IRecipeFacade facade) : ControllerBase
{
    private readonly IRecipeFacade _facade = facade;

    /// <summary>
    /// Room for the multipart wrapper around an uploaded image — boundary markers, part headers, the
    /// filename. Real envelopes run to a few hundred bytes; 64 KB is deliberately generous, because
    /// being tight here buys nothing (the validator enforces the actual limit) and costs a confusing
    /// rejection of a file that was within it.
    /// </summary>
    private const long MultipartEnvelopeAllowance = 64 * 1024;

    /// <summary>
    /// Lists recipe summaries, optionally narrowed by category and/or ingredient — e.g.
    /// <c>?category=Dessert&amp;ingredient=flour</c> for desserts containing flour.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecipeSummaryServiceModel>>> List(
        [FromQuery] RecipeFilterViewModel filter, CancellationToken ct)
    {
        return Ok(await _facade.ListAsync(filter, ct));
    }

    /// <summary>Gets one recipe with its ingredients and ordered steps.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RecipeDetailServiceModel>> GetById(int id, CancellationToken ct)
    {
        var recipe = await _facade.GetByIdAsync(id, ct);
        return recipe is null ? NotFound() : Ok(recipe);
    }

    /// <summary>Creates a recipe together with its ingredients and ordered steps.</summary>
    [HttpPost]
    public async Task<ActionResult<RecipeDetailServiceModel>> Create(
        [FromBody] CreateRecipeViewModel viewModel, CancellationToken ct)
    {
        var created = await _facade.CreateAsync(viewModel, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Replaces an existing recipe's header, ingredients, and ordered steps.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<RecipeDetailServiceModel>> Update(
        int id, [FromBody] UpdateRecipeViewModel viewModel, CancellationToken ct)
    {
        var updated = await _facade.UpdateAsync(id, viewModel, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Deletes a recipe with its ingredients and ordered steps.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        return await _facade.DeleteAsync(id, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Serves a recipe's image. 404 when there are no bytes to serve — no such recipe, no image, or a
    /// blob that has gone missing; the client shows its placeholder either way, so the distinction
    /// would be noise.
    /// </summary>
    [HttpGet("{id:int}/image")]
    public async Task<IActionResult> GetImage(int id, CancellationToken ct)
    {
        var image = await _facade.GetImageAsync(id, ct);
        if (image is null)
        {
            return NotFound();
        }

        // The content type is the one sniffed from the bytes at upload, never the one the uploader
        // declared. nosniff stops the browser second-guessing it and running a mislabelled file as
        // markup or script from our own origin — defence in depth behind that sniffing.
        Response.Headers.XContentTypeOptions = "nosniff";

        // "no-cache" means revalidate, not "don't store": the browser keeps the image but checks it's
        // current on each use. That's what makes a stable URL safe here — replace the image and the
        // ETag changes, so the next request gets the new bytes instead of a stale cached copy. Without
        // it we'd need a versioned URL, which would put blob names on the wire.
        Response.Headers.CacheControl = "no-cache";

        // File() handles the conditional GET: it compares this ETag against If-None-Match and returns
        // 304 without reading the stream, so an unchanged image costs headers rather than bytes. It
        // also disposes the stream — which OpenAsync hands us open — either way.
        // Note the overload: there is no File(Stream, string, EntityTagHeaderValue), so lastModified
        // must be passed (as null) to reach the entityTag parameter.
        return File(
            image.Content,
            image.ContentType,
            lastModified: null,
            entityTag: new EntityTagHeaderValue(image.ETag));
    }

    /// <summary>
    /// Sets a recipe's image, replacing any existing one. PUT rather than POST: a recipe has at most
    /// one image at a known address, and uploading the same file twice leaves the same single image.
    /// </summary>
    [HttpPut("{id:int}/image")]
    // A backstop against a caller making us buffer 100MB just to be told it was too big — not the
    // size rule itself, which is the validator's (and which reports it far more helpfully).
    //
    // Deliberately *above* MaxBytes: this limit measures the whole multipart body — boundaries, part
    // headers, filename and all — whereas the rule is about the file inside it. Setting the two equal
    // would mean a file of exactly the advertised 5 MB got rejected by the transport, because the
    // envelope pushed it over, with a message about request bodies rather than about the image. The
    // slack lets every upload the rule permits actually reach it.
    [RequestSizeLimit(UploadRecipeImageViewModelValidator.MaxBytes + MultipartEnvelopeAllowance)]
    // A missing file needs no guard here: `file` is non-nullable, so [ApiController]'s model binding
    // rejects the request with "The file field is required." before this action runs. A null-check
    // would be unreachable — pinned by PutImage_rejects_a_request_with_no_file.
    public async Task<IActionResult> PutImage(int id, IFormFile file, CancellationToken ct)
    {
        // IFormFile stops here. Below this line the upload is a stream and a length: the layers that
        // validate and store it have no business knowing an HTTP form delivered it. Note that neither
        // file.ContentType nor file.FileName is read — both are attacker-controlled, and the format is
        // established from the bytes instead.
        await using var content = file.OpenReadStream();
        var viewModel = new UploadRecipeImageViewModel(content, file.Length);

        return await _facade.SetImageAsync(id, viewModel, ct) ? NoContent() : NotFound();
    }

    /// <summary>Removes a recipe's image. 404 when no such recipe, or it had no image.</summary>
    [HttpDelete("{id:int}/image")]
    public async Task<IActionResult> DeleteImage(int id, CancellationToken ct)
    {
        return await _facade.RemoveImageAsync(id, ct) ? NoContent() : NotFound();
    }
}
