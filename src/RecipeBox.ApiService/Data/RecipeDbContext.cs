using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Domain;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// EF Core context for the recipe domain. Registered at runtime through the Aspire Npgsql
/// integration keyed to the "recipesdb" resource (see Program.cs) — never a raw connection string.
/// </summary>
public class RecipeDbContext(DbContextOptions<RecipeDbContext> options) : DbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Step> Steps => Set<Step>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Recipe>(recipe =>
        {
            recipe.Property(r => r.Name).IsRequired().HasMaxLength(200);
            recipe.Property(r => r.Description).HasMaxLength(2000);

            // Recipe names are unique case-insensitively. That's enforced by a LOWER(Name) functional
            // unique index (IX_Recipes_Name_Lower) created via raw SQL in the AddRecipeNameUniqueIndex
            // migration — EF's fluent API can't model an expression index, so it isn't declared here.

            // A recipe owns its ingredients and steps: deleting the recipe removes them.
            recipe.HasMany(r => r.Ingredients)
                .WithOne(i => i.Recipe!)
                .HasForeignKey(i => i.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            recipe.HasMany(r => r.Steps)
                .WithOne(s => s.Recipe!)
                .HasForeignKey(s => s.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Two independent taxonomies, each a many-to-many via an EF-managed join table.
            recipe.HasMany(r => r.Categories).WithMany(c => c.Recipes);
            recipe.HasMany(r => r.Tags).WithMany(t => t.Recipes);
        });

        modelBuilder.Entity<Ingredient>(ingredient =>
        {
            ingredient.Property(i => i.Name).IsRequired().HasMaxLength(200);
            ingredient.Property(i => i.Quantity).HasPrecision(9, 3);
            ingredient.Property(i => i.Unit).HasMaxLength(50);
        });

        modelBuilder.Entity<Step>(step =>
        {
            step.Property(s => s.Instruction).IsRequired().HasMaxLength(2000);
            // Steps are ordered 1..n and no two steps in a recipe share a position.
            step.HasIndex(s => new { s.RecipeId, s.Order }).IsUnique();
        });

        modelBuilder.Entity<Category>(category =>
        {
            category.Property(c => c.Name).IsRequired().HasMaxLength(100);
            category.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.Property(t => t.Name).IsRequired().HasMaxLength(100);
            tag.HasIndex(t => t.Name).IsUnique();
        });
    }
}
