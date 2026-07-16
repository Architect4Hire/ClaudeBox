using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>
/// EF mapping for <see cref="Recipe"/>. Lives beside the entity rather than in the context so the
/// context stays a list of DbSets and each entity's storage shape is one file away from its shape.
/// Discovered by <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> recipe)
    {
        recipe.Property(r => r.Name).IsRequired().HasMaxLength(200);
        recipe.Property(r => r.Description).HasMaxLength(2000);
        // A blob key, not a URL (see Recipe.ImageBlobName). Comfortably wider than the
        // "recipes/{id}/{guid}.jpg" keys the business layer mints.
        recipe.Property(r => r.ImageBlobName).HasMaxLength(200);

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
    }
}
