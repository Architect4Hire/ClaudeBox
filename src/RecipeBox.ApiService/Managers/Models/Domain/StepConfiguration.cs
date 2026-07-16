using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RecipeBox.ApiService.Managers.Models.Domain;

/// <summary>EF mapping for <see cref="Step"/>. Discovered by <c>ApplyConfigurationsFromAssembly</c>.</summary>
public class StepConfiguration : IEntityTypeConfiguration<Step>
{
    public void Configure(EntityTypeBuilder<Step> step)
    {
        step.Property(s => s.Instruction).IsRequired().HasMaxLength(2000);
        // Steps are ordered 1..n and no two steps in a recipe share a position.
        step.HasIndex(s => new { s.RecipeId, s.Order }).IsUnique();
    }
}
