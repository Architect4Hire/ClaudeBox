---
name: add-endpoint
description: >
  Add a new REST endpoint to the RecipeBox ASP.NET Core API using the layered
  controller → facade → business → data architecture. Use whenever creating or extending API
  routes — e.g. "add an endpoint to list recipes by category", "add a POST route to create a
  recipe with ingredients". Produces the controller action, facade (validation + cache), business
  orchestrator, data repository, DTOs, DI wiring, and tests that match this repo's conventions.
---

# Add an API endpoint

Work in `src/RecipeBox.ApiService/`, **feature-first**: every file for a feature lives together
under `Features/<Feature>/` (e.g. `Features/Recipes/`). Do not scatter layers into top-level
`Controllers/`, `Services/`, or `Repositories/` folders — the feature folder *is* the unit of
organization. Stop for review before running migrations.

## Target layout (create the feature folder if it doesn't exist)

```
Features/<Feature>/
├── <Feature>Controller.cs        # HTTP surface
├── Dtos/                         # request/response DTOs — the ONLY boundary types
├── Models/                       # internal data models (e.g. list projections) — never cross the wire
├── Facade/                       # I<Feature>Facade  + <Feature>Facade   (validate + cache)
├── Business/                     # I<Feature>Business + <Feature>Business (orchestrate)
└── Data/                         # I<Feature>Repository + <Feature>Repository (persist/query)
```

Match this against the existing `Features/Recipes/` folder — new endpoints on an existing feature
extend these files; a new feature creates a parallel `Features/<Feature>/` tree of its own.

## The layers (strict responsibilities)

```
Controller  →  Facade  →  Business  →  Data
  (HTTP)      (validate    (orchestrate   (persist/
              + cache)      only)          query)
```

- **Controller** — HTTP only: bind the request, call the facade, shape the `ActionResult<T>`.
  No validation, no cache, no business logic, no data access.
- **Facade** — the cross-cutting boundary: **validates** the incoming request and handles
  **caching** (read-through on queries, invalidate on writes), maps DTO ↔ model, then calls the
  business layer. No orchestration logic, no data access.
- **Business** — **orchestrator only**: sequences the operation, applies data-dependent domain
  rules, composes results by calling the data layer. No request validation, no caching, no
  `DbContext`/EF.
- **Data** — **data only**: EF Core queries against the Aspire-provided `DbContext`. No business
  logic, no caching, no validation.

Each layer depends on the **interface** of the one below it (`IRecipeFacade` → `IRecipeBusiness`
→ `IRecipeRepository`), never on a concrete class or a lower layer's dependencies.

## Steps

1. **DTOs** → `Features/<Feature>/Dtos/`. Define request/response DTOs (e.g. `RecipeSummaryDto`,
   `CreateRecipeRequest`). DTOs are the only types that cross the API boundary — never expose EF
   entities. Internal shapes (list projections, etc.) go in `Features/<Feature>/Models/`.

2. **Data (`IRecipeRepository` / `RecipeRepository`)** → `Features/<Feature>/Data/`. Add the
   query/persistence method. It uses the Aspire-provided `RecipeDbContext`, runs the EF query, and
   returns entities or data models. Nothing else lives here — no rules, no cache, no validation.

3. **Business (`IRecipeBusiness` / `RecipeBusiness`)** → `Features/<Feature>/Business/`. Add the
   orchestration method. It calls one
   or more repository methods, applies any data-dependent domain rules (e.g. "reject a duplicate
   recipe name" — a check that needs the DB), and composes the result. It depends only on
   `IRecipeRepository`. For simple reads this may be a thin pass-through, and that's fine.

4. **Facade (`IRecipeFacade` / `RecipeFacade`)** → `Features/<Feature>/Facade/`. Add the method the
   controller calls. It:
   - **Validates** the request DTO (FluentValidation or DataAnnotations); on failure returns the
     shared error shape without calling business.
   - **Caches**: for queries, check the cache first and return on hit; on miss call business, then
     store the result. For writes, call business, then invalidate the affected cache keys.
   - Maps request DTO → business input and business result → response DTO.
   - Depends on `IRecipeBusiness` and the cache abstraction. No data access, no orchestration.

5. **Controller** → `Features/<Feature>/<Feature>Controller.cs`. Add a thin action that calls the
   facade and returns a typed `ActionResult<T>`. No logic beyond translating the facade result into
   an HTTP response.

6. **DI wiring.** Register the layers in `Program.cs` (scoped):
   ```csharp
   builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
   builder.Services.AddScoped<IRecipeBusiness, RecipeBusiness>();
   builder.Services.AddScoped<IRecipeFacade, RecipeFacade>();
   ```

7. **Cache backing.** Use the Aspire Redis client integration for the distributed cache (keyed to
   the AppHost `cache` resource) — no hardcoded connection details. Read-through + invalidate lives
   only in the facade.

8. **Tests (per layer, mock the layer below).**
   - **Data:** integration test against a real/containerized Postgres (the query returns what you expect).
   - **Business:** unit test with a mocked `IRecipeRepository` (orchestration + domain rules).
   - **Facade:** unit test with a mocked `IRecipeBusiness` and cache — cover a cache **hit**, a
     cache **miss**, and a **validation failure**.
   - **Endpoint:** integration test (`WebApplicationFactory`) for the happy path plus one
     validation failure. Run `dotnet test`.

9. **Migration (only if the model changed).** `dotnet ef migrations add <Name>`, review, then
   `dotnet ef database update`. Commit the migration.

## Recipe domain notes
Core entities: `Recipe` (name, description, servings, steps), `Ingredient` (name, quantity,
unit), `Category`/`Tag`. Common routes: list/filter recipes, get one with ingredients + ordered
steps, create/update a recipe, manage categories.

## Checklist before done
- [ ] All files live under `Features/<Feature>/` in the layout above — nothing scattered into
      top-level `Controllers/`, `Services/`, or `Repositories/` folders
- [ ] Controller does HTTP only — no validation, cache, logic, or data access
- [ ] Facade owns validation + caching; no orchestration or data access
- [ ] Business orchestrates only; no validation, cache, or `DbContext`/EF
- [ ] Data does queries only; no rules, cache, or validation
- [ ] Each layer depends on the interface below it (`IFacade`→`IBusiness`→`IRepository`)
- [ ] No EF entity crosses the API boundary; DTOs only
- [ ] `DbContext` and cache obtained via the Aspire integrations (no hardcoded connection strings)
- [ ] Validation returns the shared error shape on failure
- [ ] Tests per layer pass, incl. facade cache-hit / cache-miss / validation-failure (`dotnet test`)
- [ ] Migration reviewed and committed (if the model changed)
```
