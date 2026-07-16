using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>EF mapping for <see cref="Ingredient"/>. Discovered by <c>ApplyConfigurationsFromAssembly</c>.</summary>
public class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> ingredient)
    {
        ingredient.Property(i => i.Name).IsRequired().HasMaxLength(200);
        ingredient.Property(i => i.Quantity).HasPrecision(9, 3);
        ingredient.Property(i => i.Unit).HasMaxLength(50);
    }
}
