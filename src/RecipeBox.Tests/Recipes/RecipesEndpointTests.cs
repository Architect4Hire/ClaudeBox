using System.Net;
using System.Net.Http.Json;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;
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

        var recipes = await client.GetFromJsonAsync<List<RecipeSummaryServiceModel>>("/api/recipes");

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

        var recipes = await client.GetFromJsonAsync<List<RecipeSummaryServiceModel>>("/api/recipes?category=Mains");

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

        var recipe = await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{id}");

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

        var request = new CreateRecipeViewModel(
            Name: "Pancakes",
            Description: "Fluffy",
            Servings: 4,
            Ingredients: new List<CreateIngredientViewModel> { new("Flour", 2, "cups") },
            Steps: new List<CreateStepViewModel> { new(1, "Mix"), new(2, "Cook") });

        var response = await client.PostAsJsonAsync("/api/recipes", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<RecipeDetailServiceModel>();
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);
        Assert.Equal("Pancakes", created.Name);

        // Round-trip: the created recipe is retrievable.
        var fetched = await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{created.Id}");
        Assert.Equal("Pancakes", fetched!.Name);
        Assert.Equal(2, fetched.Steps.Count);
    }

    [Fact]
    public async Task Create_with_taxonomy_persists_and_round_trips_categories_and_tags()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(_ => Task.CompletedTask);
        var client = factory.CreateClient();

        var request = new CreateRecipeViewModel(
            Name: "Brownies",
            Description: "Fudgy",
            Servings: 9,
            Ingredients: new List<CreateIngredientViewModel> { new("Chocolate", 200, "g") },
            Steps: new List<CreateStepViewModel> { new(1, "Bake") },
            Categories: new List<string> { "Dessert" },
            Tags: new List<string> { "vegetarian", "comfort" });

        var response = await client.PostAsJsonAsync("/api/recipes", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RecipeDetailServiceModel>();
        Assert.NotNull(created);

        var fetched = await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{created!.Id}");
        Assert.Equal(new[] { "Dessert" }, fetched!.Categories);
        Assert.Equal(new[] { "vegetarian", "comfort" }, fetched.Tags.OrderByDescending(t => t).ToArray());
    }

    [Fact]
    public async Task Update_replaces_taxonomy_with_the_supplied_names()
    {
        await using var factory = new RecipeApiFactory();
        var id = 0;
        await factory.SeedAsync(async db =>
        {
            var recipe = SampleRecipe("Bread", "Baking"); // starts in category "Baking", no tags
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            id = recipe.Id;
        });
        var client = factory.CreateClient();

        var request = new UpdateRecipeViewModel(
            Name: "Sourdough",
            Description: "Tangy",
            Servings: 8,
            Ingredients: new List<UpdateIngredientViewModel> { new("Starter", 1, "cup") },
            Steps: new List<UpdateStepViewModel> { new(1, "Feed") },
            Categories: new List<string> { "Baking", "Artisan" },
            Tags: new List<string> { "rustic" });

        var response = await client.PutAsJsonAsync($"/api/recipes/{id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fetched = await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{id}");
        Assert.Equal(new[] { "Artisan", "Baking" }, fetched!.Categories.OrderBy(c => c).ToArray());
        Assert.Equal(new[] { "rustic" }, fetched.Tags);
    }

    [Fact]
    public async Task Create_returns_400_for_invalid_request()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(_ => Task.CompletedTask);
        var client = factory.CreateClient();

        var invalid = new CreateRecipeViewModel(
            Name: "",
            Description: null,
            Servings: 0,
            Ingredients: new List<CreateIngredientViewModel>(),
            Steps: new List<CreateStepViewModel>());

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

        var request = new CreateRecipeViewModel(
            Name: "bread", // different case — the rule is case-insensitive
            Description: null,
            Servings: 2,
            Ingredients: new List<CreateIngredientViewModel> { new("Water", 1, "cup") },
            Steps: new List<CreateStepViewModel> { new(1, "Combine") });

        var response = await client.PostAsJsonAsync("/api/recipes", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_replaces_recipe_and_returns_the_new_state()
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

        var request = new UpdateRecipeViewModel(
            Name: "Sourdough",
            Description: "Tangy",
            Servings: 8,
            Ingredients: new List<UpdateIngredientViewModel> { new("Starter", 1, "cup") },
            Steps: new List<UpdateStepViewModel> { new(1, "Feed"), new(2, "Bake") });

        var response = await client.PutAsJsonAsync($"/api/recipes/{id}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RecipeDetailServiceModel>();
        Assert.NotNull(updated);
        Assert.Equal("Sourdough", updated!.Name);
        Assert.Equal(8, updated.Servings);

        // Round-trip: the persisted recipe reflects the edit, children replaced.
        var fetched = await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{id}");
        Assert.Equal("Sourdough", fetched!.Name);
        Assert.Equal("Starter", Assert.Single(fetched.Ingredients).Name);
        Assert.Equal(new[] { 1, 2 }, fetched.Steps.Select(s => s.Order).ToArray());
    }

    [Fact]
    public async Task Update_returns_404_when_missing()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(_ => Task.CompletedTask);
        var client = factory.CreateClient();

        var request = new UpdateRecipeViewModel(
            Name: "Ghost",
            Description: null,
            Servings: 2,
            Ingredients: new List<UpdateIngredientViewModel> { new("Air", 1, null) },
            Steps: new List<UpdateStepViewModel> { new(1, "Vanish") });

        var response = await client.PutAsJsonAsync("/api/recipes/999", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_returns_400_for_invalid_request()
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

        var invalid = new UpdateRecipeViewModel(
            Name: "",
            Description: null,
            Servings: 0,
            Ingredients: new List<UpdateIngredientViewModel>(),
            Steps: new List<UpdateStepViewModel>());

        var response = await client.PutAsJsonAsync($"/api/recipes/{id}", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(problem);
        Assert.Contains(problem!.Errors.Keys, k => k.Contains("Name"));
    }

    [Fact]
    public async Task Update_returns_409_when_name_taken_by_another_recipe()
    {
        await using var factory = new RecipeApiFactory();
        var id = 0;
        await factory.SeedAsync(async db =>
        {
            db.Recipes.Add(SampleRecipe("Soup", "Mains"));
            var bread = SampleRecipe("Bread", "Baking");
            db.Recipes.Add(bread);
            await db.SaveChangesAsync();
            id = bread.Id;
        });
        var client = factory.CreateClient();

        // Rename "Bread" to an existing recipe's name (case-insensitively).
        var request = new UpdateRecipeViewModel(
            Name: "soup",
            Description: null,
            Servings: 2,
            Ingredients: new List<UpdateIngredientViewModel> { new("Water", 1, "cup") },
            Steps: new List<UpdateStepViewModel> { new(1, "Combine") });

        var response = await client.PutAsJsonAsync($"/api/recipes/{id}", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record ValidationProblemResponse(Dictionary<string, string[]> Errors);
}
