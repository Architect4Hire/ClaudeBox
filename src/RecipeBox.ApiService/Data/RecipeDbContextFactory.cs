using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Design-time factory used ONLY by the <c>dotnet ef</c> tooling to build the model when adding
/// migrations. At runtime the context is always resolved from the Aspire Npgsql integration
/// (see Program.cs); this type is never used there. The placeholder connection string only selects
/// the Npgsql provider so the model can be built — <c>migrations add</c> does not open a connection.
/// </summary>
public class RecipeDbContextFactory : IDesignTimeDbContextFactory<RecipeDbContext>
{
    public RecipeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseNpgsql("Host=localhost;Database=recipesdb;Username=postgres;Password=postgres")
            .Options;

        return new RecipeDbContext(options);
    }
}
