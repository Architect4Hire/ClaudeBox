using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Data-layer tests for <see cref="RecipeRepository"/> against a real (in-memory SQLite) database:
/// the category filter, the count projections, ordered-step loading, and the case-insensitive
/// name lookup. The Postgres-only functional unique index is not exercised here (SQLite builds the
/// schema from the model, not the migration); that backstop needs a Postgres integration test.
/// </summary>
public class RecipeRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RecipeRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    private RecipeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<RecipeDbContext>().UseSqlite(_connection).Options);

    private static Recipe Recipe(string name, string category, int ingredients, int steps) => new()
    {
        Name = name,
        Servings = 4,
        Categories = { new Category { Name = category } },
        Ingredients = Enumerable.Range(1, ingredients)
            .Select(i => new Ingredient { Name = $"Ing{i}", Quantity = i })
            .ToList(),
        Steps = Enumerable.Range(1, steps)
            .Select(i => new Step { Order = i, Instruction = $"Step {i}" })
            .ToList(),
    };

    /// <summary>Replaces the generated "Ing1..." lines with named ingredients, for the search tests.</summary>
    private static Recipe WithIngredients(Recipe recipe, params string[] names)
    {
        recipe.Ingredients.Clear();
        foreach (var name in names)
        {
            recipe.Ingredients.Add(new Ingredient { Name = name, Quantity = 1 });
        }

        return recipe;
    }

    /// <summary>Re-points a recipe at a specific category instance, so several can share one row.</summary>
    private static Recipe WithCategory(Recipe recipe, Category category)
    {
        recipe.Categories.Clear();
        recipe.Categories.Add(category);
        return recipe;
    }

    /// <summary>Attaches a tag instance, which several recipes can share (the helper adds none).</summary>
    private static Recipe WithTag(Recipe recipe, Tag tag)
    {
        recipe.Tags.Add(tag);
        return recipe;
    }

    private async Task SeedAsync(params Recipe[] recipes)
    {
        await using var context = NewContext();
        context.Recipes.AddRange(recipes);
        await context.SaveChangesAsync();
    }

    // ── Transactions ─────────────────────────────────────────────────────────────────────────────
    // The data layer composes multi-write operations behind ExecuteInTransactionAsync (see
    // RecipeDataLayer.DeleteRecipeAsync). Those tests mock the repository, so they can only prove the
    // unit was handed over — these prove it's wired to a real transaction. That it also survives
    // Npgsql's retrying execution strategy is provider-specific, and lives in
    // RecipeRepositoryPostgresTests.

    [Fact]
    public async Task ExecuteInTransactionAsync_rolls_back_writes_when_the_operation_throws()
    {
        await SeedAsync(Recipe("Soup", "Mains", 2, 3));
        await using var context = NewContext();
        var sut = new RecipeRepository(context);
        var id = await IdOfAsync("Soup");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteInTransactionAsync<bool>(
                async token =>
                {
                    Assert.True(await sut.DeleteAsync(id, token));
                    throw new InvalidOperationException("something later failed");
                },
                CancellationToken.None));

        await using var verify = NewContext();
        Assert.True(await verify.Recipes.AnyAsync(r => r.Id == id));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_keeps_writes_when_the_operation_returns()
    {
        await SeedAsync(Recipe("Soup", "Mains", 2, 3));
        await using var context = NewContext();
        var sut = new RecipeRepository(context);
        var id = await IdOfAsync("Soup");

        var deleted = await sut.ExecuteInTransactionAsync(
            token => sut.DeleteAsync(id, token), CancellationToken.None);

        Assert.True(deleted);
        await using var verify = NewContext();
        Assert.False(await verify.Recipes.AnyAsync(r => r.Id == id));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_returns_the_operations_result()
    {
        await using var context = NewContext();
        var sut = new RecipeRepository(context);

        Assert.Equal(42, await sut.ExecuteInTransactionAsync(_ => Task.FromResult(42), CancellationToken.None));
    }

    [Fact]
    public async Task DataLayer_delete_leaves_the_recipe_when_a_later_sweep_fails()
    {
        // The whole point of the transaction, driven end-to-end: a sweep that blows up must take the
        // recipe delete back with it rather than leaving the store half-swept.
        await SeedAsync(Recipe("Soup", "Mains", 2, 3));
        await using var context = NewContext();
        var id = await IdOfAsync("Soup");
        var sut = new RecipeDataLayer(new ThrowingSweepRepository(context), new FakeRecipeImageStore());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.DeleteRecipeAsync(id, CancellationToken.None));

        await using var verify = NewContext();
        Assert.True(await verify.Recipes.AnyAsync(r => r.Id == id));
    }

    /// <summary>
    /// A real repository (real transaction, real deletes) whose category sweep fails, to force a
    /// rollback mid-composition. Delegates rather than subclasses, so no production method has to be
    /// made <c>virtual</c> just to be tested.
    /// </summary>
    private sealed class ThrowingSweepRepository(RecipeDbContext db) : IRecipeRepository
    {
        private readonly RecipeRepository _inner = new(db);

        public Task<int> DeleteOrphanedCategoriesAsync(CancellationToken ct) =>
            throw new InvalidOperationException("sweep failed");

        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
            _inner.ExecuteInTransactionAsync(operation, ct);

        public Task<bool> DeleteAsync(int id, CancellationToken ct) => _inner.DeleteAsync(id, ct);

        public Task<int> DeleteOrphanedTagsAsync(CancellationToken ct) =>
            _inner.DeleteOrphanedTagsAsync(ct);

        public Task<IReadOnlyList<RecipeSummaryServiceModel>> ListAsync(RecipeFilter filter, CancellationToken ct) =>
            _inner.ListAsync(filter, ct);

        public Task<Recipe?> GetByIdAsync(int id, CancellationToken ct) => _inner.GetByIdAsync(id, ct);

        public Task<bool> ExistsByNameAsync(string name, CancellationToken ct) =>
            _inner.ExistsByNameAsync(name, ct);

        public Task<bool> ExistsWithNameExceptAsync(string name, int excludingId, CancellationToken ct) =>
            _inner.ExistsWithNameExceptAsync(name, excludingId, ct);

        public Task<Recipe> AddAsync(Recipe recipe, CancellationToken ct) => _inner.AddAsync(recipe, ct);

        public Task<Recipe?> UpdateAsync(int id, Recipe incoming, CancellationToken ct) =>
            _inner.UpdateAsync(id, incoming, ct);

        public Task<string?> GetImageBlobNameAsync(int id, CancellationToken ct) =>
            _inner.GetImageBlobNameAsync(id, ct);

        public Task<ImageAssignment> SetImageBlobNameAsync(int id, string? blobName, CancellationToken ct) =>
            _inner.SetImageBlobNameAsync(id, blobName, ct);
    }

    private async Task<int> IdOfAsync(string name)
    {
        await using var context = NewContext();
        return await context.Recipes.Where(r => r.Name == name).Select(r => r.Id).SingleAsync();
    }

    [Fact]
    public async Task ListAsync_returns_all_ordered_by_name_with_counts()
    {
        await SeedAsync(
            Recipe("Soup", "Mains", ingredients: 2, steps: 3),
            Recipe("Bread", "Baking", ingredients: 5, steps: 4));
        var sut = new RecipeRepository(NewContext());

        var result = await sut.ListAsync(RecipeFilter.None, CancellationToken.None);

        Assert.Equal(new[] { "Bread", "Soup" }, result.Select(r => r.Name).ToArray());
        var bread = result[0];
        Assert.Equal(5, bread.IngredientCount);
        Assert.Equal(4, bread.StepCount);
        Assert.Equal(new[] { "Baking" }, bread.Categories);
    }

    [Fact]
    public async Task ListAsync_filters_by_category()
    {
        await SeedAsync(
            Recipe("Soup", "Mains", 2, 3),
            Recipe("Bread", "Baking", 5, 4));
        var sut = new RecipeRepository(NewContext());

        var result = await sut.ListAsync(new RecipeFilter("Baking", null), CancellationToken.None);

        Assert.Equal("Bread", Assert.Single(result).Name);
    }

    // ── Ingredient search ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_filters_by_ingredient_ignoring_case()
    {
        await SeedAsync(
            WithIngredients(Recipe("Bread", "Baking", 0, 4), "Flour", "Yeast"),
            WithIngredients(Recipe("Soup", "Mains", 0, 3), "Carrot"));
        var sut = new RecipeRepository(NewContext());

        // The stored name is "Flour"; a lowercase term must still match it.
        var result = await sut.ListAsync(new RecipeFilter(null, "flour"), CancellationToken.None);

        Assert.Equal("Bread", Assert.Single(result).Name);
    }

    [Fact]
    public async Task ListAsync_matches_an_ingredient_on_a_partial_name()
    {
        await SeedAsync(WithIngredients(Recipe("Bread", "Baking", 0, 4), "Plain Flour"));
        var sut = new RecipeRepository(NewContext());

        // Substring, not prefix or exact: "flo" sits in the middle of "Plain Flour".
        var result = await sut.ListAsync(new RecipeFilter(null, "flo"), CancellationToken.None);

        Assert.Equal("Bread", Assert.Single(result).Name);
    }

    [Fact]
    public async Task ListAsync_combines_category_and_ingredient_with_and()
    {
        // Bread and Meringue share one Category instance: seeding two rows named "Baking" would trip
        // the unique index on category name.
        var baking = new Category { Name = "Baking" };
        await SeedAsync(
            WithIngredients(WithCategory(Recipe("Bread", "Baking", 0, 4), baking), "Flour"),
            // Same ingredient, different category — excluded by the category half of the filter.
            WithIngredients(Recipe("Pasta", "Mains", 0, 4), "Flour"),
            // Same category, different ingredient — excluded by the ingredient half.
            WithIngredients(WithCategory(Recipe("Meringue", "Baking", 0, 4), baking), "Egg White"));
        var sut = new RecipeRepository(NewContext());

        var result = await sut.ListAsync(new RecipeFilter("Baking", "flour"), CancellationToken.None);

        Assert.Equal("Bread", Assert.Single(result).Name);
    }

    [Fact]
    public async Task ListAsync_returns_empty_when_no_ingredient_matches()
    {
        await SeedAsync(WithIngredients(Recipe("Bread", "Baking", 0, 4), "Flour"));
        var sut = new RecipeRepository(NewContext());

        var result = await sut.ListAsync(new RecipeFilter(null, "saffron"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListAsync_counts_are_unaffected_by_the_ingredient_filter()
    {
        // The summary counts every ingredient on a matched recipe, not just the ones that matched.
        await SeedAsync(WithIngredients(Recipe("Bread", "Baking", 0, 4), "Flour", "Yeast", "Salt"));
        var sut = new RecipeRepository(NewContext());

        var result = await sut.ListAsync(new RecipeFilter(null, "flour"), CancellationToken.None);

        Assert.Equal(3, Assert.Single(result).IngredientCount);
    }

    [Fact]
    public async Task ListAsync_treats_wildcards_in_the_term_literally()
    {
        // Guards the LIKE-injection footgun: '%' must match a literal '%', not "anything".
        await SeedAsync(WithIngredients(Recipe("Bread", "Baking", 0, 4), "Flour"));
        var sut = new RecipeRepository(NewContext());

        var result = await sut.ListAsync(new RecipeFilter(null, "%"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByIdAsync_loads_ingredients_and_returns_steps_in_order()
    {
        int id;
        await using (var context = NewContext())
        {
            var recipe = new Recipe
            {
                Name = "Cake",
                Servings = 8,
                Ingredients = { new Ingredient { Name = "Flour", Quantity = 2, Unit = "cups" } },
                Steps =
                {
                    new Step { Order = 3, Instruction = "Bake" },
                    new Step { Order = 1, Instruction = "Mix" },
                    new Step { Order = 2, Instruction = "Pour" },
                },
            };
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
            id = recipe.Id;
        }
        var sut = new RecipeRepository(NewContext());

        var result = await sut.GetByIdAsync(id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Ingredients);
        Assert.Equal(new[] { 1, 2, 3 }, result.Steps.Select(s => s.Order).ToArray());
        Assert.Equal("Mix", result.Steps.First().Instruction);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_missing()
    {
        var sut = new RecipeRepository(NewContext());

        var result = await sut.GetByIdAsync(999, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExistsByNameAsync_matches_case_insensitively()
    {
        await SeedAsync(Recipe("Bread", "Baking", 1, 1));
        var sut = new RecipeRepository(NewContext());

        Assert.True(await sut.ExistsByNameAsync("bread", CancellationToken.None));
        Assert.True(await sut.ExistsByNameAsync("BREAD", CancellationToken.None));
        Assert.False(await sut.ExistsByNameAsync("Soup", CancellationToken.None));
    }

    [Fact]
    public async Task ExistsWithNameExceptAsync_ignores_the_excluded_recipe()
    {
        int breadId;
        await using (var context = NewContext())
        {
            var bread = Recipe("Bread", "Baking", 1, 1);
            var soup = Recipe("Soup", "Mains", 1, 1);
            context.Recipes.AddRange(bread, soup);
            await context.SaveChangesAsync();
            breadId = bread.Id;
        }
        var sut = new RecipeRepository(NewContext());

        // "Bread" keeping its own name is not a conflict...
        Assert.False(await sut.ExistsWithNameExceptAsync("bread", breadId, CancellationToken.None));
        // ...but taking another recipe's name is (case-insensitively).
        Assert.True(await sut.ExistsWithNameExceptAsync("SOUP", breadId, CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_persists_recipe_and_assigns_id()
    {
        var sut = new RecipeRepository(NewContext());
        var recipe = Recipe("Pancakes", "Breakfast", ingredients: 1, steps: 2);

        var saved = await sut.AddAsync(recipe, CancellationToken.None);

        Assert.True(saved.Id > 0);
        await using var verify = NewContext();
        var reloaded = await verify.Recipes
            .Include(r => r.Steps)
            .FirstAsync(r => r.Id == saved.Id);
        Assert.Equal(2, reloaded.Steps.Count);
    }

    [Fact]
    public async Task UpdateAsync_overwrites_scalars_and_replaces_ingredients_and_steps()
    {
        int id;
        await using (var context = NewContext())
        {
            var recipe = Recipe("Bread", "Baking", ingredients: 3, steps: 3);
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
            id = recipe.Id;
        }
        var sut = new RecipeRepository(NewContext());

        var incoming = new Recipe
        {
            Name = "Sourdough",
            Description = "Tangy",
            Servings = 8,
            Ingredients = { new Ingredient { Name = "Starter", Quantity = 1, Unit = "cup" } },
            Steps =
            {
                new Step { Order = 1, Instruction = "Feed" },
                new Step { Order = 2, Instruction = "Bake" },
            },
        };

        var updated = await sut.UpdateAsync(id, incoming, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Sourdough", updated!.Name);

        await using var verify = NewContext();
        var reloaded = await verify.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstAsync(r => r.Id == id);
        Assert.Equal("Sourdough", reloaded.Name);
        Assert.Equal("Tangy", reloaded.Description);
        Assert.Equal(8, reloaded.Servings);
        // Children were replaced wholesale, not appended.
        Assert.Equal("Starter", Assert.Single(reloaded.Ingredients).Name);
        Assert.Equal(new[] { 1, 2 }, reloaded.Steps.OrderBy(s => s.Order).Select(s => s.Order).ToArray());
    }

    [Fact]
    public async Task UpdateAsync_returns_null_when_missing()
    {
        var sut = new RecipeRepository(NewContext());
        var incoming = new Recipe
        {
            Name = "Nope",
            Servings = 1,
            Ingredients = { new Ingredient { Name = "Air", Quantity = 1 } },
            Steps = { new Step { Order = 1, Instruction = "Nothing" } },
        };

        var result = await sut.UpdateAsync(999, incoming, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_replaces_taxonomy_and_reuses_existing_rows_by_name()
    {
        // An edit now replaces taxonomy wholesale (like ingredients/steps): a name it keeps ("Baking")
        // must reuse the existing row rather than duplicate it, and a name it drops ("Rustic") must go.
        int id;
        await using (var context = NewContext())
        {
            var recipe = new Recipe
            {
                Name = "Bread",
                Servings = 4,
                Categories = { new Category { Name = "Baking" } },
                Tags = { new Tag { Name = "Rustic" } },
                Ingredients = { new Ingredient { Name = "Flour", Quantity = 3 } },
                Steps = { new Step { Order = 1, Instruction = "Mix" } },
            };
            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
            id = recipe.Id;
        }
        var sut = new RecipeRepository(NewContext());

        var incoming = new Recipe
        {
            Name = "Sourdough",
            Servings = 8,
            Categories = { new Category { Name = "Baking" }, new Category { Name = "Dessert" } },
            Tags = { new Tag { Name = "Sweet" } },
            Ingredients = { new Ingredient { Name = "Starter", Quantity = 1, Unit = "cup" } },
            Steps = { new Step { Order = 1, Instruction = "Feed" } },
        };

        await sut.UpdateAsync(id, incoming, CancellationToken.None);

        await using var verify = NewContext();
        var reloaded = await verify.Recipes
            .Include(r => r.Categories)
            .Include(r => r.Tags)
            .FirstAsync(r => r.Id == id);
        Assert.Equal("Sourdough", reloaded.Name);
        Assert.Equal(new[] { "Baking", "Dessert" }, reloaded.Categories.Select(c => c.Name).OrderBy(n => n).ToArray());
        Assert.Equal(new[] { "Sweet" }, reloaded.Tags.Select(t => t.Name).ToArray());
        // "Baking" was reused, not duplicated; the dropped "Rustic" tag row is now unreferenced.
        Assert.Equal(1, await verify.Categories.CountAsync(c => c.Name == "Baking"));
    }

    [Fact]
    public async Task AddAsync_reuses_an_existing_category_by_name_instead_of_duplicating()
    {
        await SeedAsync(Recipe("Bread", "Baking", ingredients: 1, steps: 1));
        var sut = new RecipeRepository(NewContext());

        var second = new Recipe
        {
            Name = "Cake",
            Servings = 6,
            // Same category name as the seeded recipe — the repository must attach the existing row.
            Categories = { new Category { Name = "Baking" } },
            Tags = { new Tag { Name = "sweet" } },
            Ingredients = { new Ingredient { Name = "Sugar", Quantity = 2, Unit = "cups" } },
            Steps = { new Step { Order = 1, Instruction = "Bake" } },
        };

        await sut.AddAsync(second, CancellationToken.None);

        await using var verify = NewContext();
        // Exactly one "Baking" row, shared by both recipes.
        Assert.Equal(1, await verify.Categories.CountAsync(c => c.Name == "Baking"));
        Assert.Equal(2, await verify.Recipes.CountAsync(r => r.Categories.Any(c => c.Name == "Baking")));
    }

    [Fact]
    public async Task ListAsync_with_unmatched_category_returns_empty()
    {
        await SeedAsync(
            Recipe("Soup", "Mains", 2, 3),
            Recipe("Bread", "Baking", 5, 4));
        var sut = new RecipeRepository(NewContext());

        var result = await sut.ListAsync(new RecipeFilter("NoSuchCategory", null), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DeleteAsync_cascades_to_ingredients_and_steps()
    {
        await SeedAsync(Recipe("Soup", "Mains", 2, 3));
        var sut = new RecipeRepository(NewContext());

        var deleted = await sut.DeleteAsync(1, CancellationToken.None);

        Assert.True(deleted);
        await using var verify = NewContext();
        Assert.Empty(verify.Recipes);
        // The owned children go with the recipe rather than lingering as unreachable rows.
        Assert.Empty(verify.Ingredients);
        Assert.Empty(verify.Steps);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_unknown_id()
    {
        await SeedAsync(Recipe("Soup", "Mains", 2, 3));
        var sut = new RecipeRepository(NewContext());

        Assert.False(await sut.DeleteAsync(404, CancellationToken.None));

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Recipes.CountAsync());
    }

    [Fact]
    public async Task DeleteOrphanedCategoriesAsync_removes_only_categories_with_no_recipes()
    {
        await SeedAsync(
            Recipe("Soup", "Mains", 2, 3),
            Recipe("Bread", "Baking", 5, 4));
        await new RecipeRepository(NewContext()).DeleteAsync(1, CancellationToken.None);
        var sut = new RecipeRepository(NewContext());

        var reaped = await sut.DeleteOrphanedCategoriesAsync(CancellationToken.None);

        Assert.Equal(1, reaped);
        await using var verify = NewContext();
        // "Mains" lost its only recipe; "Baking" still has Bread and must survive.
        Assert.Equal("Baking", Assert.Single(verify.Categories).Name);
    }

    [Fact]
    public async Task DeleteOrphanedCategoriesAsync_is_a_no_op_when_every_category_is_used()
    {
        await SeedAsync(Recipe("Soup", "Mains", 2, 3));
        var sut = new RecipeRepository(NewContext());

        Assert.Equal(0, await sut.DeleteOrphanedCategoriesAsync(CancellationToken.None));

        await using var verify = NewContext();
        Assert.Single(verify.Categories);
    }

    [Fact]
    public async Task DeleteAsync_keeps_a_category_that_another_recipe_still_uses()
    {
        // Seeding writes entities straight through, bypassing the repository's resolve-by-name, so the
        // two recipes must share one Category instance — a second "Mains" row would breach its unique index.
        var mains = new Category { Name = "Mains" };
        await SeedAsync(
            WithCategory(Recipe("Soup", "Mains", 2, 3), mains),
            WithCategory(Recipe("Stew", "Mains", 1, 1), mains));
        await new RecipeRepository(NewContext()).DeleteAsync(1, CancellationToken.None);
        var sut = new RecipeRepository(NewContext());

        Assert.Equal(0, await sut.DeleteOrphanedCategoriesAsync(CancellationToken.None));

        await using var verify = NewContext();
        // Deleting one of two recipes sharing "Mains" must not strip the survivor's category.
        Assert.Equal("Mains", Assert.Single(verify.Categories).Name);
        Assert.Equal("Stew", Assert.Single(verify.Recipes).Name);
    }

    [Fact]
    public async Task DeleteOrphanedTagsAsync_removes_only_tags_with_no_recipes()
    {
        await SeedAsync(
            WithTag(Recipe("Soup", "Mains", 2, 3), new Tag { Name = "solo" }),
            WithTag(Recipe("Bread", "Baking", 5, 4), new Tag { Name = "quick" }));
        await new RecipeRepository(NewContext()).DeleteAsync(1, CancellationToken.None);
        var sut = new RecipeRepository(NewContext());

        var reaped = await sut.DeleteOrphanedTagsAsync(CancellationToken.None);

        Assert.Equal(1, reaped);
        await using var verify = NewContext();
        // "solo" lost its only recipe; "quick" still has Bread and must survive.
        Assert.Equal("quick", Assert.Single(verify.Tags).Name);
    }

    [Fact]
    public async Task DeleteAsync_keeps_a_tag_that_another_recipe_still_uses()
    {
        // As with categories, seeding bypasses resolve-by-name, so the shared tag must be one instance.
        var quick = new Tag { Name = "quick" };
        await SeedAsync(
            WithTag(Recipe("Soup", "Mains", 2, 3), quick),
            WithTag(Recipe("Stew", "Stews", 1, 1), quick));
        await new RecipeRepository(NewContext()).DeleteAsync(1, CancellationToken.None);
        var sut = new RecipeRepository(NewContext());

        Assert.Equal(0, await sut.DeleteOrphanedTagsAsync(CancellationToken.None));

        await using var verify = NewContext();
        Assert.Equal("quick", Assert.Single(verify.Tags).Name);
    }

    [Fact]
    public async Task DeleteOrphanedTagsAsync_is_a_no_op_when_every_tag_is_used()
    {
        await SeedAsync(WithTag(Recipe("Soup", "Mains", 2, 3), new Tag { Name = "quick" }));
        var sut = new RecipeRepository(NewContext());

        Assert.Equal(0, await sut.DeleteOrphanedTagsAsync(CancellationToken.None));

        await using var verify = NewContext();
        Assert.Single(verify.Tags);
    }

    public void Dispose() => _connection.Dispose();
}
