using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>EF mapping for <see cref="Tag"/>. Discovered by <c>ApplyConfigurationsFromAssembly</c>.</summary>
public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> tag)
    {
        tag.Property(t => t.Name).IsRequired().HasMaxLength(100);
        tag.HasIndex(t => t.Name).IsUnique();
    }
}
