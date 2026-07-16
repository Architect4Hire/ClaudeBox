using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RecipeBox.ApiService.Data;
using RecipeBox.ApiService.Managers.Models.Domain;
using Xunit;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// The development seeder, run for real against SQLite and the in-memory image store.
/// <para>The load-bearing test here is <see cref="Seeds_an_image_for_every_recipe"/>. Seed images are
/// matched to recipes by a slug of the recipe's name, and nothing in the compiler or the app enforces
/// that: rename a recipe, or add one without adding its photograph, and the image silently disappears
/// from the site with everything still building and running. This is the only thing that notices.</para>
/// </summary>
public class RecipeSeederTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public RecipeSeederTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private RecipeDbContext NewContext()
    {
        var context = new RecipeDbContext(
            new DbContextOptionsBuilder<RecipeDbContext>().UseSqlite(_connection).Options);
        context.Database.EnsureCreated();
        return context;
    }

    private async Task<(FakeRecipeImageStore Images, List<Recipe> Recipes)> SeedAsync()
    {
        var images = new FakeRecipeImageStore();
        await using (var context = NewContext())
        {
            await RecipeSeeder.SeedAsync(context, images, NullLogger.Instance);
        }

        await using var verify = NewContext();
        return (images, await verify.Recipes.ToListAsync());
    }

    [Fact]
    public async Task Seeds_an_image_for_every_recipe()
    {
        var (images, recipes) = await SeedAsync();

        // If this fails, look for a recipe whose name no longer matches its file in Data/SeedImages —
        // the failure message names it.
        var missing = recipes.Where(r => r.ImageBlobName is null).Select(r => r.Name).ToList();
        Assert.True(missing.Count == 0, $"Recipes seeded without an image: {string.Join(", ", missing)}");
        Assert.Equal(recipes.Count, images.BlobNames.Count);
    }

    [Fact]
    public async Task Seeds_the_whole_catalogue()
    {
        var (_, recipes) = await SeedAsync();

        // The list pages at twelve, so the set has to span several pages for the pager to be exercised
        // on a fresh database.
        Assert.Equal(31, recipes.Count);
    }

    [Fact]
    public async Task Seeded_images_are_real_jpegs_that_round_trip()
    {
        var (images, recipes) = await SeedAsync();
        var recipe = recipes.First(r => r.Name == "Tiramisu");

        var image = await images.OpenAsync(recipe.ImageBlobName!, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal("image/jpeg", image!.ContentType);
        // Guards against committing a placeholder, a truncated download, or an HTML error page saved
        // with a .jpg name — all of which would still "seed" without complaint.
        var header = new byte[3];
        await image.Content.ReadExactlyAsync(header);
        Assert.Equal<byte[]>([0xFF, 0xD8, 0xFF], header);
    }

    [Fact]
    public async Task Names_the_seed_blobs_stably_so_re_seeding_does_not_stack_up_copies()
    {
        var (images, recipes) = await SeedAsync();

        // Not a fresh GUID per run, unlike a user upload: an Azurite volume outlives a wiped database,
        // so re-seeding must overwrite its own blobs rather than leave the old set orphaned.
        foreach (var recipe in recipes)
        {
            Assert.Equal($"recipes/{recipe.Id}/seed.jpg", recipe.ImageBlobName);
        }
    }

    [Fact]
    public async Task Does_not_re_seed_the_catalogue_when_recipes_already_exist()
    {
        await using (var context = NewContext())
        {
            context.Recipes.Add(new Recipe { Name = "Someone's Own Recipe", Servings = 1 });
            await context.SaveChangesAsync();
        }

        var images = new FakeRecipeImageStore();
        await using (var context = NewContext())
        {
            await RecipeSeeder.SeedAsync(context, images, NullLogger.Instance);
        }

        await using var verify = NewContext();
        // The row guard is what keeps a restart from duplicating the catalogue over a user's own data.
        Assert.Equal(1, await verify.Recipes.CountAsync());
        // And a recipe nobody committed a photograph for just doesn't get one — no error, no blob.
        Assert.Empty(images.BlobNames);
    }

    [Fact]
    public async Task Backfills_images_onto_a_catalogue_that_already_exists_without_them()
    {
        // The case that shipped broken and that every other test here missed, because they all start
        // from an empty database: any database created before images existed is *not* empty, so the
        // row guard skips it. If image seeding shared that guard, every developer with an existing
        // Postgres volume — which is everyone who ever ran this app — would see 31 placeholders and
        // nothing in the logs to explain it.
        await using (var context = NewContext())
        {
            await RecipeSeeder.SeedAsync(context, new FakeRecipeImageStore(), NullLogger.Instance);
            // Wipe the images, leaving the catalogue: exactly what an upgraded database looks like.
            foreach (var recipe in await context.Recipes.ToListAsync())
            {
                recipe.ImageBlobName = null;
            }
            await context.SaveChangesAsync();
        }

        var images = new FakeRecipeImageStore();
        await using (var context = NewContext())
        {
            await RecipeSeeder.SeedAsync(context, images, NullLogger.Instance);
        }

        await using var verify = NewContext();
        Assert.Equal(31, await verify.Recipes.CountAsync());
        Assert.All(await verify.Recipes.ToListAsync(), r => Assert.NotNull(r.ImageBlobName));
        Assert.Equal(31, images.BlobNames.Count);
    }

    [Fact]
    public async Task Leaves_recipes_that_already_have_an_image_alone()
    {
        var images = new FakeRecipeImageStore();
        await using (var context = NewContext())
        {
            await RecipeSeeder.SeedAsync(context, images, NullLogger.Instance);
        }
        var firstRun = images.Uploaded.Count;

        await using (var context = NewContext())
        {
            await RecipeSeeder.SeedAsync(context, images, NullLogger.Instance);
        }

        // Backfilling must not mean re-uploading the whole set on every startup.
        Assert.Equal(31, firstRun);
        Assert.Equal(firstRun, images.Uploaded.Count);
    }

    [Fact]
    public async Task Seeds_the_recipes_even_when_blob_storage_is_unreachable()
    {
        var images = new FakeRecipeImageStore { UploadFailure = new InvalidOperationException("azurite down") };

        await using (var context = NewContext())
        {
            await RecipeSeeder.SeedAsync(context, images, NullLogger.Instance);
        }

        await using var verify = NewContext();
        // Images are a nicety for local development; the catalogue is the point. A blob store that's
        // still starting up must not leave a developer staring at an empty list.
        Assert.Equal(31, await verify.Recipes.CountAsync());
        Assert.All(await verify.Recipes.ToListAsync(), r => Assert.Null(r.ImageBlobName));
    }
}
