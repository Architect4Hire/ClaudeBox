---
name: add-endpoint
description: >
  Add a new REST endpoint to the RecipeBox ASP.NET Core API. Use whenever creating or extending
  API routes — e.g. "add an endpoint to list recipes by category", "add a POST route to create a
  recipe with ingredients". Produces controller action, service method, DTOs, validation, and
  tests that match this repo's Aspire/ASP.NET conventions.
---

# Add an API endpoint

Work in `src/RecipeBox.ApiService/`. Stop for review before running migrations.

1. **DTOs.** Define request/response DTOs (e.g. `RecipeSummaryDto`, `CreateRecipeRequest`) in the
   feature's `Dtos/` folder. Never expose EF entities across the API boundary.
2. **Service.** Add the method to the feature's service interface and implementation. Business
   logic and EF queries live here. Everything `async`. Use the Aspire-provided DbContext.
3. **Controller.** Add a thin action: bind the request, call the service, return a typed
   `ActionResult<T>`. No logic beyond shaping the HTTP response.
4. **Validation.** Validate at the boundary. On failure, return the shared error shape.
5. **Tests.** Cover the happy path plus at least one validation failure. Run `dotnet test`.
6. **Migration (only if the model changed).** `dotnet ef migrations add <Name>`, review, then
   `dotnet ef database update`. Commit the migration.

## Recipe domain notes
Core entities: `Recipe` (name, description, servings, steps), `Ingredient` (name, quantity,
unit), `Category`/`Tag`. Common routes: list/filter recipes, get one with ingredients + ordered
steps, create/update a recipe, manage categories.

## Checklist before done
- [ ] Controller thin; logic in the service
- [ ] No EF entity crosses the API boundary
- [ ] DbContext obtained via the Aspire integration (no hardcoded connection string)
- [ ] Input validated; shared error shape on failure
- [ ] Tests pass (`dotnet test`)
- [ ] Migration reviewed and committed (if the model changed)
