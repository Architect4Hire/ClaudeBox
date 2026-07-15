using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Models.Domain;
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

    private async Task SeedAsync(params Recipe[] recipes)
    {
        await using var context = NewContext();
        context.Recipes.AddRange(recipes);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ListAsync_returns_all_ordered_by_name_with_counts()
    {
        await SeedAsync(
            Recipe("Soup", "Mains", ingredients: 2, steps: 3),
            Recipe("Bread", "Baking", ingredients: 5, steps: 4));
        var sut = new RecipeRepository(NewContext());

        var result = await sut.ListAsync(null, CancellationToken.None);

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

        var result = await sut.ListAsync("Baking", CancellationToken.None);

        Assert.Equal("Bread", Assert.Single(result).Name);
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

    public void Dispose() => _connection.Dispose();
}
