using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeBox.ApiService.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeNameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Case-insensitive unique index on recipe name. Authored as raw SQL because EF's fluent
            // API can't express a LOWER(...) functional index; this is the real backstop for the
            // unique-name rule (RecipeRepository translates its violation to a 409). Identifiers are
            // quoted to match EF's default PascalCase table/column casing.
            migrationBuilder.Sql(
                """CREATE UNIQUE INDEX "IX_Recipes_Name_Lower" ON "Recipes" (LOWER("Name"));""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX "IX_Recipes_Name_Lower";""");
        }
    }
}
