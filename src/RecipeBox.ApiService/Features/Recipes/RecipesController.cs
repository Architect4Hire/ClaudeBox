using Microsoft.AspNetCore.Mvc;
using RecipeBox.ApiService.Features.Recipes.Dtos;
using RecipeBox.ApiService.Features.Recipes.Facade;

namespace RecipeBox.ApiService.Features.Recipes;

/// <summary>
/// HTTP surface for recipes. Thin by design: binds the request, calls the facade, and shapes the
/// result. No validation, caching, business logic, or data access.
/// </summary>
[ApiController]
[Route("api/recipes")]
public class RecipesController(IRecipeFacade facade) : ControllerBase
{
    private readonly IRecipeFacade _facade = facade;

    /// <summary>Lists recipe summaries, optionally filtered to a single category.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecipeSummaryDto>>> List(
        [FromQuery] string? category, CancellationToken ct)
    {
        return Ok(await _facade.ListAsync(category, ct));
    }

    /// <summary>Gets one recipe with its ingredients and ordered steps.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RecipeDetailDto>> GetById(int id, CancellationToken ct)
    {
        var recipe = await _facade.GetByIdAsync(id, ct);
        return recipe is null ? NotFound() : Ok(recipe);
    }

    /// <summary>Creates a recipe together with its ingredients and ordered steps.</summary>
    [HttpPost]
    public async Task<ActionResult<RecipeDetailDto>> Create(
        [FromBody] CreateRecipeRequest request, CancellationToken ct)
    {
        var created = await _facade.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
