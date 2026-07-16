using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Models.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Postgres-backed integration tests for the one behaviour SQLite cannot exercise: the case-insensitive
/// functional unique index (<c>IX_Recipes_Name_Lower</c>, created via raw SQL in the
/// <c>AddRecipeNameUniqueIndex</c> migration) and the repository catch-block that translates its
/// violation into a <see cref="RecipeNameConflictException"/> (→ 409). This is the real backstop when a
/// concurrent create/rename slips past the business-layer pre-check; the unit suite only mocks around it.
/// <para>
/// The repository does no pre-check of its own, so calling it twice with the same name is a faithful,
/// deterministic stand-in for the lost race. If the <see cref="RecipeRepository.RecipeNameUniqueIndex"/>
/// constant ever drifts from the migration's index name, the exception filter stops matching and these
/// tests fail with a raw <see cref="DbUpdateException"/> instead of the domain exception.
/// </para>
/// Requires Docker; applies the real migrations so the functional index actually exists.
/// </summary>
[Trait("Category", "Integration")]
public class RecipeRepositoryPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18.3")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = NewContext();
        // Apply the real migrations (not EnsureCreated) so the raw-SQL functional index is present.
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private RecipeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<RecipeDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private static Recipe NewRecipe(string name) => new()
    {
        Name = name,
        Servings = 4,
        Ingredients = { new Ingredient { Name = "Flour", Quantity = 2, Unit = "cups" } },
        Steps = { new Step { Order = 1, Instruction = "Mix" } },
    };

    private static Recipe NewRecipe(string name, string ingredient)
    {
        var recipe = NewRecipe(name);
        recipe.Ingredients.Clear();
        recipe.Ingredients.Add(new Ingredient { Name = ingredient, Quantity = 1, Unit = "cups" });
        return recipe;
    }

    [Fact]
    public async Task AddAsync_translates_unique_index_violation_to_conflict()
    {
        await new RecipeRepository(NewContext()).AddAsync(NewRecipe("Bread"), CancellationToken.None);

        // A different-cased duplicate slips past any name pre-check but is caught by LOWER("Name").
        await Assert.ThrowsAsync<RecipeNameConflictException>(() =>
            new RecipeRepository(NewContext()).AddAsync(NewRecipe("bread"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_translates_unique_index_violation_to_conflict_on_rename()
    {
        await new RecipeRepository(NewContext()).AddAsync(NewRecipe("Alpha"), CancellationToken.None);
        var beta = await new RecipeRepository(NewContext()).AddAsync(NewRecipe("Beta"), CancellationToken.None);

        var rename = NewRecipe("alpha"); // collides with "Alpha" case-insensitively
        await Assert.ThrowsAsync<RecipeNameConflictException>(() =>
            new RecipeRepository(NewContext()).UpdateAsync(beta.Id, rename, CancellationToken.None));
    }

    // ── Ingredient search on the real provider ───────────────────────────────────────────────────
    // The SQLite suite covers this filter's behaviour, but it exercises SQLite's own Contains→LIKE
    // translation. These two pin the behaviour the production provider actually gives us, since the
    // repository's LOWER(...)/Contains choice is a deliberate bet on how Npgsql translates it.

    [Fact]
    public async Task ListAsync_matches_an_ingredient_case_insensitively_on_postgres()
    {
        var repository = new RecipeRepository(NewContext());
        await repository.AddAsync(NewRecipe("Bread", "Plain Flour"), CancellationToken.None);
        await repository.AddAsync(NewRecipe("Soup", "Carrot"), CancellationToken.None);

        var result = await new RecipeRepository(NewContext())
            .ListAsync(new RecipeFilter(null, "flour"), CancellationToken.None);

        Assert.Equal("Bread", Assert.Single(result).Name);
    }

    [Fact]
    public async Task ListAsync_treats_wildcards_in_the_term_literally_on_postgres()
    {
        // The real point of this one: Npgsql renders string.Contains as strpos(...) > 0 rather than a
        // LIKE pattern, so '%' is just a character. If that translation ever changed to an unescaped
        // LIKE, this term would match every recipe and the test would catch it.
        await new RecipeRepository(NewContext())
            .AddAsync(NewRecipe("Bread", "Plain Flour"), CancellationToken.None);

        var result = await new RecipeRepository(NewContext())
            .ListAsync(new RecipeFilter(null, "%"), CancellationToken.None);

        Assert.Empty(result);
    }
}
