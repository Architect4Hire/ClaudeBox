---
name: add-endpoint
description: >
  Add a new REST endpoint to the RecipeBox ASP.NET Core API using the layered
  controller → facade → business → data architecture. Use whenever creating or extending API
  routes — e.g. "add an endpoint to list recipes by category", "add a POST route to create a
  recipe with ingredients". Produces the controller action, view models, service models, facade
  (validate + cache), business (translate + orchestrate + map), data repository, validators,
  mappers, DI wiring, and tests that match this repo's conventions.
---

# Add an API endpoint

Work in `src/RecipeBox.ApiService/`, organized **type-first**. The orchestration layers —
`Controllers/`, `Facade/`, `Business/` — and the data-access layer, `Data/`, sit at the project root.
The rest of what they lean on lives under the `Managers/` umbrella: validators, models, mappers, and
infrastructure. Stop for review before running migrations.

## Target layout

```
RecipeBox.ApiService/
├── Controllers/            # <Feature>Controller.cs — HTTP surface (ViewModel in, ServiceModel out)
├── Facade/                 # I<Feature>Facade  + <Feature>Facade   (validate VM + cache + return SM)
├── Business/               # I<Feature>Business + <Feature>Business (VM→domain, orchestrate, domain→SM)
├── Data/                   # RecipeDbContext + I<Feature>Repository + <Feature>Repository
├── Managers/
│   ├── Validators/         # FluentValidation validators for the view models
│   ├── Models/
│   │   ├── ViewModels/     # inbound request types — the ONLY thing the controller binds
│   │   ├── ServiceModels/  # outbound response types — the ONLY thing the API returns
│   │   └── Domain/         # EF entities + domain exceptions
│   ├── Mappers/            # VM→domain, domain→ServiceModel (extension methods)
│   └── Infrastructure/     # cross-cutting (e.g. the global exception handler)
├── Migrations/
└── Program.cs
```

## The three model types (this is the core idea)

A request enters as a **ViewModel** and a response leaves as a **ServiceModel** — those are the only
types on the wire. In between, work is done on **Domain** entities. There is no separate DTO layer:
the domain entity *is* the internal shape, so a loaded entity maps directly to a service model.
Nothing leaks — no EF entity ever reaches the controller, and no view model ever reaches the DB.

| Type | Folder | Lives between | Who creates it |
|------|--------|---------------|----------------|
| **ViewModel** | `Managers/Models/ViewModels/` | client → controller → facade | model binder |
| **Domain** entity | `Managers/Models/Domain/` | business ↔ data ↔ EF | business (from the VM) / EF (on load) |
| **ServiceModel** | `Managers/Models/ServiceModels/` | business → facade → controller → client | business (from the entity) |

## The layers (strict responsibilities)

```
Controller  →  Facade              →  Business                       →  Data
  (HTTP:        (validate the VM +      (translate VM→domain, orchestrate,  (persist/query;
   VM in,        cache; return SM)       apply domain rules, domain→SM)      returns entities)
   SM out)
```

- **Controller** (`Controllers/`) — HTTP only: bind the **ViewModel**, call the facade, return an
  `ActionResult<ServiceModel>`. No validation, cache, logic, or data access; never sees an entity.
- **Facade** (`Facade/`) — the boundary: **validates** the ViewModel (via a `Managers/Validators/`
  validator), handles **caching** of ServiceModels (read-through on queries, invalidate on writes),
  and returns ServiceModels. No orchestration, mapping, or EF. Depends on `I<Feature>Business`.
- **Business** (`Business/`) — **orchestrator**: translates the validated **ViewModel → Domain**
  entity, sequences repository calls, applies data-dependent domain rules (e.g. "reject a duplicate
  name"), and maps the returned **Domain entity → ServiceModel**. No validation, caching, or EF.
  Depends on `I<Feature>Repository`.
- **Data** (`Data/`) — **data only**: EF Core queries against the Aspire-provided
  `DbContext`. Detail reads and writes return the **Domain entity**; a **list** read projects
  straight to its summary **ServiceModel** in SQL (counts without materializing child rows — the one
  place data touches an outbound model, to keep the projection). No rules, cache, or validation; may
  translate a DB constraint violation into the domain exception.

Each layer depends on the **interface** of the one below it (`IRecipeFacade` → `IRecipeBusiness` →
`IRecipeRepository`), never on a concrete class or a lower layer's dependencies.

## Steps

1. **ViewModel** → `Managers/Models/ViewModels/`. Define the inbound request type(s) (e.g.
   `CreateRecipeViewModel`). The only shape the controller binds from the wire.

2. **ServiceModel** → `Managers/Models/ServiceModels/`. Define the outbound response type(s) (e.g.
   `RecipeSummaryServiceModel`, `RecipeDetailServiceModel`). The only shape the API returns.

3. **Validator** → `Managers/Validators/`. Add a FluentValidation `AbstractValidator<TViewModel>`
   for each write ViewModel. Shape/format rules only — data-dependent rules that need the DB go in
   business.

4. **Mappers** → `Managers/Mappers/`. Add the two seams you touch: `ViewModel.ToEntity()` (business,
   VM→domain) and `Entity.ToServiceModel()` (business, domain→SM).

5. **Data (`IRecipeRepository` / `RecipeRepository`)** → `Data/`. Add the query/persistence
   method. Detail reads and writes return the **Domain entity** (with the needed `Include`s); a list
   read projects to its summary **ServiceModel** in SQL. Writes accept a Domain entity. May translate
   a unique-index violation into the domain exception. No rules, cache, or validation.

6. **Business (`IRecipeBusiness` / `RecipeBusiness`)** → `Business/`. Add the method the facade
   calls. Detail reads: map the returned **entity → ServiceModel**. List reads: pass the repository's
   projected summaries through. Writes: translate the **ViewModel → Domain** entity, apply
   data-dependent domain rules (throwing the domain exception on violation), call the repository, and
   map the persisted **entity → ServiceModel**. Depends only on `IRecipeRepository`.

7. **Facade (`IRecipeFacade` / `RecipeFacade`)** → `Facade/`. Add the method the controller calls. It
   **validates** the ViewModel with the injected `IValidator<TViewModel>` (the global handler maps
   `ValidationException` → 400), applies **caching** of ServiceModels (read-through on queries;
   invalidate the affected keys on writes), and returns the ServiceModel. Depends on
   `IRecipeBusiness`, the validator, and the cache abstraction. No mapping, orchestration, or EF.

8. **Controller** → `Controllers/<Feature>Controller.cs`. Add a thin action that binds the ViewModel,
   calls the facade, and returns `ActionResult<ServiceModel>`. No logic beyond shaping the response.

9. **DI wiring.** Register the layers in `Program.cs` (scoped), and register validators:
   ```csharp
   builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
   builder.Services.AddScoped<IRecipeBusiness, RecipeBusiness>();
   builder.Services.AddScoped<IRecipeFacade, RecipeFacade>();
   builder.Services.AddValidatorsFromAssemblyContaining<CreateRecipeViewModelValidator>();
   ```

10. **Cache backing.** Use the Aspire Redis client integration for the distributed cache (keyed to
    the AppHost `cache` resource) — no hardcoded connection details. Read-through + invalidate lives
    only in the facade, and it caches ServiceModels.

11. **Tests (per layer, mock the layer below).**
    - **Data:** integration test against a real/containerized Postgres — the query returns the
      expected entities / summary service models.
    - **Business:** unit test with a mocked `IRecipeRepository` — entity→ServiceModel mapping and the
      list pass-through on reads, plus the VM→domain translation and unique-name rule on create.
    - **Facade:** unit test with a mocked `IRecipeBusiness`, a real validator, and an in-memory cache
      — cover a cache **hit**, a cache **miss**, and a **validation failure**.
    - **Endpoint:** integration test (`WebApplicationFactory`) for the happy path plus one
      validation failure — asserting on ServiceModels, posting ViewModels. Run `dotnet test`.

12. **Migration (only if the model changed).** `dotnet ef migrations add <Name>`, review, then
    `dotnet ef database update`. Commit the migration, and confirm
    `dotnet ef migrations has-pending-model-changes` is clean. (If you move a namespace that appears
    in the migration snapshot — a Domain entity or the DbContext — update those strings too.)

## Recipe domain notes
Core entities: `Recipe` (name, description, servings, steps), `Ingredient` (name, quantity,
unit), `Category`/`Tag`. Common routes: list/filter recipes, get one with ingredients + ordered
steps, create/update a recipe, manage categories.

## Checklist before done
- [ ] Files live in the type-first folders above — controller/facade/business/data at the project
      root; validators, models, mappers, infrastructure under `Managers/`
- [ ] Only ViewModels enter and only ServiceModels leave the API — no EF entity crosses the
      controller boundary
- [ ] Controller does HTTP only — no validation, cache, logic, or data access
- [ ] Facade owns validation + caching; no orchestration, mapping, or EF
- [ ] Business translates VM→domain, orchestrates, applies domain rules, maps domain→ServiceModel;
      no validation, cache, or EF
- [ ] Data returns domain entities (list projects to its summary ServiceModel) and does queries
      only; no rules, cache, or validation
- [ ] Each layer depends on the interface below it (`IFacade`→`IBusiness`→`IRepository`)
- [ ] `DbContext` and cache obtained via the Aspire integrations (no hardcoded connection strings)
- [ ] Validation returns the shared error shape on failure
- [ ] Tests per layer pass, incl. facade cache-hit / cache-miss / validation-failure (`dotnet test`)
- [ ] Migration reviewed and committed, and `has-pending-model-changes` is clean (if the model changed)
```
