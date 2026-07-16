using NSubstitute;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Models.Domain;
using RecipeBox.ApiService.Managers.Models.ServiceModels;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// The data layer with a mocked repository: the delete composition (recipe first, then both taxonomy
/// sweeps, all in one transaction) that is the reason this seam exists, and the pass-throughs that
/// must not do anything of their own on the way past. That the transaction abstraction maps to a
/// real rollback is a repository concern — see <see cref="RecipeRepositoryTests"/>.
/// </summary>
public class RecipeDataLayerTests
{
    private readonly IRecipeRepository _repository = Substitute.For<IRecipeRepository>();
    private readonly IDataTransaction _transaction = Substitute.For<IDataTransaction>();
    private readonly RecipeDataLayer _sut;

    public RecipeDataLayerTests()
    {
        _repository.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_transaction);
        _sut = new RecipeDataLayer(_repository);
    }

    [Fact]
    public async Task DeleteRecipeAsync_reaps_both_orphaned_taxonomies_after_deleting_the_recipe()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteRecipeAsync(7, CancellationToken.None);

        Assert.True(result);
        await _repository.Received(1).DeleteOrphanedCategoriesAsync(Arg.Any<CancellationToken>());
        await _repository.Received(1).DeleteOrphanedTagsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRecipeAsync_reaps_only_after_the_recipe_is_gone_and_commits_last()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        await _sut.DeleteRecipeAsync(7, CancellationToken.None);

        // Order matters twice over: a sweep that ran before the delete would still see this recipe
        // holding its taxonomy, and a commit before the sweeps would put them outside the transaction.
        Received.InOrder(() =>
        {
            _repository.BeginTransactionAsync(Arg.Any<CancellationToken>());
            _repository.DeleteAsync(7, Arg.Any<CancellationToken>());
            _repository.DeleteOrphanedCategoriesAsync(Arg.Any<CancellationToken>());
            _repository.DeleteOrphanedTagsAsync(Arg.Any<CancellationToken>());
            _transaction.CommitAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DeleteRecipeAsync_does_not_leave_the_transaction_open()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        await _sut.DeleteRecipeAsync(7, CancellationToken.None);

        await _transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DeleteRecipeAsync_rolls_back_and_reaps_nothing_when_a_sweep_fails()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        _repository.DeleteOrphanedCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("sweep failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteRecipeAsync(7, CancellationToken.None));

        // The recipe delete must not survive the failure: no commit, and the transaction is disposed
        // (which rolls back), so the store keeps both the recipe and its taxonomy.
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DeleteRecipeAsync_returns_false_and_reaps_nothing_when_recipe_is_missing()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteRecipeAsync(7, CancellationToken.None);

        Assert.False(result);
        // Nothing was removed, so nothing can have been orphaned — neither sweep must run, and there is
        // nothing to commit.
        await _repository.DidNotReceive().DeleteOrphanedCategoriesAsync(Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().DeleteOrphanedTagsAsync(Arg.Any<CancellationToken>());
        await _transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _transaction.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ListAsync_passes_the_filter_through_and_returns_the_repository_summaries()
    {
        var summaries = new List<RecipeSummaryServiceModel>
        {
            new(1, "Soup", "warm", 4, new[] { "Main" }, 3, 2),
        };
        var filter = new RecipeFilter("Main", null);
        _repository.ListAsync(filter, Arg.Any<CancellationToken>()).Returns(summaries);

        var result = await _sut.ListAsync(filter, CancellationToken.None);

        Assert.Same(summaries, result);
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_repository_entity()
    {
        var recipe = new Recipe { Id = 7, Name = "Stew", Servings = 6 };
        _repository.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(recipe);

        Assert.Same(recipe, await _sut.GetByIdAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_the_repository_has_no_recipe()
    {
        _repository.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns((Recipe?)null);

        Assert.Null(await _sut.GetByIdAsync(7, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistsByNameAsync_returns_the_repository_answer(bool exists)
    {
        _repository.ExistsByNameAsync("Bread", Arg.Any<CancellationToken>()).Returns(exists);

        Assert.Equal(exists, await _sut.ExistsByNameAsync("Bread", CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistsWithNameExceptAsync_returns_the_repository_answer(bool exists)
    {
        _repository.ExistsWithNameExceptAsync("Bread", 7, Arg.Any<CancellationToken>()).Returns(exists);

        Assert.Equal(exists, await _sut.ExistsWithNameExceptAsync("Bread", 7, CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_hands_the_entity_to_the_repository_and_returns_the_persisted_one()
    {
        var incoming = new Recipe { Name = "Fresh Bread", Servings = 4 };
        var persisted = new Recipe { Id = 42, Name = "Fresh Bread", Servings = 4 };
        _repository.AddAsync(incoming, Arg.Any<CancellationToken>()).Returns(persisted);

        Assert.Same(persisted, await _sut.AddAsync(incoming, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_hands_the_entity_to_the_repository_and_returns_the_persisted_one()
    {
        var incoming = new Recipe { Name = "Rye Loaf", Servings = 6 };
        var persisted = new Recipe { Id = 7, Name = "Rye Loaf", Servings = 6 };
        _repository.UpdateAsync(7, incoming, Arg.Any<CancellationToken>()).Returns(persisted);

        Assert.Same(persisted, await _sut.UpdateAsync(7, incoming, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_returns_null_when_the_repository_has_no_recipe()
    {
        _repository.UpdateAsync(7, Arg.Any<Recipe>(), Arg.Any<CancellationToken>()).Returns((Recipe?)null);

        Assert.Null(await _sut.UpdateAsync(7, new Recipe { Name = "Ghost" }, CancellationToken.None));
    }
}
