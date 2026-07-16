using System.Text;
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
    private readonly FakeRecipeImageStore _images = new();
    private readonly RecipeDataLayer _sut;

    public RecipeDataLayerTests()
    {
        // Run the unit the data layer passes in, the way the real repository would. That the unit is
        // genuinely transactional (and retriable) is the repository's concern — see
        // RecipeRepositoryTests and RecipeRepositoryPostgresTests.
        _repository
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(call.Arg<CancellationToken>()));

        _sut = new RecipeDataLayer(_repository, _images);
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
    public async Task DeleteRecipeAsync_reaps_only_after_the_recipe_is_gone()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        await _sut.DeleteRecipeAsync(7, CancellationToken.None);

        // Order matters: a sweep that ran before the delete would still see this recipe holding its
        // taxonomy, and reap nothing.
        Received.InOrder(() =>
        {
            _repository.DeleteAsync(7, Arg.Any<CancellationToken>());
            _repository.DeleteOrphanedCategoriesAsync(Arg.Any<CancellationToken>());
            _repository.DeleteOrphanedTagsAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DeleteRecipeAsync_puts_the_whole_composition_inside_one_transaction()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        await _sut.DeleteRecipeAsync(7, CancellationToken.None);

        // The delete and both sweeps have to be one atomic unit — a sweep that failed after the recipe
        // was already committed would leave taxonomy no recipe references. Handing the repository the
        // whole unit is also what lets it retry on a transient fault; opening a transaction and
        // running the legs itself is what Npgsql's retrying strategy refuses outright.
        await _repository.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRecipeAsync_lets_a_failed_sweep_take_the_delete_back_with_it()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        _repository.DeleteOrphanedCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("sweep failed"));

        // The throw must escape the unit rather than be swallowed: that's what rolls the transaction
        // back, so the store keeps both the recipe and its taxonomy. That the rollback really happens
        // is pinned end-to-end in RecipeRepositoryTests against a real database.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteRecipeAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRecipeAsync_returns_false_and_reaps_nothing_when_recipe_is_missing()
    {
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteRecipeAsync(7, CancellationToken.None);

        Assert.False(result);
        // Nothing was removed, so nothing can have been orphaned — neither sweep must run.
        await _repository.DidNotReceive().DeleteOrphanedCategoriesAsync(Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().DeleteOrphanedTagsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_passes_the_filter_through_and_returns_the_repository_summaries()
    {
        var summaries = new List<RecipeSummaryServiceModel>
        {
            new(1, "Soup", "warm", 4, new[] { "Main" }, 3, 2, false),
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

    // ── Images ───────────────────────────────────────────────────────────────────────────────────
    // Nothing makes the row and the blob atomic, so the ordering here is the design. These tests pin
    // it, because every failure they describe is silent: the code still returns success.

    private static MemoryStream Bytes(string content = "image-bytes") =>
        new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task SetImageAsync_uploads_the_blob_before_it_points_the_row_at_it()
    {
        _repository.SetImageBlobNameAsync(7, "recipes/7/new.jpg", Arg.Any<CancellationToken>())
            .Returns(new ImageAssignment(true, null));

        await _sut.SetImageAsync(7, "recipes/7/new.jpg", Bytes(), "image/jpeg", CancellationToken.None);

        // Row-first would leave a window where the recipe names bytes that don't exist yet — a broken
        // image for anyone loading the page in between.
        Assert.Contains("recipes/7/new.jpg", _images.Uploaded);
        await _repository.Received(1).SetImageBlobNameAsync(7, "recipes/7/new.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetImageAsync_stores_the_whole_stream_under_the_given_content_type()
    {
        _repository.SetImageBlobNameAsync(7, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ImageAssignment(true, null));

        await _sut.SetImageAsync(7, "recipes/7/new.png", Bytes("the-actual-bytes"), "image/png", CancellationToken.None);

        Assert.Equal("the-actual-bytes", Encoding.UTF8.GetString(_images.BytesOf("recipes/7/new.png")));
        Assert.Equal("image/png", _images.ContentTypeOf("recipes/7/new.png"));
    }

    [Fact]
    public async Task SetImageAsync_never_writes_the_row_when_the_upload_fails()
    {
        _images.UploadFailure = new InvalidOperationException("blob store unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetImageAsync(7, "recipes/7/new.jpg", Bytes(), "image/jpeg", CancellationToken.None));

        // The recipe keeps whatever image it had. Because the new blob name is freshly minted, a failed
        // upload can't have damaged the old blob either.
        await _repository.DidNotReceive()
            .SetImageBlobNameAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetImageAsync_reaps_the_blob_it_replaced()
    {
        _repository.SetImageBlobNameAsync(7, "recipes/7/new.jpg", Arg.Any<CancellationToken>())
            .Returns(new ImageAssignment(true, "recipes/7/old.jpg"));

        await _sut.SetImageAsync(7, "recipes/7/new.jpg", Bytes(), "image/jpeg", CancellationToken.None);

        // Miss this and every re-upload leaks a blob that nothing will ever reference or delete.
        Assert.Contains("recipes/7/old.jpg", _images.Deleted);
        Assert.DoesNotContain("recipes/7/old.jpg", _images.BlobNames);
        Assert.Contains("recipes/7/new.jpg", _images.BlobNames);
    }

    [Fact]
    public async Task SetImageAsync_takes_back_the_blob_when_the_recipe_turns_out_not_to_exist()
    {
        _repository.SetImageBlobNameAsync(7, "recipes/7/new.jpg", Arg.Any<CancellationToken>())
            .Returns(ImageAssignment.RecipeNotFound);

        var result = await _sut.SetImageAsync(7, "recipes/7/new.jpg", Bytes(), "image/jpeg", CancellationToken.None);

        Assert.False(result);
        // Uploading first means a 404 can leave bytes behind. The compensating delete is what stops the
        // container filling up with images for recipes that never existed.
        Assert.Contains("recipes/7/new.jpg", _images.Deleted);
        Assert.Empty(_images.BlobNames);
    }

    [Fact]
    public async Task RemoveImageAsync_clears_the_row_and_deletes_the_blob()
    {
        await _images.UploadAsync("recipes/7/old.jpg", Bytes(), "image/jpeg", CancellationToken.None);
        _repository.SetImageBlobNameAsync(7, null, Arg.Any<CancellationToken>())
            .Returns(new ImageAssignment(true, "recipes/7/old.jpg"));

        var result = await _sut.RemoveImageAsync(7, CancellationToken.None);

        Assert.True(result);
        Assert.Empty(_images.BlobNames);
    }

    [Fact]
    public async Task RemoveImageAsync_returns_false_when_the_recipe_had_no_image()
    {
        _repository.SetImageBlobNameAsync(7, null, Arg.Any<CancellationToken>())
            .Returns(new ImageAssignment(true, null));

        Assert.False(await _sut.RemoveImageAsync(7, CancellationToken.None));
        Assert.Empty(_images.Deleted);
    }

    [Fact]
    public async Task RemoveImageAsync_returns_false_when_the_recipe_is_missing()
    {
        _repository.SetImageBlobNameAsync(7, null, Arg.Any<CancellationToken>())
            .Returns(ImageAssignment.RecipeNotFound);

        Assert.False(await _sut.RemoveImageAsync(7, CancellationToken.None));
        Assert.Empty(_images.Deleted);
    }

    [Fact]
    public async Task OpenImageAsync_reads_the_blob_the_row_names()
    {
        await _images.UploadAsync("recipes/7/img.jpg", Bytes("bytes"), "image/jpeg", CancellationToken.None);
        _repository.GetImageBlobNameAsync(7, Arg.Any<CancellationToken>()).Returns("recipes/7/img.jpg");

        var image = await _sut.OpenImageAsync(7, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal("image/jpeg", image!.ContentType);
    }

    [Fact]
    public async Task OpenImageAsync_returns_null_when_the_recipe_names_no_image()
    {
        _repository.GetImageBlobNameAsync(7, Arg.Any<CancellationToken>()).Returns((string?)null);

        Assert.Null(await _sut.OpenImageAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task OpenImageAsync_returns_null_when_the_named_blob_has_gone_missing()
    {
        // A row can outlive its blob — a compensating delete that raced, say. That's a 404, not a 500.
        _repository.GetImageBlobNameAsync(7, Arg.Any<CancellationToken>()).Returns("recipes/7/vanished.jpg");

        Assert.Null(await _sut.OpenImageAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRecipeAsync_deletes_the_image_blob_outside_the_transaction()
    {
        // A substitute rather than the in-memory fake, because this test is about call *order* and only
        // a substitute records it.
        var images = Substitute.For<IRecipeImageStore>();
        var sut = new RecipeDataLayer(_repository, images);
        _repository.GetImageBlobNameAsync(7, Arg.Any<CancellationToken>()).Returns("recipes/7/img.jpg");
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        await sut.DeleteRecipeAsync(7, CancellationToken.None);

        // Ordering is the point, for two independent reasons. A blob delete inside the transaction
        // can't be rolled back, so a rollback would restore a recipe whose image is already gone. And
        // the unit may be re-run on a transient fault, which would delete the blob twice.
        Received.InOrder(() =>
        {
            _repository.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
            images.DeleteAsync("recipes/7/img.jpg", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DeleteRecipeAsync_keeps_the_image_when_the_delete_rolls_back()
    {
        await _images.UploadAsync("recipes/7/img.jpg", Bytes(), "image/jpeg", CancellationToken.None);
        _repository.GetImageBlobNameAsync(7, Arg.Any<CancellationToken>()).Returns("recipes/7/img.jpg");
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        _repository.DeleteOrphanedCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("sweep failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.DeleteRecipeAsync(7, CancellationToken.None));

        // The recipe survives the rollback, so its image must too — otherwise the store is left holding
        // a live recipe pointing at bytes that no longer exist.
        Assert.Contains("recipes/7/img.jpg", _images.BlobNames);
    }

    [Fact]
    public async Task DeleteRecipeAsync_deletes_no_blob_when_the_recipe_is_missing()
    {
        _repository.GetImageBlobNameAsync(7, Arg.Any<CancellationToken>()).Returns((string?)null);
        _repository.DeleteAsync(7, Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await _sut.DeleteRecipeAsync(7, CancellationToken.None));
        Assert.Empty(_images.Deleted);
    }
}
