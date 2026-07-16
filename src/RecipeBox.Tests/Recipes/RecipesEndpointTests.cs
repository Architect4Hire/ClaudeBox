using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using RecipeBox.ApiService.Managers.Models.ViewModels;
using RecipeBox.ApiService.Managers.Validators;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// End-to-end endpoint tests over <see cref="RecipeApiFactory"/> (in-memory SQLite + memory cache),
/// covering the happy paths plus a validation failure and a not-found.
/// </summary>
public class RecipesEndpointTests
{
    // A real JPEG signature — the endpoint sniffs the bytes, so a placeholder string wouldn't upload.
    private static readonly byte[] JpegBytes =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x02, 0x03];

    private static MultipartFormDataContent ImageForm(byte[] bytes, string declaredType, string fileName)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

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

    /// <summary>A sample recipe with a specific ingredient line, for the ingredient-search tests.</summary>
    private static Recipe RecipeWithIngredient(string name, string category, string ingredient)
    {
        var recipe = SampleRecipe(name, category);
        recipe.Ingredients.Clear();
        recipe.Ingredients.Add(new Ingredient { Name = ingredient, Quantity = 1, Unit = "cups" });
        return recipe;
    }

    [Fact]
    public async Task List_filters_by_ingredient()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(db =>
        {
            db.Recipes.Add(RecipeWithIngredient("Bread", "Baking", "Plain Flour"));
            db.Recipes.Add(RecipeWithIngredient("Soup", "Mains", "Carrot"));
            return Task.CompletedTask;
        });
        var client = factory.CreateClient();

        // Lowercase partial term against the stored "Plain Flour".
        var recipes = await client.GetFromJsonAsync<List<RecipeSummaryServiceModel>>(
            "/api/recipes?ingredient=flour");

        Assert.NotNull(recipes);
        Assert.Equal("Bread", Assert.Single(recipes!).Name);
    }

    [Fact]
    public async Task List_combines_category_and_ingredient_filters()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(db =>
        {
            // Bread and Meringue must share one Category instance — seeding two rows named "Baking"
            // would trip the unique index on category name.
            var baking = new Category { Name = "Baking" };
            var bread = RecipeWithIngredient("Bread", "Baking", "Flour");
            var meringue = RecipeWithIngredient("Meringue", "Baking", "Egg White");
            bread.Categories.Clear();
            bread.Categories.Add(baking);
            meringue.Categories.Clear();
            meringue.Categories.Add(baking);

            db.Recipes.Add(bread);
            db.Recipes.Add(RecipeWithIngredient("Pasta", "Mains", "Flour"));
            db.Recipes.Add(meringue);
            return Task.CompletedTask;
        });
        var client = factory.CreateClient();

        var recipes = await client.GetFromJsonAsync<List<RecipeSummaryServiceModel>>(
            "/api/recipes?category=Baking&ingredient=flour");

        // Only the recipe satisfying both filters comes back.
        Assert.NotNull(recipes);
        Assert.Equal("Bread", Assert.Single(recipes!).Name);
    }

    [Fact]
    public async Task List_returns_400_for_an_over_long_ingredient_term()
    {
        await using var factory = new RecipeApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/recipes?ingredient={new string('x', 201)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    [Fact]
    public async Task Delete_removes_the_recipe_and_returns_204()
    {
        await using var factory = new RecipeApiFactory();
        var id = 0;
        await factory.SeedAsync(async db =>
        {
            var soup = SampleRecipe("Soup", "Mains");
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();
            id = soup.Id;
        });
        var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/recipes/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The delete must be observable through the API, not just in the database — this also proves
        // the facade evicted the cached detail entry rather than serving a ghost.
        var afterDelete = await client.GetAsync($"/api/recipes/{id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_404_for_unknown_id()
    {
        await using var factory = new RecipeApiFactory();
        await factory.SeedAsync(_ => Task.CompletedTask);
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/api/recipes/404");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_drops_the_orphaned_category_from_the_filter_options()
    {
        await using var factory = new RecipeApiFactory();
        var id = 0;
        await factory.SeedAsync(async db =>
        {
            db.Recipes.Add(SampleRecipe("Bread", "Baking"));
            var soup = SampleRecipe("Soup", "Mains");
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();
            id = soup.Id;
        });
        var client = factory.CreateClient();

        await client.DeleteAsync($"/api/recipes/{id}");

        // Deleting the only "Mains" recipe must retire the category too, while "Baking" survives on Bread.
        var remaining = await client.GetFromJsonAsync<List<RecipeSummaryServiceModel>>("/api/recipes");
        Assert.Equal("Bread", Assert.Single(remaining!).Name);
        Assert.Empty(await client.GetFromJsonAsync<List<RecipeSummaryServiceModel>>("/api/recipes?category=Mains") ?? []);
    }

    [Fact]
    public async Task Delete_reaps_the_orphaned_tag_but_keeps_one_still_in_use()
    {
        await using var factory = new RecipeApiFactory();
        var id = 0;
        await factory.SeedAsync(async db =>
        {
            // One tag instance shared by both recipes; "solo" belongs to the doomed recipe alone.
            var shared = new Tag { Name = "quick" };
            var bread = SampleRecipe("Bread", "Baking");
            bread.Tags.Add(shared);
            db.Recipes.Add(bread);

            var soup = SampleRecipe("Soup", "Mains");
            soup.Tags.Add(shared);
            soup.Tags.Add(new Tag { Name = "solo" });
            db.Recipes.Add(soup);

            await db.SaveChangesAsync();
            id = soup.Id;
        });
        var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/recipes/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Tags have no endpoint, so this is read straight from the database: "solo" is orphaned and
        // must go, while "quick" is still on Bread and must not be swept up with it.
        var tags = await factory.QueryAsync(db => db.Tags.Select(t => t.Name).ToListAsync());
        Assert.Equal("quick", Assert.Single(tags));
    }

    // ── Images ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Seeds one recipe and returns its id, for the image tests that all need exactly that.</summary>
    private static async Task<int> SeedOneAsync(RecipeApiFactory factory, string name = "Bread")
    {
        await factory.SeedAsync(db =>
        {
            db.Recipes.Add(SampleRecipe(name, "Baking"));
            return Task.CompletedTask;
        });
        return await factory.QueryAsync(db => db.Recipes.Select(r => r.Id).FirstAsync());
    }

    [Fact]
    public async Task GetImage_returns_404_when_the_recipe_has_no_image()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/recipes/{id}/image");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetImage_returns_404_for_a_recipe_that_does_not_exist()
    {
        await using var factory = new RecipeApiFactory();
        await SeedOneAsync(factory);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/recipes/9999/image");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutImage_then_GetImage_round_trips_the_bytes()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        var put = await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "photo.jpg"));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var get = await client.GetAsync($"/api/recipes/{id}/image");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(JpegBytes, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetImage_is_revalidatable_and_not_content_sniffable()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "photo.jpg"));

        var response = await client.GetAsync($"/api/recipes/{id}/image");

        Assert.NotNull(response.Headers.ETag);
        Assert.True(response.Headers.CacheControl?.NoCache);
        // Without nosniff, a browser may second-guess the content type we set and run a mislabelled
        // file as markup from our own origin.
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task GetImage_answers_304_when_the_client_already_has_the_current_image()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "photo.jpg"));
        var first = await client.GetAsync($"/api/recipes/{id}/image");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/recipes/{id}/image");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(request);

        // This is what makes a stable image URL cheap: unchanged bytes cost headers, not a resend.
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task GetImage_serves_the_new_bytes_after_the_image_is_replaced()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "photo.jpg"));
        var first = await client.GetAsync($"/api/recipes/{id}/image");

        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49];
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(pngBytes, "image/png", "photo.png"));

        // The URL didn't change, so a client holding the old ETag must still be told the image did.
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/recipes/{id}/image");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(pngBytes, await second.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PutImage_makes_HasImage_true_on_the_detail_and_list_straight_away()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        // Prime both caches first: this is the whole point. Without invalidation these stay HasImage=false
        // for the cache TTL, the client never asks for an image, and the upload looks like it did nothing.
        Assert.False((await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{id}"))!.HasImage);
        Assert.False((await client.GetFromJsonAsync<List<RecipeSummaryServiceModel>>("/api/recipes"))![0].HasImage);

        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "photo.jpg"));

        Assert.True((await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{id}"))!.HasImage);
        Assert.True((await client.GetFromJsonAsync<List<RecipeSummaryServiceModel>>("/api/recipes"))![0].HasImage);
    }

    [Fact]
    public async Task PutImage_rejects_a_file_whose_bytes_are_not_an_image()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        // Declared image/jpeg, named .jpg, and actually HTML. Only the bytes give it away.
        var form = ImageForm(Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "image/jpeg", "evil.jpg");
        var response = await client.PutAsync($"/api/recipes/{id}/image", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Images.BlobNames);
    }

    [Fact]
    public async Task PutImage_stores_the_sniffed_content_type_not_the_declared_one()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        // Real JPEG bytes, but declared as text/html. Echo the declared type back on GET and we'd be
        // serving attacker-chosen markup from our own origin — so the declared type is never stored.
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "text/html", "photo.jpg"));

        var get = await client.GetAsync($"/api/recipes/{id}/image");
        Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal("image/jpeg", factory.Images.ContentTypeOf(Assert.Single(factory.Images.BlobNames)));
    }

    [Fact]
    public async Task PutImage_accepts_a_file_at_exactly_the_advertised_size_limit()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        // The boundary the users see: the API says "5 MB or smaller", so 5 MB exactly must work.
        // It nearly didn't — RequestSizeLimit caps the whole multipart body, so when it was set to
        // exactly MaxBytes the envelope around the file pushed a 5 MB upload over, and it was refused
        // with a message about request bodies. The unit test on the validator passed throughout;
        // only driving the real endpoint showed it.
        var atLimit = new byte[UploadRecipeImageViewModelValidator.MaxBytes];
        JpegBytes.CopyTo(atLimit, 0);

        var response = await client.PutAsync($"/api/recipes/{id}/image", ImageForm(atLimit, "image/jpeg", "big.jpg"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PutImage_rejects_a_file_over_the_size_limit()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        var tooBig = new byte[UploadRecipeImageViewModelValidator.MaxBytes + 1];
        JpegBytes.CopyTo(tooBig, 0);

        var response = await client.PutAsync($"/api/recipes/{id}/image", ImageForm(tooBig, "image/jpeg", "big.jpg"));

        // 400, not 413: the oversized body is caught while MVC reads the form, which turns it into a
        // model-binding failure long before any exception handler could restate it. Documented here
        // because it's the wire behaviour clients actually get.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Images.BlobNames);
    }

    [Fact]
    public async Task PutImage_rejects_a_request_with_no_file()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        var form = new MultipartFormDataContent();
        form.Add(new StringContent("not a file"), "somethingElse");

        // Model binding handles this — `file` is non-nullable, so [ApiController] 400s before the
        // action body runs. Pinned because the controller relies on it instead of null-checking.
        var response = await client.PutAsync($"/api/recipes/{id}/image", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Images.BlobNames);
    }

    [Fact]
    public async Task PutImage_returns_404_for_a_recipe_that_does_not_exist()
    {
        await using var factory = new RecipeApiFactory();
        await SeedOneAsync(factory);
        var client = factory.CreateClient();

        var response = await client.PutAsync("/api/recipes/9999/image", ImageForm(JpegBytes, "image/jpeg", "p.jpg"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Uploading before writing the row means a 404 can strand bytes; the compensating delete is what
        // keeps the container clean.
        Assert.Empty(factory.Images.BlobNames);
    }

    [Fact]
    public async Task PutImage_replacing_an_image_leaves_only_the_new_blob()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "one.jpg"));
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "two.jpg"));

        // Two uploads, one surviving blob — otherwise every re-upload leaks the one it replaced.
        Assert.Equal(2, factory.Images.Uploaded.Count);
        Assert.Single(factory.Images.BlobNames);
    }

    [Fact]
    public async Task DeleteImage_removes_the_image_and_its_blob()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "photo.jpg"));

        var delete = await client.DeleteAsync($"/api/recipes/{id}/image");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(factory.Images.BlobNames);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/recipes/{id}/image")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{id}"))!.HasImage);
    }

    [Fact]
    public async Task DeleteImage_returns_404_when_the_recipe_has_no_image()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/recipes/{id}/image")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_recipe_takes_its_image_blob_with_it()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "photo.jpg"));

        await client.DeleteAsync($"/api/recipes/{id}");

        // The row is the only record of which blob was this recipe's, so a delete that skipped the blob
        // would orphan it permanently.
        Assert.Empty(factory.Images.BlobNames);
    }

    [Fact]
    public async Task Editing_a_recipe_does_not_disturb_its_image()
    {
        await using var factory = new RecipeApiFactory();
        var id = await SeedOneAsync(factory);
        var client = factory.CreateClient();
        await client.PutAsync($"/api/recipes/{id}/image", ImageForm(JpegBytes, "image/jpeg", "photo.jpg"));

        var update = new UpdateRecipeViewModel(
            "Renamed Bread", "Now with a new name", 6,
            new List<UpdateIngredientViewModel> { new("Rye", 3, "cups") },
            new List<UpdateStepViewModel> { new(1, "Knead") },
            new List<string> { "Baking" },
            new List<string>());
        var response = await client.PutAsJsonAsync($"/api/recipes/{id}", update);
        response.EnsureSuccessStatusCode();

        // The edit view model has no image field, so an update that overwrote every scalar would silently
        // wipe the image.
        Assert.True((await client.GetFromJsonAsync<RecipeDetailServiceModel>($"/api/recipes/{id}"))!.HasImage);
        Assert.Single(factory.Images.BlobNames);
    }

    private sealed record ValidationProblemResponse(Dictionary<string, string[]> Errors);
}
