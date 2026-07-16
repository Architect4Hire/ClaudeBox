using Microsoft.AspNetCore.Mvc;
using RecipeBox.ApiService.Facade;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;

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

    /// <summary>Lists recipe summaries, optionally filtered to a single category.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecipeSummaryServiceModel>>> List(
        [FromQuery] string? category, CancellationToken ct)
    {
        return Ok(await _facade.ListAsync(category, ct));
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
}
