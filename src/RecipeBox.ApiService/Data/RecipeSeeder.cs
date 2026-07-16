using System.Text;
using Microsoft.EntityFrameworkCore;
using RecipeBox.ApiService.Managers.Infrastructure;
using RecipeBox.ApiService.Managers.Models.Domain;

namespace RecipeBox.ApiService.Data;

/// <summary>
/// Idempotent development seeder: gives a fresh <c>aspire run</c> a full catalogue of recipes so the
/// list, filters and pagination all render against realistic volume instead of an empty page.
/// Not for production data.
/// <para>The count is deliberate: the list pages at twelve, so the set spans several pages and the
/// pager is exercised on a fresh database.</para>
/// <para>A recipe lives in two stores — a row, and (usually) a blob — and the two are seeded under
/// <em>separate</em> conditions, which is the subtle part. See <see cref="SeedAsync"/>.</para>
/// </summary>
public static class RecipeSeeder
{
    /// <summary>
    /// Seeds the catalogue, then its photographs.
    /// <para>The two steps guard themselves differently, and deliberately. Rows are only written to a
    /// pristine database, so a restart never duplicates the catalogue or fights user-created data.
    /// Images can't use that same "is the database empty" test: every database that predates the
    /// images has recipes in it, so an empty-check would skip the upload forever and the site would
    /// show placeholders on every card with nothing in the logs to explain why. So images backfill
    /// instead — any recipe that has no image, and has a photograph committed for it, gets one.</para>
    /// <para>The cost of backfilling: deleting a seeded recipe's image and restarting brings it back.
    /// That's the price of the seed set repairing itself, and for local development data it's the
    /// right way round.</para>
    /// </summary>
    public static async Task SeedAsync(
        AppDbContext db, IRecipeImageStore images, ILogger logger, CancellationToken ct = default)
    {
        await SeedRecipesAsync(db, ct);
        await SeedImagesAsync(db, images, logger, ct);
    }

    private static async Task SeedRecipesAsync(AppDbContext db, CancellationToken ct)
    {
        // Only seed a pristine database; once any recipe exists (seeded or user-created) we leave it alone.
        if (await db.Recipes.AnyAsync(ct))
        {
            return;
        }

        // Shared taxonomy instances — reused across recipes so each Category/Tag is inserted once,
        // honouring the unique-name indexes rather than creating duplicates per recipe.
        var mainCourse = new Category { Name = "Main Course" };
        var dessert = new Category { Name = "Dessert" };
        var breakfast = new Category { Name = "Breakfast" };
        var soup = new Category { Name = "Soup" };
        var salad = new Category { Name = "Salad" };
        var sideDish = new Category { Name = "Side Dish" };
        var appetizer = new Category { Name = "Appetizer" };

        var quick = new Tag { Name = "quick" };
        var vegetarian = new Tag { Name = "vegetarian" };
        var vegan = new Tag { Name = "vegan" };
        var comfort = new Tag { Name = "comfort" };
        var classic = new Tag { Name = "classic" };
        var spicy = new Tag { Name = "spicy" };
        var onePot = new Tag { Name = "one-pot" };
        var makeAhead = new Tag { Name = "make-ahead" };
        var baked = new Tag { Name = "baked" };
        var glutenFree = new Tag { Name = "gluten-free" };

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
                Tags = { vegetarian, comfort, classic, baked },
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
            new()
            {
                Name = "Chicken Tikka Masala",
                Description = "Yoghurt-marinated chicken grilled hard, then simmered in a spiced tomato and cream sauce.",
                Servings = 4,
                Categories = { mainCourse },
                Tags = { spicy, comfort },
                Ingredients =
                {
                    new Ingredient { Name = "Chicken thighs", Quantity = 700m, Unit = "g" },
                    new Ingredient { Name = "Natural yoghurt", Quantity = 150m, Unit = "ml" },
                    new Ingredient { Name = "Garam masala", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Ginger", Quantity = 20m, Unit = "g" },
                    new Ingredient { Name = "Garlic", Quantity = 4m, Unit = "cloves" },
                    new Ingredient { Name = "Chopped tomatoes", Quantity = 400m, Unit = "g" },
                    new Ingredient { Name = "Double cream", Quantity = 100m, Unit = "ml" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Marinate the diced chicken in the yoghurt, half the garam masala, and the grated ginger and garlic for at least an hour." },
                    new Step { Order = 2, Instruction = "Grill or pan-sear the chicken hard until charred at the edges, then set aside." },
                    new Step { Order = 3, Instruction = "Fry the remaining spices, add the tomatoes, and simmer until thick and darkened." },
                    new Step { Order = 4, Instruction = "Stir in the cream and the chicken and simmer gently for 10 minutes until the sauce clings." },
                },
            },
            new()
            {
                Name = "Beef Chilli con Carne",
                Description = "A long, low simmer of beef, beans and smoky chipotle that only improves overnight.",
                Servings = 6,
                Categories = { mainCourse },
                Tags = { spicy, onePot, makeAhead, comfort },
                Ingredients =
                {
                    new Ingredient { Name = "Beef mince", Quantity = 700m, Unit = "g" },
                    new Ingredient { Name = "Onion", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Chipotle paste", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Ground cumin", Quantity = 2m, Unit = "tsp" },
                    new Ingredient { Name = "Chopped tomatoes", Quantity = 800m, Unit = "g" },
                    new Ingredient { Name = "Kidney beans", Quantity = 400m, Unit = "g" },
                    new Ingredient { Name = "Dark chocolate", Quantity = 20m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Brown the mince hard in batches so it colours rather than steams, then set aside." },
                    new Step { Order = 2, Instruction = "Soften the diced onions, add the cumin and chipotle, and fry until fragrant." },
                    new Step { Order = 3, Instruction = "Return the beef, add the tomatoes, and simmer uncovered for an hour." },
                    new Step { Order = 4, Instruction = "Stir in the drained beans and the chocolate, and simmer 20 minutes more before seasoning." },
                },
            },
            new()
            {
                Name = "Margherita Pizza",
                Description = "Slow-fermented dough, San Marzano tomatoes, fior di latte and basil — nothing else.",
                Servings = 2,
                Categories = { mainCourse },
                Tags = { vegetarian, classic, baked },
                Ingredients =
                {
                    new Ingredient { Name = "Strong white flour", Quantity = 500m, Unit = "g" },
                    new Ingredient { Name = "Water", Quantity = 325m, Unit = "ml" },
                    new Ingredient { Name = "Fresh yeast", Quantity = 2m, Unit = "g" },
                    new Ingredient { Name = "Fine salt", Quantity = 12m, Unit = "g" },
                    new Ingredient { Name = "San Marzano tomatoes", Quantity = 400m, Unit = "g" },
                    new Ingredient { Name = "Fior di latte mozzarella", Quantity = 250m, Unit = "g" },
                    new Ingredient { Name = "Fresh basil", Quantity = 1m, Unit = "handful" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Mix the flour, water, yeast and salt into a shaggy dough and knead until smooth." },
                    new Step { Order = 2, Instruction = "Prove for 8 hours at room temperature, then divide into balls and rest a further 4 hours." },
                    new Step { Order = 3, Instruction = "Stretch each ball by hand, top with crushed tomatoes and torn mozzarella." },
                    new Step { Order = 4, Instruction = "Bake as hot as your oven goes until the crust blisters, then finish with basil and olive oil." },
                },
            },
            new()
            {
                Name = "Thai Green Curry",
                Description = "Fragrant coconut curry with green chilli, lemongrass and Thai basil.",
                Servings = 4,
                Categories = { mainCourse },
                Tags = { spicy, quick },
                Ingredients =
                {
                    new Ingredient { Name = "Green curry paste", Quantity = 3m, Unit = "tbsp" },
                    new Ingredient { Name = "Coconut milk", Quantity = 400m, Unit = "ml" },
                    new Ingredient { Name = "Chicken breast", Quantity = 500m, Unit = "g" },
                    new Ingredient { Name = "Thai aubergines", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Fish sauce", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Palm sugar", Quantity = 1m, Unit = "tsp" },
                    new Ingredient { Name = "Thai basil", Quantity = 1m, Unit = "handful" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Split the coconut milk in a hot pan and fry the curry paste in the released oil until it smells sweet." },
                    new Step { Order = 2, Instruction = "Add the sliced chicken and turn it through the paste to coat." },
                    new Step { Order = 3, Instruction = "Pour in the rest of the coconut milk with the aubergines and simmer until tender." },
                    new Step { Order = 4, Instruction = "Balance with fish sauce and palm sugar, then stir through the Thai basil off the heat." },
                },
            },
            new()
            {
                Name = "Mushroom Risotto",
                Description = "Creamy arborio rice with mixed mushrooms and a hit of dried porcini stock.",
                Servings = 4,
                Categories = { mainCourse },
                Tags = { vegetarian, comfort },
                Ingredients =
                {
                    new Ingredient { Name = "Arborio rice", Quantity = 320m, Unit = "g" },
                    new Ingredient { Name = "Mixed mushrooms", Quantity = 400m, Unit = "g" },
                    new Ingredient { Name = "Dried porcini", Quantity = 20m, Unit = "g" },
                    new Ingredient { Name = "Shallot", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Dry white wine", Quantity = 120m, Unit = "ml" },
                    new Ingredient { Name = "Parmesan", Quantity = 60m, Unit = "g" },
                    new Ingredient { Name = "Butter", Quantity = 40m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Soak the porcini in hot water and keep the liquor as part of your stock." },
                    new Step { Order = 2, Instruction = "Fry the fresh mushrooms hard until browned and set aside, then soften the shallots in the same pan." },
                    new Step { Order = 3, Instruction = "Toast the rice, deglaze with the wine, then add stock a ladle at a time until al dente." },
                    new Step { Order = 4, Instruction = "Beat in the butter and parmesan off the heat, fold the mushrooms back in, and rest 2 minutes before serving." },
                },
            },
            new()
            {
                Name = "Shepherd's Pie",
                Description = "Slow-cooked lamb under a mashed potato crust, browned hard on top.",
                Servings = 6,
                Categories = { mainCourse },
                Tags = { comfort, classic, makeAhead, baked },
                Ingredients =
                {
                    new Ingredient { Name = "Lamb mince", Quantity = 750m, Unit = "g" },
                    new Ingredient { Name = "Carrot", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Onion", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Tomato purée", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Lamb stock", Quantity = 400m, Unit = "ml" },
                    new Ingredient { Name = "Floury potatoes", Quantity = 1m, Unit = "kg" },
                    new Ingredient { Name = "Butter", Quantity = 60m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Brown the lamb, then add the diced carrot and onion and cook until softened." },
                    new Step { Order = 2, Instruction = "Stir in the tomato purée, add the stock, and simmer for 45 minutes until thick." },
                    new Step { Order = 3, Instruction = "Boil and mash the potatoes with the butter until smooth and well seasoned." },
                    new Step { Order = 4, Instruction = "Spread the mash over the lamb, rough up the surface with a fork, and bake at 200°C for 30 minutes until crisp." },
                },
            },
            new()
            {
                Name = "Lemon Herb Roast Chicken",
                Description = "A whole bird roasted over lemon and thyme until the skin shatters.",
                Servings = 4,
                Categories = { mainCourse },
                Tags = { classic, glutenFree },
                Ingredients =
                {
                    new Ingredient { Name = "Whole chicken", Quantity = 1.6m, Unit = "kg" },
                    new Ingredient { Name = "Lemon", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Fresh thyme", Quantity = 6m, Unit = "sprigs" },
                    new Ingredient { Name = "Garlic", Quantity = 1m, Unit = "head" },
                    new Ingredient { Name = "Butter", Quantity = 50m, Unit = "g" },
                    new Ingredient { Name = "Olive oil", Quantity = 2m, Unit = "tbsp" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Dry the bird thoroughly and salt it, ideally the night before, uncovered in the fridge." },
                    new Step { Order = 2, Instruction = "Stuff the cavity with a halved lemon, the thyme, and the halved garlic head." },
                    new Step { Order = 3, Instruction = "Rub with softened butter and oil, then roast at 200°C for about 80 minutes, basting twice." },
                    new Step { Order = 4, Instruction = "Rest for 20 minutes before carving, and squeeze over the remaining lemon." },
                },
            },
            new()
            {
                Name = "Pad Thai",
                Description = "Wok-tossed rice noodles with tamarind, egg, peanuts and lime.",
                Servings = 2,
                Categories = { mainCourse },
                Tags = { quick, spicy },
                Ingredients =
                {
                    new Ingredient { Name = "Flat rice noodles", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Prawns", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Tamarind paste", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Fish sauce", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Palm sugar", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Eggs", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Beansprouts", Quantity = 100m, Unit = "g" },
                    new Ingredient { Name = "Roasted peanuts", Quantity = 40m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Soak the noodles in warm water until pliable but not soft." },
                    new Step { Order = 2, Instruction = "Whisk the tamarind, fish sauce and palm sugar into a sauce." },
                    new Step { Order = 3, Instruction = "Sear the prawns in a screaming wok, push aside, and scramble the eggs alongside." },
                    new Step { Order = 4, Instruction = "Add the noodles and sauce, toss hard, then finish with beansprouts, crushed peanuts and lime." },
                },
            },
            new()
            {
                Name = "Salmon Teriyaki",
                Description = "Salmon fillets glazed in a reduced soy, mirin and sake sauce.",
                Servings = 2,
                Categories = { mainCourse },
                Tags = { quick },
                Ingredients =
                {
                    new Ingredient { Name = "Salmon fillets", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Soy sauce", Quantity = 3m, Unit = "tbsp" },
                    new Ingredient { Name = "Mirin", Quantity = 3m, Unit = "tbsp" },
                    new Ingredient { Name = "Sake", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Caster sugar", Quantity = 1m, Unit = "tbsp" },
                    new Ingredient { Name = "Spring onion", Quantity = 2m, Unit = null },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Pat the salmon dry and sear skin-side down until the skin is crisp." },
                    new Step { Order = 2, Instruction = "Flip briefly, then pour off any excess fat from the pan." },
                    new Step { Order = 3, Instruction = "Add the soy, mirin, sake and sugar and let it bubble to a glaze, spooning it over the fish." },
                    new Step { Order = 4, Instruction = "Serve scattered with sliced spring onion." },
                },
            },
            new()
            {
                Name = "Vegetable Lasagne",
                Description = "Layers of roasted vegetables, tomato sauce and béchamel, baked until bubbling.",
                Servings = 6,
                Categories = { mainCourse },
                Tags = { vegetarian, comfort, makeAhead, baked },
                Ingredients =
                {
                    new Ingredient { Name = "Lasagne sheets", Quantity = 12m, Unit = null },
                    new Ingredient { Name = "Courgette", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Aubergine", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Red pepper", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Chopped tomatoes", Quantity = 800m, Unit = "g" },
                    new Ingredient { Name = "Milk", Quantity = 600m, Unit = "ml" },
                    new Ingredient { Name = "Butter", Quantity = 50m, Unit = "g" },
                    new Ingredient { Name = "Plain flour", Quantity = 50m, Unit = "g" },
                    new Ingredient { Name = "Parmesan", Quantity = 60m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Roast the chopped courgette, aubergine and peppers at 220°C until caramelised at the edges." },
                    new Step { Order = 2, Instruction = "Simmer the tomatoes down to a thick sauce and fold the roasted vegetables through." },
                    new Step { Order = 3, Instruction = "Make a béchamel from the butter, flour and milk, and season it well." },
                    new Step { Order = 4, Instruction = "Layer sauce, pasta and béchamel, finish with parmesan, and bake at 190°C for 40 minutes." },
                },
            },
            new()
            {
                Name = "Huevos Rancheros",
                Description = "Fried eggs on warm tortillas with a smoky ranchero salsa.",
                Servings = 2,
                Categories = { breakfast },
                Tags = { spicy, vegetarian, quick },
                Ingredients =
                {
                    new Ingredient { Name = "Corn tortillas", Quantity = 4m, Unit = null },
                    new Ingredient { Name = "Eggs", Quantity = 4m, Unit = null },
                    new Ingredient { Name = "Chopped tomatoes", Quantity = 400m, Unit = "g" },
                    new Ingredient { Name = "Jalapeño", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Onion", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Black beans", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Coriander", Quantity = 1m, Unit = "handful" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Fry the diced onion and jalapeño, add the tomatoes, and simmer into a loose salsa." },
                    new Step { Order = 2, Instruction = "Warm the black beans through and warm the tortillas in a dry pan." },
                    new Step { Order = 3, Instruction = "Fry the eggs so the whites are set and the yolks still run." },
                    new Step { Order = 4, Instruction = "Stack tortilla, beans, egg and salsa, and finish with coriander." },
                },
            },
            new()
            {
                Name = "Overnight Oats",
                Description = "Oats soaked overnight with yoghurt, chia and fruit — breakfast with no morning effort.",
                Servings = 2,
                Categories = { breakfast },
                Tags = { quick, vegetarian, makeAhead },
                Ingredients =
                {
                    new Ingredient { Name = "Rolled oats", Quantity = 100m, Unit = "g" },
                    new Ingredient { Name = "Milk", Quantity = 200m, Unit = "ml" },
                    new Ingredient { Name = "Natural yoghurt", Quantity = 100m, Unit = "g" },
                    new Ingredient { Name = "Chia seeds", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Honey", Quantity = 1m, Unit = "tbsp" },
                    new Ingredient { Name = "Blueberries", Quantity = 100m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Stir the oats, milk, yoghurt, chia and honey together in a jar." },
                    new Step { Order = 2, Instruction = "Cover and refrigerate overnight so the oats and chia swell." },
                    new Step { Order = 3, Instruction = "Loosen with a splash of milk in the morning and top with the blueberries." },
                },
            },
            new()
            {
                Name = "Eggs Benedict",
                Description = "Poached eggs and ham on a toasted muffin under blender hollandaise.",
                Servings = 2,
                Categories = { breakfast },
                Tags = { classic },
                Ingredients =
                {
                    new Ingredient { Name = "English muffins", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Eggs", Quantity = 4m, Unit = null },
                    new Ingredient { Name = "Egg yolks", Quantity = 3m, Unit = null },
                    new Ingredient { Name = "Butter", Quantity = 150m, Unit = "g" },
                    new Ingredient { Name = "Lemon", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Sliced ham", Quantity = 4m, Unit = "slices" },
                    new Ingredient { Name = "White wine vinegar", Quantity = 1m, Unit = "tbsp" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Blend the yolks with lemon juice, then trickle in the hot melted butter until the hollandaise thickens." },
                    new Step { Order = 2, Instruction = "Poach the eggs in barely simmering water with the vinegar for 3 minutes." },
                    new Step { Order = 3, Instruction = "Toast the split muffins and warm the ham." },
                    new Step { Order = 4, Instruction = "Build muffin, ham and egg, then spoon the hollandaise over generously." },
                },
            },
            new()
            {
                Name = "Banana Bread",
                Description = "The reliable use for over-ripe bananas: moist, dark and barely sweet.",
                Servings = 8,
                Categories = { breakfast, dessert },
                Tags = { vegetarian, baked, makeAhead },
                Ingredients =
                {
                    new Ingredient { Name = "Over-ripe bananas", Quantity = 3m, Unit = null },
                    new Ingredient { Name = "Plain flour", Quantity = 250m, Unit = "g" },
                    new Ingredient { Name = "Light brown sugar", Quantity = 150m, Unit = "g" },
                    new Ingredient { Name = "Butter", Quantity = 115m, Unit = "g" },
                    new Ingredient { Name = "Eggs", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Bicarbonate of soda", Quantity = 1m, Unit = "tsp" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Mash the bananas and beat them into the melted butter, sugar and eggs." },
                    new Step { Order = 2, Instruction = "Fold in the flour and bicarbonate of soda until only just combined." },
                    new Step { Order = 3, Instruction = "Bake in a lined loaf tin at 170°C for 55 minutes until a skewer comes out clean." },
                    new Step { Order = 4, Instruction = "Cool in the tin for 10 minutes before turning out." },
                },
            },
            new()
            {
                Name = "French Onion Soup",
                Description = "Onions caramelised for the better part of an hour, under a raft of melted gruyère.",
                Servings = 4,
                Categories = { soup },
                Tags = { classic, comfort },
                Ingredients =
                {
                    new Ingredient { Name = "Onion", Quantity = 1m, Unit = "kg" },
                    new Ingredient { Name = "Butter", Quantity = 50m, Unit = "g" },
                    new Ingredient { Name = "Dry white wine", Quantity = 150m, Unit = "ml" },
                    new Ingredient { Name = "Beef stock", Quantity = 1m, Unit = "l" },
                    new Ingredient { Name = "Baguette", Quantity = 0.5m, Unit = null },
                    new Ingredient { Name = "Gruyère", Quantity = 150m, Unit = "g" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Cook the thinly sliced onions in the butter over low heat for 45 minutes, stirring, until deep brown." },
                    new Step { Order = 2, Instruction = "Deglaze with the wine and let it reduce away almost completely." },
                    new Step { Order = 3, Instruction = "Add the stock and simmer for 20 minutes, then season." },
                    new Step { Order = 4, Instruction = "Float toasted baguette on each bowl, blanket with gruyère, and grill until blistered." },
                },
            },
            new()
            {
                Name = "Chicken Noodle Soup",
                Description = "A whole-pot cure: poached chicken, its own broth, and plenty of noodles.",
                Servings = 4,
                Categories = { soup },
                Tags = { comfort, onePot },
                Ingredients =
                {
                    new Ingredient { Name = "Chicken thighs", Quantity = 600m, Unit = "g" },
                    new Ingredient { Name = "Chicken stock", Quantity = 1.5m, Unit = "l" },
                    new Ingredient { Name = "Carrot", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Celery", Quantity = 2m, Unit = "sticks" },
                    new Ingredient { Name = "Egg noodles", Quantity = 150m, Unit = "g" },
                    new Ingredient { Name = "Flat-leaf parsley", Quantity = 2m, Unit = "tbsp" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Poach the chicken thighs in the stock for 25 minutes, then lift out and shred." },
                    new Step { Order = 2, Instruction = "Add the sliced carrot and celery to the broth and simmer until tender." },
                    new Step { Order = 3, Instruction = "Add the noodles and cook until just done." },
                    new Step { Order = 4, Instruction = "Return the shredded chicken, season hard, and finish with parsley." },
                },
            },
            new()
            {
                Name = "Butternut Squash Soup",
                Description = "Roasted squash blended with coconut milk and a warm hum of ginger.",
                Servings = 4,
                Categories = { soup },
                Tags = { vegan, vegetarian, glutenFree, makeAhead },
                Ingredients =
                {
                    new Ingredient { Name = "Butternut squash", Quantity = 1m, Unit = "kg" },
                    new Ingredient { Name = "Onion", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Ginger", Quantity = 20m, Unit = "g" },
                    new Ingredient { Name = "Coconut milk", Quantity = 200m, Unit = "ml" },
                    new Ingredient { Name = "Vegetable stock", Quantity = 700m, Unit = "ml" },
                    new Ingredient { Name = "Olive oil", Quantity = 2m, Unit = "tbsp" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Toss the peeled, cubed squash in oil and roast at 200°C for 35 minutes until browned." },
                    new Step { Order = 2, Instruction = "Soften the onion and grated ginger in a large pan." },
                    new Step { Order = 3, Instruction = "Add the roasted squash and stock, simmer 10 minutes, then blend until silky." },
                    new Step { Order = 4, Instruction = "Stir through the coconut milk and season with salt and lime." },
                },
            },
            new()
            {
                Name = "Caesar Salad",
                Description = "Cos lettuce, garlic croutons and an anchovy dressing with real backbone.",
                Servings = 2,
                Categories = { salad },
                Tags = { classic, quick },
                Ingredients =
                {
                    new Ingredient { Name = "Cos lettuce", Quantity = 2m, Unit = "heads" },
                    new Ingredient { Name = "Anchovy fillets", Quantity = 6m, Unit = null },
                    new Ingredient { Name = "Egg yolks", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Garlic", Quantity = 1m, Unit = "clove" },
                    new Ingredient { Name = "Parmesan", Quantity = 50m, Unit = "g" },
                    new Ingredient { Name = "Sourdough", Quantity = 4m, Unit = "slices" },
                    new Ingredient { Name = "Olive oil", Quantity = 100m, Unit = "ml" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Fry torn sourdough in oil with a crushed garlic clove until crisp all over." },
                    new Step { Order = 2, Instruction = "Blend the anchovies, yolks, garlic and half the parmesan, then emulsify in the oil." },
                    new Step { Order = 3, Instruction = "Toss the lettuce with the dressing so every leaf is coated but not drowned." },
                    new Step { Order = 4, Instruction = "Add the croutons and shave the remaining parmesan over the top." },
                },
            },
            new()
            {
                Name = "Greek Salad",
                Description = "Tomatoes, cucumber, olives and a slab of feta — no lettuce in sight.",
                Servings = 4,
                Categories = { salad },
                Tags = { quick, vegetarian, glutenFree },
                Ingredients =
                {
                    new Ingredient { Name = "Ripe tomatoes", Quantity = 500m, Unit = "g" },
                    new Ingredient { Name = "Cucumber", Quantity = 1m, Unit = null },
                    new Ingredient { Name = "Red onion", Quantity = 0.5m, Unit = null },
                    new Ingredient { Name = "Kalamata olives", Quantity = 100m, Unit = "g" },
                    new Ingredient { Name = "Feta", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Dried oregano", Quantity = 1m, Unit = "tsp" },
                    new Ingredient { Name = "Extra-virgin olive oil", Quantity = 60m, Unit = "ml" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Cut the tomatoes into wedges and the cucumber into thick half-moons." },
                    new Step { Order = 2, Instruction = "Combine with the thinly sliced red onion and the olives, and salt lightly." },
                    new Step { Order = 3, Instruction = "Lay the feta on top in one slab, scatter with oregano, and pour the oil over everything." },
                },
            },
            new()
            {
                Name = "Quinoa Tabbouleh",
                Description = "A herb salad first and a grain salad second — quinoa standing in for bulgur.",
                Servings = 4,
                Categories = { salad },
                Tags = { vegan, vegetarian, glutenFree, makeAhead },
                Ingredients =
                {
                    new Ingredient { Name = "Quinoa", Quantity = 150m, Unit = "g" },
                    new Ingredient { Name = "Flat-leaf parsley", Quantity = 100m, Unit = "g" },
                    new Ingredient { Name = "Fresh mint", Quantity = 30m, Unit = "g" },
                    new Ingredient { Name = "Ripe tomatoes", Quantity = 300m, Unit = "g" },
                    new Ingredient { Name = "Spring onion", Quantity = 4m, Unit = null },
                    new Ingredient { Name = "Lemon", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Extra-virgin olive oil", Quantity = 60m, Unit = "ml" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Cook the quinoa, drain well, and spread out to cool completely." },
                    new Step { Order = 2, Instruction = "Chop the parsley and mint finely by hand — never in a processor." },
                    new Step { Order = 3, Instruction = "Combine with the diced tomatoes, sliced spring onion, lemon juice and oil." },
                    new Step { Order = 4, Instruction = "Season and leave 30 minutes for the flavours to come together." },
                },
            },
            new()
            {
                Name = "Garlic Butter Green Beans",
                Description = "Blanched beans finished in foaming garlic butter with toasted almonds.",
                Servings = 4,
                Categories = { sideDish },
                Tags = { quick, vegetarian, glutenFree },
                Ingredients =
                {
                    new Ingredient { Name = "Green beans", Quantity = 400m, Unit = "g" },
                    new Ingredient { Name = "Butter", Quantity = 40m, Unit = "g" },
                    new Ingredient { Name = "Garlic", Quantity = 3m, Unit = "cloves" },
                    new Ingredient { Name = "Flaked almonds", Quantity = 30m, Unit = "g" },
                    new Ingredient { Name = "Lemon", Quantity = 1m, Unit = null },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Blanch the beans in boiling salted water for 3 minutes, then refresh in iced water." },
                    new Step { Order = 2, Instruction = "Toast the almonds in a dry pan until golden and set aside." },
                    new Step { Order = 3, Instruction = "Foam the butter with the sliced garlic, add the beans, and toss until hot through." },
                    new Step { Order = 4, Instruction = "Finish with the almonds and a squeeze of lemon." },
                },
            },
            new()
            {
                Name = "Crispy Roast Potatoes",
                Description = "Parboiled, roughed up, and roasted hard in hot fat until the edges shatter.",
                Servings = 6,
                Categories = { sideDish },
                Tags = { classic, vegan, vegetarian, glutenFree },
                Ingredients =
                {
                    new Ingredient { Name = "Floury potatoes", Quantity = 1.5m, Unit = "kg" },
                    new Ingredient { Name = "Semolina", Quantity = 2m, Unit = "tbsp" },
                    new Ingredient { Name = "Olive oil", Quantity = 150m, Unit = "ml" },
                    new Ingredient { Name = "Garlic", Quantity = 4m, Unit = "cloves" },
                    new Ingredient { Name = "Fresh rosemary", Quantity = 3m, Unit = "sprigs" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Parboil the halved potatoes in salted water for 10 minutes until the edges give." },
                    new Step { Order = 2, Instruction = "Drain, shake them roughly in the colander to fluff the surfaces, and dust with semolina." },
                    new Step { Order = 3, Instruction = "Tip into a tray of smoking-hot oil, turn to coat, and roast at 200°C for 45 minutes." },
                    new Step { Order = 4, Instruction = "Add the garlic and rosemary for the last 10 minutes, then drain and salt." },
                },
            },
            new()
            {
                Name = "Classic Hummus",
                Description = "Chickpeas blended past the point you think is enough, with a lot of tahini.",
                Servings = 6,
                Categories = { appetizer },
                Tags = { vegan, vegetarian, quick, glutenFree, makeAhead },
                Ingredients =
                {
                    new Ingredient { Name = "Chickpeas", Quantity = 400m, Unit = "g" },
                    new Ingredient { Name = "Tahini", Quantity = 120m, Unit = "g" },
                    new Ingredient { Name = "Lemon", Quantity = 2m, Unit = null },
                    new Ingredient { Name = "Garlic", Quantity = 2m, Unit = "cloves" },
                    new Ingredient { Name = "Ground cumin", Quantity = 1m, Unit = "tsp" },
                    new Ingredient { Name = "Extra-virgin olive oil", Quantity = 40m, Unit = "ml" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Simmer the drained chickpeas with a pinch of bicarbonate of soda until they collapse easily." },
                    new Step { Order = 2, Instruction = "Blend the lemon juice and garlic first, then add the tahini and a little iced water until pale." },
                    new Step { Order = 3, Instruction = "Add the warm chickpeas and cumin and blend for a full four minutes until completely smooth." },
                    new Step { Order = 4, Instruction = "Spread into a bowl, pool olive oil in the well, and serve warm." },
                },
            },
            new()
            {
                Name = "Tomato Bruschetta",
                Description = "Grilled bread rubbed with raw garlic under macerated summer tomatoes.",
                Servings = 4,
                Categories = { appetizer },
                Tags = { quick, vegetarian, vegan, classic },
                Ingredients =
                {
                    new Ingredient { Name = "Sourdough", Quantity = 8m, Unit = "slices" },
                    new Ingredient { Name = "Ripe tomatoes", Quantity = 400m, Unit = "g" },
                    new Ingredient { Name = "Garlic", Quantity = 2m, Unit = "cloves" },
                    new Ingredient { Name = "Fresh basil", Quantity = 1m, Unit = "handful" },
                    new Ingredient { Name = "Extra-virgin olive oil", Quantity = 60m, Unit = "ml" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Dice the tomatoes, salt them, and leave 15 minutes to draw out their juice." },
                    new Step { Order = 2, Instruction = "Grill or griddle the bread until charred in stripes." },
                    new Step { Order = 3, Instruction = "Rub each hot slice with a cut garlic clove and drizzle with oil." },
                    new Step { Order = 4, Instruction = "Spoon the tomatoes and their juice over the top and tear basil across." },
                },
            },
            new()
            {
                Name = "Lemon Tart",
                Description = "A sharp, just-set lemon custard in a thin, crisp pastry shell.",
                Servings = 8,
                Categories = { dessert },
                Tags = { classic, baked, makeAhead },
                Ingredients =
                {
                    new Ingredient { Name = "Plain flour", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Butter", Quantity = 100m, Unit = "g" },
                    new Ingredient { Name = "Icing sugar", Quantity = 60m, Unit = "g" },
                    new Ingredient { Name = "Eggs", Quantity = 5m, Unit = null },
                    new Ingredient { Name = "Lemon", Quantity = 4m, Unit = null },
                    new Ingredient { Name = "Caster sugar", Quantity = 200m, Unit = "g" },
                    new Ingredient { Name = "Double cream", Quantity = 150m, Unit = "ml" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Rub the butter into the flour and icing sugar, bind with one egg, and chill for an hour." },
                    new Step { Order = 2, Instruction = "Line the tin, blind-bake at 180°C until sandy and pale gold, then brush with egg wash." },
                    new Step { Order = 3, Instruction = "Whisk the remaining eggs with the caster sugar, lemon juice and zest, then stir in the cream." },
                    new Step { Order = 4, Instruction = "Pour into the warm shell and bake at 140°C for 35 minutes until only just set with a slight wobble." },
                },
            },
            new()
            {
                Name = "Apple Crumble",
                Description = "Sharp bramley apples under a rubble of buttery oat crumble.",
                Servings = 6,
                Categories = { dessert },
                Tags = { comfort, vegetarian, baked, classic },
                Ingredients =
                {
                    new Ingredient { Name = "Bramley apples", Quantity = 1m, Unit = "kg" },
                    new Ingredient { Name = "Caster sugar", Quantity = 80m, Unit = "g" },
                    new Ingredient { Name = "Plain flour", Quantity = 150m, Unit = "g" },
                    new Ingredient { Name = "Rolled oats", Quantity = 60m, Unit = "g" },
                    new Ingredient { Name = "Butter", Quantity = 120m, Unit = "g" },
                    new Ingredient { Name = "Light brown sugar", Quantity = 80m, Unit = "g" },
                    new Ingredient { Name = "Ground cinnamon", Quantity = 1m, Unit = "tsp" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Cook the peeled, chopped apples with the caster sugar and cinnamon until they just start to collapse." },
                    new Step { Order = 2, Instruction = "Rub the butter into the flour, then stir through the oats and brown sugar for a lumpy crumble." },
                    new Step { Order = 3, Instruction = "Pile the crumble loosely over the apples without pressing it down." },
                    new Step { Order = 4, Instruction = "Bake at 190°C for 40 minutes until the fruit bubbles up at the edges." },
                },
            },
            new()
            {
                Name = "Tiramisu",
                Description = "Coffee-soaked savoiardi layered with mascarpone cream and set overnight.",
                Servings = 8,
                Categories = { dessert },
                Tags = { classic, makeAhead },
                Ingredients =
                {
                    new Ingredient { Name = "Savoiardi biscuits", Quantity = 250m, Unit = "g" },
                    new Ingredient { Name = "Mascarpone", Quantity = 500m, Unit = "g" },
                    new Ingredient { Name = "Egg yolks", Quantity = 4m, Unit = null },
                    new Ingredient { Name = "Caster sugar", Quantity = 100m, Unit = "g" },
                    new Ingredient { Name = "Strong espresso", Quantity = 300m, Unit = "ml" },
                    new Ingredient { Name = "Marsala", Quantity = 50m, Unit = "ml" },
                    new Ingredient { Name = "Cocoa powder", Quantity = 2m, Unit = "tbsp" },
                },
                Steps =
                {
                    new Step { Order = 1, Instruction = "Whisk the yolks and sugar over simmering water until thick and pale, then cool." },
                    new Step { Order = 2, Instruction = "Beat in the mascarpone until smooth and slack enough to spread." },
                    new Step { Order = 3, Instruction = "Dip the savoiardi briefly in the espresso and marsala — a second each side, no longer." },
                    new Step { Order = 4, Instruction = "Layer biscuits and cream twice, then chill overnight and dust with cocoa before serving." },
                },
            },
        };

        db.Recipes.AddRange(recipes);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Gives any recipe without an image the photograph committed for it, if there is one.
    /// <para>Matched by filename: a recipe called "Shepherd's Pie" takes <c>shepherds-pie.jpg</c>. That
    /// keeps the mapping out of the recipe list above — nothing here has to be kept in step with a
    /// parallel table of image paths — at the cost of a rename silently dropping an image, which is
    /// why a missing file is logged rather than passed over in silence, and why
    /// <c>RecipeSeederTests.Seeds_an_image_for_every_recipe</c> exists.</para>
    /// <para>Best-effort by design: this is development seed data, and an image that fails to upload
    /// must not stop a fresh <c>aspire run</c> from coming up with a working recipe list. The recipe
    /// simply shows its placeholder.</para>
    /// </summary>
    private static async Task SeedImagesAsync(
        AppDbContext db, IRecipeImageStore images, ILogger logger, CancellationToken ct)
    {
        // Only recipes that have no image: those already carrying one are left alone, so this is a
        // no-op on every run after the first rather than re-uploading 31 blobs at each startup.
        var withoutImages = await db.Recipes.Where(r => r.ImageBlobName == null).ToListAsync(ct);
        if (withoutImages.Count == 0)
        {
            return;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "Data", "SeedImages");
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Seed image directory {Directory} not found; recipes seeded without images.", directory);
            return;
        }

        foreach (var recipe in withoutImages)
        {
            var path = Path.Combine(directory, $"{Slugify(recipe.Name)}.jpg");
            if (!File.Exists(path))
            {
                // Debug, not Warning: now that this backfills rather than running once, the common
                // case for "no file" is a recipe someone created themselves, which is entirely normal
                // and would otherwise warn on every restart forever. Drift between a seed recipe's
                // name and its file is caught by RecipeSeederTests instead, where it's an error.
                logger.LogDebug(
                    "No seed image at {Path} for recipe {Recipe}; it will show the placeholder.",
                    path, recipe.Name);
                continue;
            }

            try
            {
                // A stable, id-scoped name rather than the GUID an upload would mint: re-seeding a wiped
                // database over a surviving Azurite volume then overwrites its own blobs instead of
                // stacking up a new copy of all 31 every time.
                var blobName = $"recipes/{recipe.Id}/seed.jpg";
                await using var content = File.OpenRead(path);
                await images.UploadAsync(blobName, content, RecipeImageFormat.Jpeg, ct);
                recipe.ImageBlobName = blobName;
            }
            catch (Exception ex)
            {
                // Left with no ImageBlobName, so the row stays honest: no image, rather than one that
                // 404s. Swallowed because unreachable blob storage shouldn't take the whole API down
                // on a path that only exists to make local development pleasant.
                logger.LogWarning(ex, "Failed to upload the seed image for recipe {Recipe}.", recipe.Name);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// "Shepherd's Pie" → "shepherds-pie". Apostrophes vanish rather than becoming separators (so it's
    /// not "shepherd-s-pie"); every other non-alphanumeric run collapses to a single hyphen.
    /// </summary>
    private static string Slugify(string name)
    {
        var slug = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (c is not '\'' && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }
}
