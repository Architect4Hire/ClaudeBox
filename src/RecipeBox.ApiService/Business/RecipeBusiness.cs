using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Infrastructure;
using RecipeBox.ApiService.Managers.Mappers;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Business;

/// <summary>
/// Orchestration for the Recipes feature. Depends only on <see cref="IRecipeDataLayer"/>; detail
/// reads map the loaded domain entity to a service model, the list read passes the data layer's
/// projected summaries straight through, and create translates the view model to a domain entity,
/// applies the unique-name rule, and maps the persisted result back up to a service model.
/// </summary>
public class RecipeBusiness(IRecipeDataLayer data) : IRecipeBusiness
{
    private readonly IRecipeDataLayer _data = data;

    // Translate the validated view model into normalized domain criteria — the same VM→domain step the
    // write paths do with ToEntity(), and what keeps view models from reaching the data layer. The
    // results need no mapping back: the data layer projects them to summary service models in SQL
    // (counts without materializing rows), so they pass straight through.
    public Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(
        RecipeFilterViewModel filter, CancellationToken ct) =>
        _data.ListAsync(filter.ToFilter(), ct);

    public async Task<RecipeDetailServiceModel?> GetByIdAsync(int id, CancellationToken ct)
    {
        var recipe = await _data.GetByIdAsync(id, ct);
        return recipe?.ToServiceModel();
    }

    public async Task<RecipeDetailServiceModel> CreateAsync(CreateRecipeViewModel viewModel, CancellationToken ct)
    {
        // Translate the validated view model into a domain entity before touching persistence.
        var recipe = viewModel.ToEntity();

        // Data-dependent rule: name uniqueness needs the DB, so it lives here, not in the validator.
        // The DB's unique index is the real backstop (a concurrent create can slip past this check);
        // the repository translates that violation to the same RecipeNameConflictException.
        if (await _data.ExistsByNameAsync(recipe.Name, ct))
        {
            throw new RecipeNameConflictException(recipe.Name);
        }

        var created = await _data.AddAsync(recipe, ct);
        return created.ToServiceModel();
    }

    public async Task<RecipeDetailServiceModel?> UpdateAsync(int id, UpdateRecipeViewModel viewModel, CancellationToken ct)
    {
        // Translate the validated view model into a detached carrier of the edited values.
        var incoming = viewModel.ToEntity();

        // Data-dependent rule: the name must be unique among *other* recipes (a recipe may keep its own
        // name). As with create, the DB's unique index is the real backstop against a concurrent rename;
        // the repository translates that violation to the same RecipeNameConflictException.
        if (await _data.ExistsWithNameExceptAsync(incoming.Name, id, ct))
        {
            throw new RecipeNameConflictException(incoming.Name);
        }

        var updated = await _data.UpdateAsync(id, incoming, ct);
        return updated?.ToServiceModel();
    }

    // Deleting a recipe also reaps the categories and tags it orphaned, and its image. That sequencing
    // is a data concern, so it lives in the data layer and arrives here as one operation.
    public Task<bool> DeleteAsync(int id, CancellationToken ct) =>
        _data.DeleteRecipeAsync(id, ct);

    public async Task<RecipeImageServiceModel?> GetImageAsync(int id, CancellationToken ct)
    {
        var image = await _data.OpenImageAsync(id, ct);
        return image?.ToServiceModel();
    }

    public async Task<bool> SetImageAsync(int id, UploadRecipeImageViewModel viewModel, CancellationToken ct)
    {
        // What the file *is*, decided from its bytes. The validator has already rejected anything
        // unrecognised, so a null here would mean it didn't run — hence the throw rather than a
        // fallback: guessing a content type is precisely the mistake this whole path exists to avoid.
        var contentType = await RecipeImageFormat.DetectAsync(viewModel.Content, ct)
            ?? throw new InvalidOperationException(
                "Upload reached the business layer without a recognised image format; " +
                "UploadRecipeImageViewModelValidator should have rejected it.");

        var blobName = NewBlobName(id, contentType);
        return await _data.SetImageAsync(id, blobName, viewModel.Content, contentType, ct);
    }

    public Task<bool> RemoveImageAsync(int id, CancellationToken ct) =>
        _data.RemoveImageAsync(id, ct);

    /// <summary>
    /// Mints a fresh name for every upload rather than reusing one per recipe.
    /// <para>That's what lets the data layer upload before it writes the row: a new name can't collide
    /// with the image currently being served, so the new bytes stay invisible until the row points at
    /// them, and a failed upload leaves the old image intact. Reusing a stable name would overwrite the
    /// live image in place, and a failure mid-upload would corrupt it.</para>
    /// <para>The recipe id leads so the container groups by recipe, which makes an orphan obvious when
    /// browsing the store.</para>
    /// </summary>
    private static string NewBlobName(int id, string contentType) =>
        $"recipes/{id}/{Guid.NewGuid():N}{Extension(contentType)}";

    // Cosmetic only — nothing reads the format back off the name (the blob carries its own content
    // type). It's here so a human browsing the container sees what a blob is without opening it.
    private static string Extension(string contentType) => contentType switch
    {
        RecipeImageFormat.Jpeg => ".jpg",
        RecipeImageFormat.Png => ".png",
        RecipeImageFormat.WebP => ".webp",
        _ => "",
    };
}
