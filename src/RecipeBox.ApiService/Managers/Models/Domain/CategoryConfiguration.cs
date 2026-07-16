using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>EF mapping for <see cref="Category"/>. Discovered by <c>ApplyConfigurationsFromAssembly</c>.</summary>
public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> category)
    {
        category.Property(c => c.Name).IsRequired().HasMaxLength(100);
        category.HasIndex(c => c.Name).IsUnique();
    }
}
