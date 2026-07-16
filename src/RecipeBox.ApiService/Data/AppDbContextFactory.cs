using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Design-time factory used ONLY by the <c>dotnet ef</c> tooling to build the model when adding
/// migrations. At runtime the context is always resolved from the Aspire Npgsql integration
/// (see Program.cs); this type is never used there.
/// <para>
/// It prefers the Aspire-injected connection string (the <c>ConnectionStrings__appdb</c>
/// environment variable) so <c>dotnet ef database update</c> targets the running Postgres while
/// <c>aspire run</c> is up. When that variable is absent — e.g. <c>migrations add</c>, which never
/// opens a connection — it falls back to a placeholder that only selects the Npgsql provider so the
/// model can still be built.
/// </para>
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string PlaceholderConnectionString =
        "Host=localhost;Database=appdb;Username=postgres;Password=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__appdb")
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
