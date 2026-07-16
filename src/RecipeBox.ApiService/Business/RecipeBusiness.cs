using RecipeBox.ApiService.Data;
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

    // Deleting a recipe also reaps the categories and tags it orphaned. That sequencing is a data
    // concern, so it lives in the data layer and arrives here as one operation.
    public Task<bool> DeleteAsync(int id, CancellationToken ct) =>
        _data.DeleteRecipeAsync(id, ct);
}
