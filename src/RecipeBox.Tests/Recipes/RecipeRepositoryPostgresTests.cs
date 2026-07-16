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

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    /// <summary>
    /// A context configured the way Aspire's Npgsql integration configures the real one — with
    /// retry-on-failure enabled, and so with <c>NpgsqlRetryingExecutionStrategy</c> in play.
    /// <para>This is the difference that matters, and the reason the delete bug survived a green
    /// suite: the plain <see cref="NewContext"/> above has no execution strategy, so it accepts a
    /// caller-opened transaction that production rejects outright. A repository test that doesn't
    /// configure retry isn't testing the database the app actually talks to.</para>
    /// </summary>
    private AppDbContext NewRetryingContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
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

    // ── Transactions under the retrying execution strategy ───────────────────────────────────────
    // The other behaviour SQLite cannot exercise. Aspire's Npgsql integration enables retry-on-failure,
    // and NpgsqlRetryingExecutionStrategy rejects a transaction the caller opened itself. The whole
    // SQLite suite is blind to it — SQLite has no execution strategy, so it happily accepted the
    // transaction the data layer used to open, and DELETE /api/recipes/{id} returned 500 against real
    // Postgres while every test was green. These are the tests that would have said so.

    [Fact]
    public async Task Querying_inside_a_caller_opened_transaction_is_rejected_under_the_retrying_strategy()
    {
        await using var context = NewRetryingContext();

        // Pins the mechanism the tests below depend on, and the reason ExecuteInTransactionAsync takes
        // a callback instead of handing back a transaction. Without this they could pass vacuously: if
        // EnableRetryOnFailure ever stopped taking effect there'd be no strategy to offend, they'd go
        // green against a plain connection, and the bug they exist to catch could walk back in.
        //
        // Note where the rejection lands. Opening the transaction is fine — it's the first query
        // *inside* it that throws, which is why the failure surfaced from the middle of a delete
        // rather than at the point the transaction was opened.
        await using var transaction = await context.Database.BeginTransactionAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Recipes.AnyAsync());

        Assert.Contains("does not support user-initiated transactions", error.Message);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_works_under_the_retrying_execution_strategy()
    {
        await using var seedContext = NewRetryingContext();
        await new RecipeRepository(seedContext).AddAsync(NewRecipe("Doomed"), CancellationToken.None);

        await using var context = NewRetryingContext();
        var sut = new RecipeRepository(context);
        var id = await context.Recipes.Where(r => r.Name == "Doomed").Select(r => r.Id).SingleAsync();

        // Before the fix this threw InvalidOperationException: "The configured execution strategy
        // 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions."
        var deleted = await sut.ExecuteInTransactionAsync(
            async token =>
            {
                var removed = await sut.DeleteAsync(id, token);
                await sut.DeleteOrphanedCategoriesAsync(token);
                await sut.DeleteOrphanedTagsAsync(token);
                return removed;
            },
            CancellationToken.None);

        Assert.True(deleted);
        await using var verify = NewRetryingContext();
        Assert.False(await verify.Recipes.AnyAsync(r => r.Id == id));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_rolls_back_under_the_retrying_execution_strategy()
    {
        await using var seedContext = NewRetryingContext();
        await new RecipeRepository(seedContext).AddAsync(NewRecipe("Survivor"), CancellationToken.None);

        await using var context = NewRetryingContext();
        var sut = new RecipeRepository(context);
        var id = await context.Recipes.Where(r => r.Name == "Survivor").Select(r => r.Id).SingleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteInTransactionAsync<bool>(
                async token =>
                {
                    Assert.True(await sut.DeleteAsync(id, token));
                    throw new InvalidOperationException("sweep failed");
                },
                CancellationToken.None));

        // Rolling back is the point of the transaction; a strategy that swallowed the throw and
        // committed anyway would be worse than no transaction at all.
        await using var verify = NewRetryingContext();
        Assert.True(await verify.Recipes.AnyAsync(r => r.Id == id));
    }

    [Fact]
    public async Task DataLayer_delete_succeeds_against_real_postgres()
    {
        await using var seedContext = NewRetryingContext();
        await new RecipeRepository(seedContext).AddAsync(NewRecipe("Doomed"), CancellationToken.None);

        await using var context = NewRetryingContext();
        var id = await context.Recipes.Where(r => r.Name == "Doomed").Select(r => r.Id).SingleAsync();
        var images = new FakeRecipeImageStore();
        var sut = new RecipeDataLayer(new RecipeRepository(context), images);

        // The whole composed operation, on the provider the app actually runs on — the exact call
        // DELETE /api/recipes/{id} makes, which used to 500.
        Assert.True(await sut.DeleteRecipeAsync(id, CancellationToken.None));

        await using var verify = NewRetryingContext();
        Assert.False(await verify.Recipes.AnyAsync(r => r.Id == id));
    }
}
