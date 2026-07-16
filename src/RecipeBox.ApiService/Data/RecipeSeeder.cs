using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Managers.Models.Domain;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Idempotent development seeder: gives a fresh <c>aspire run</c> a handful of recipes so the
/// list and cards render immediately instead of an empty page. Runs only when the table is empty,
/// so it never fights user-created data or duplicates on restart. Not for production data.
/// </summary>
public static class RecipeSeeder
{
    public static async Task SeedAsync(RecipeDbContext db)
    {
        // Only seed a pristine database; once any recipe exists (seeded or user-created) we leave it alone.
        if (await db.Recipes.AnyAsync())
        {
            return;
        }

        // Shared taxonomy instances — reused across recipes so each Category/Tag is inserted once,
        // honouring the unique-name indexes rather than creating duplicates per recipe.
        var mainCourse = new Category { Name = "Main Course" };
        var dessert = new Category { Name = "Dessert" };
        var breakfast = new Category { Name = "Breakfast" };
        var soup = new Category { Name = "Soup" };

        var quick = new Tag { Name = "quick" };
        var vegetarian = new Tag { Name = "vegetarian" };
        var comfort = new Tag { Name = "comfort" };
        var classic = new Tag { Name = "classic" };

        var recipes = new List<Recipe>
        {
            new()
            {
                Name = "Spaghetti Aglio e Olio",
                Description = "A fast Roman classic: garlic gently fried in good olive oil, tossed with spaghetti and chilli.",
                Servings = 2,
                Categories = { mainCourse },
                Tags = { quick, vegetarian, classic },
                Ingredients =
                {
                    new Ingredient { Name = "Spaghetti", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Garlic", Quantity = 4m, Unit = "cloves" },
                    new Ingredient { Name = "Extra-virgin olive oil", Quantity = 60m, Unit = "ml" },
                    new Ingredient { Name = "Red chilli flakes", Quantity = 1m, Unit = "tsp" },
                    new Ingredient { Name = "Flat-leaf parsley", Quantity = 2m, Unit = "tbsp" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Boil the spaghetti in well-salted water until al dente." },
                    new Step { Order = 2, Instruction = "Gently fry the sliced garlic and chilli flakes in the olive oil until the garlic is pale gold." },
                    new Step { Order = 3, Instruction = "Toss the drained pasta in the oil with a splash of pasta water, then stir through the parsley and serve." },
                },
            },
            new()
            {
                Name = "Classic Pancakes",
                Description = "Fluffy stovetop pancakes for a lazy weekend breakfast.",
                Servings = 4,
                Categories = { breakfast },
                Tags = { quick, vegetarian },
                Ingredients =
                {
                    new Ingredient { Name = "Plain flour", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Milk", Quantity = 300m, Unit = "ml" },
                    new Ingredient { Name = "Egg", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Baking powder", Quantity = 2m, Unit = "tsp" },
                    new Ingredient { Name = "Butter", Quantity = 25m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Whisk the flour, baking powder, milk and egg into a smooth batter." },
                    new Step { Order = 2, Instruction = "Melt a little butter in a hot pan and ladle in the batter." },
                    new Step { Order = 3, Instruction = "Cook until bubbles form on top, then flip and cook the other side until golden." },
                },
            },
            new()
            {
                Name = "Tomato Basil Soup",
                Description = "A silky, slow-simmered tomato soup finished with fresh basil.",
                Servings = 4,
                Categories = { soup },
                Tags = { vegetarian, comfort },
                Ingredients =
                {
                    new Ingredient { Name = "Ripe tomatoes", Quantity = 800m, Unit = "g" },
                    new Ingredient { Name = "Onion", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Garlic", Quantity = 2m, Unit = "cloves" },
                    new Ingredient { Name = "Vegetable stock", Quantity = 500m, Unit = "ml" },
                    new Ingredient { Name = "Fresh basil", Quantity = 1m, Unit = "handful" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Soften the diced onion and garlic in a little oil." },
                    new Step { Order = 2, Instruction = "Add the chopped tomatoes and stock and simmer for 25 minutes." },
                    new Step { Order = 3, Instruction = "Blend until smooth, stir through torn basil, and season to taste." },
                },
            },
            new()
            {
                Name = "Chocolate Brownies",
                Description = "Dense, fudgy brownies with a crackly top.",
                Servings = 9,
                Categories = { dessert },
                Tags = { vegetarian, comfort, classic },
                Ingredients =
                {
                    new Ingredient { Name = "Dark chocolate", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Butter", Quantity = 175m, Unit = "g" },
                    new Ingredient { Name = "Caster sugar", Quantity = 250m, Unit = "g" },
                    new Ingredient { Name = "Eggs", Quantity = 3m, Unit = null },
                    new Ingredient { Name = "Plain flour", Quantity = 100m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Melt the chocolate and butter together, then let cool slightly." },
                    new Step { Order = 2, Instruction = "Whisk the eggs and sugar until pale and thick, then fold in the chocolate and flour." },
                    new Step { Order = 3, Instruction = "Pour into a lined tin and bake at 180°C for about 25 minutes until just set." },
                },
            },
        };

        db.Recipes.AddRange(recipes);
        await db.SaveChangesAsync();
    }
}
