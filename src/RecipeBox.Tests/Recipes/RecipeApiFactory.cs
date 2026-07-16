using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeBox.ApiService.Data;

namespace RecipeBox.Tests.Recipes;

/// <summary>
/// Boots the API in-process for endpoint tests. The Aspire-provided Postgres context and Redis
/// distributed cache are swapped for a shared in-memory SQLite database and an in-memory cache, so
/// tests need no containers. Dummy connection strings satisfy the Aspire integrations at
/// registration; the real registrations are then replaced before the host resolves them.
/// </summary>
public class RecipeApiFactory : WebApplicationFactory<Program>
{
    // A single kept-open connection keeps the :memory: database alive for the host's lifetime.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseSetting(
            "ConnectionStrings:recipesdb",
            "Host=localhost;Database=recipesdb;Username=test;Password=test");
        builder.UseSetting("ConnectionStrings:cache", "localhost:6379");

        builder.ConfigureTestServices(services =>
        {
            // Aspire registers RecipeDbContext as a *pooled* context: dropping the options alone
            // leaves the pool singletons (IDbContextPool<>, IScopedDbContextLease<>) referencing the
            // now-scoped options and scope-validation fails. Remove every RecipeDbContext-related
            // descriptor, then re-add a plain scoped SQLite context.
            var contextDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(RecipeDbContext) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericArguments().Contains(typeof(RecipeDbContext))))
                .ToList();
            foreach (var descriptor in contextDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<RecipeDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });
    }

    /// <summary>Recreates the schema and runs a seed action against a fresh scope.</summary>
    public async Task SeedAsync(Func<RecipeDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await seed(db);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Reads database state through a fresh scope, for asserting on rows the API does not expose —
    /// tags have no endpoint, so a tag reaped by a delete is otherwise unobservable from a test.
    /// </summary>
    public async Task<T> QueryAsync<T>(Func<RecipeDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();
        return await query(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
