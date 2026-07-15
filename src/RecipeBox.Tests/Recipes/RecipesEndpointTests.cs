using System.Net;
using System.Net.Http.Json;
using RecipeBox.ApiService.Domain;
using RecipeBox.ApiService.Features.Recipes.Dtos;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// End-to-end endpoint tests over <see cref="RecipeApiFactory"/> (in-memory SQLite + memory cache),
/// covering the happy paths plus a validation failure and a not-found.
/// </summary>
public class RecipesEndpointTests
{
    private static Recipe SampleRecipe(string name, string category) => new()
    {
        Name = name,
        Description = "A test recipe",
        Servings = 4,
        Ingredients = { new Ingredient { Name = "Flour", Quantity = 2, Unit = "cups" } },
        Steps =
        {
            new Step { Order = 2, Instruction = "Bake" },
            new Step { Order = 1, Instruction = "Mix" },
        },
        Categories = { new Category { Name = category } },
    };

    [Fact]
    public async Task List_returns_all_recipes()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(db =>
        {
            db.Recipes.Add(SampleRecipe("Bread", "Baking"));
            db.Recipes.Add(SampleRecipe("Soup", "Mains"));
            return Task.CompletedTask;
        });
        var client = factory.CreateClient();

        var recipes = await client.GetFromJsonAsync<List<RecipeSummaryDto>>("/api/recipes");

        Assert.NotNull(recipes);
        Assert.Equal(2, recipes!.Count);
        Assert.Contains(recipes, r => r.Name == "Bread" && r.IngredientCount == 1 && r.StepCount == 2);
    }

    [Fact]
    public async Task List_filters_by_category()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(db =>
        {
            db.Recipes.Add(SampleRecipe("Bread", "Baking"));
            db.Recipes.Add(SampleRecipe("Soup", "Mains"));
            return Task.CompletedTask;
        });
        var client = factory.CreateClient();

        var recipes = await client.GetFromJsonAsync<List<RecipeSummaryDto>>("/api/recipes?category=Mains");

        Assert.NotNull(recipes);
        Assert.Equal("Soup", Assert.Single(recipes!).Name);
    }

    [Fact]
    public async Task GetById_returns_recipe_with_ordered_steps()
    {
        await using var factory = new RecipeApiFactory();
        var id = 0;
        await factory.SeedAsync(async db =>
        {
            var recipe = SampleRecipe("Bread", "Baking");
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            id = recipe.Id;
        });
        var client = factory.CreateClient();

        var recipe = await client.GetFromJsonAsync<RecipeDetailDto>($"/api/recipes/{id}");

        Assert.NotNull(recipe);
        Assert.Equal("Bread", recipe!.Name);
        Assert.Single(recipe.Ingredients);
        Assert.Equal(new[] { 1, 2 }, recipe.Steps.Select(s => s.Order).ToArray());
        Assert.Equal("Mix", recipe.Steps[0].Instruction);
    }

    [Fact]
    public async Task GetById_returns_404_when_missing()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(_ => Task.CompletedTask);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/recipes/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_persists_recipe_and_returns_201_with_location()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(_ => Task.CompletedTask);
        var client = factory.CreateClient();

        var request = new CreateRecipeRequest(
            Name: "Pancakes",
            Description: "Fluffy",
            Servings: 4,
            Ingredients: new List<CreateIngredientRequest> { new("Flour", 2, "cups") },
            Steps: new List<CreateStepRequest> { new(1, "Mix"), new(2, "Cook") });

        var response = await client.PostAsJsonAsync("/api/recipes", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<RecipeDetailDto>();
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal("Pancakes", created.Name);

        // Round-trip: the created recipe is retrievable.
        var fetched = await client.GetFromJsonAsync<RecipeDetailDto>($"/api/recipes/{created.Id}");
        Assert.Equal("Pancakes", fetched!.Name);
        Assert.Equal(2, fetched.Steps.Count);
    }

    [Fact]
    public async Task Create_returns_400_for_invalid_request()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(_ => Task.CompletedTask);
        var client = factory.CreateClient();

        var invalid = new CreateRecipeRequest(
            Name: "",
            Description: null,
            Servings: 0,
            Ingredients: new List<CreateIngredientRequest>(),
            Steps: new List<CreateStepRequest>());

        var response = await client.PostAsJsonAsync("/api/recipes", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(problem);
        Assert.Contains(problem!.Errors.Keys, k => k.Contains("Name"));
    }

    [Fact]
    public async Task Create_returns_409_for_duplicate_name()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(db =>
        {
            db.Recipes.Add(SampleRecipe("Bread", "Baking"));
            return Task.CompletedTask;
        });
        var client = factory.CreateClient();

        var request = new CreateRecipeRequest(
            Name: "bread", // different case — the rule is case-insensitive
            Description: null,
            Servings: 2,
            Ingredients: new List<CreateIngredientRequest> { new("Water", 1, "cup") },
            Steps: new List<CreateStepRequest> { new(1, "Combine") });

        var response = await client.PostAsJsonAsync("/api/recipes", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record ValidationProblemResponse(Dictionary<string, string[]> Errors);
}
