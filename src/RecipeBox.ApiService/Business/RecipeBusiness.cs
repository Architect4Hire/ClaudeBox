using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Mappers;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

namespace RecipeBox.ApiService.Business;

/// <summary>
/// Orchestration for the Recipes feature. Depends only on <see cref="IRecipeRepository"/>; detail
/// reads map the loaded domain entity to a service model, the list read passes the repository's
/// projected summaries straight through, and create translates the view model to a domain entity,
/// applies the unique-name rule, and maps the persisted result back up to a service model.
/// </summary>
public class RecipeBusiness(IRecipeRepository repository) : IRecipeBusiness
{
    private readonly IRecipeRepository _repository = repository;

    // The list is projected to summary service models in SQL by the data layer (counts without
    // materializing rows), so there is nothing to map here — pass it straight through.
    public Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(string? category, CancellationToken ct) =>
        _repository.ListAsync(category, ct);

    public async Task<RecipeDetailServiceModel?> GetByIdAsync(int id, CancellationToken ct)
    {
        var recipe = await _repository.GetByIdAsync(id, ct);
        return recipe?.ToServiceModel();
    }

    public async Task<RecipeDetailServiceModel> CreateAsync(CreateRecipeViewModel viewModel, CancellationToken ct)
    {
        // Translate the validated view model into a domain entity before touching persistence.
        var recipe = viewModel.ToEntity();

        // Data-dependent rule: name uniqueness needs the DB, so it lives here, not in the validator.
        // The DB's unique index is the real backstop (a concurrent create can slip past this check);
        // the repository translates that violation to the same RecipeNameConflictException.
        if (await _repository.ExistsByNameAsync(recipe.Name, ct))
        {
            throw new RecipeNameConflictException(recipe.Name);
        }

        var created = await _repository.AddAsync(recipe, ct);
        return created.ToServiceModel();
    }
}
