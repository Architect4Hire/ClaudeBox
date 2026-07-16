using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Managers.Models.Domain;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// EF Core context for the application. Registered at runtime through the Aspire Npgsql
/// integration keyed to the "appdb" resource (see Program.cs) — never a raw connection string.
/// <para>
/// Entity mapping lives in one <see cref="IEntityTypeConfiguration{TEntity}"/> per entity, beside
/// the entities themselves, and is picked up by <c>ApplyConfigurationsFromAssembly</c>. Adding an
/// entity means a DbSet here and a config there — this file never becomes a domain dumping ground.
/// </para>
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Step> Steps => Set<Step>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
