using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeBox.ApiService.Data;

namespace RecipeBox.Tests.Infrastructure;

/// <summary>
/// Boots the API in-process for endpoint tests, with every Aspire-provided backing resource swapped
/// for an in-memory equivalent so tests need no containers: Postgres becomes a shared in-memory
/// SQLite database, and the Redis distributed cache becomes an in-memory one. Dummy connection
/// strings satisfy the Aspire integrations at registration; the real registrations are then replaced
/// before the host resolves them.
/// <para>
/// This type knows nothing about any domain. A domain that needs its own doubles — a blob store, a
/// message bus — overrides <see cref="ConfigureDomainServices"/> and
/// <see cref="ConfigureDomainSettings"/> rather than editing this file.
/// </para>
/// </summary>
public class AppApiFactory : WebApplicationFactory<Program>
{
    // A single kept-open connection keeps the :memory: database alive for the host's lifetime.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseSetting(
            "ConnectionStrings:appdb",
            "Host=localhost;Database=appdb;Username=test;Password=test");
        builder.UseSetting("ConnectionStrings:cache", "localhost:6379");

        ConfigureDomainSettings(builder);

        builder.ConfigureTestServices(services =>
        {
            // Aspire registers AppDbContext as a *pooled* context: dropping the options alone leaves
            // the pool singletons (IDbContextPool<>, IScopedDbContextLease<>) referencing the
            // now-scoped options and scope-validation fails. Remove every AppDbContext-related
            // descriptor, then re-add a plain scoped SQLite context.
            var contextDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var descriptor in contextDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            ConfigureDomainServices(services);
        });
    }

    /// <summary>
    /// Hook for configuration a domain's resources need in order to *register* — connection strings
    /// an Aspire client integration parses at startup, health-check switches, and the like. Runs
    /// before <see cref="ConfigureDomainServices"/>. The base implementation does nothing.
    /// </summary>
    protected virtual void ConfigureDomainSettings(IWebHostBuilder builder)
    {
    }

    /// <summary>
    /// Hook for swapping a domain's own services for test doubles. Runs after the generic database
    /// and cache swaps, inside <c>ConfigureTestServices</c>. The base implementation does nothing.
    /// </summary>
    protected virtual void ConfigureDomainServices(IServiceCollection services)
    {
    }

    /// <summary>Recreates the schema and runs a seed action against a fresh scope.</summary>
    public async Task SeedAsync(Func<AppDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await seed(db);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Reads database state through a fresh scope, for asserting on rows the API does not expose —
    /// tags have no endpoint, so a tag reaped by a delete is otherwise unobservable from a test.
    /// </summary>
    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
