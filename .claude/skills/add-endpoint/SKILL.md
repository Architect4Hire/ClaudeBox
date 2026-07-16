---
name: add-endpoint
description: >
  Add a new REST endpoint to the RecipeBox ASP.NET Core API using the layered
  controller → facade → business → data layer → repository architecture. Use whenever creating or
  extending API routes — e.g. "add an endpoint to list recipes by category", "add a POST route to
  create a recipe with ingredients". Produces the controller action, view models, service models,
  facade (validate + cache), business (translate + apply rules + map), data layer (compose data
  operations), repository, validators, mappers, DI wiring, and tests that match this repo's
  conventions.
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
├── Business/               # I<Feature>Business + <Feature>Business (VM→domain, domain rules, domain→SM)
├── Data/                   # AppDbContext
│                           #   I<Feature>DataLayer  + <Feature>DataLayer  (compose data operations)
│                           #   I<Feature>Repository + <Feature>Repository (EF queries)
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
Controller  →  Facade              →  Business                →  DataLayer          →  Repository
  (HTTP:        (validate the VM +      (translate VM→domain,      (compose repository    (EF queries;
   VM in,        cache; return SM)       apply domain rules,        calls into whole       returns
   SM out)                               domain→SM)                 data operations)       entities)
```

- **Controller** (`Controllers/`) — HTTP only: bind the **ViewModel**, call the facade, return an
  `ActionResult<ServiceModel>`. No validation, cache, logic, or data access; never sees an entity.
- **Facade** (`Facade/`) — the boundary: **validates** the ViewModel (via a `Managers/Validators/`
  validator), handles **caching** of ServiceModels (read-through on queries, invalidate on writes),
  and returns ServiceModels. No orchestration, mapping, or EF. Depends on `I<Feature>Business`.
- **Business** (`Business/`) — **domain rules and translation**: translates the validated
  **ViewModel → Domain** entity, applies data-dependent domain rules (e.g. "reject a duplicate
  name"), and maps the returned **Domain entity → ServiceModel**. No validation, caching, or EF.
  Depends on `I<Feature>DataLayer`.
- **DataLayer** (`Data/`) — **composes data operations**: turns one logical read or write into
  however many repository calls it takes, so business asks once and does no sequencing (e.g.
  `DeleteRecipeAsync` = delete the recipe, then reap the categories and tags it orphaned). Owns the
  **transaction boundary** for anything it composes: it knows which calls form one operation, so it
  is the only layer that can say where atomicity starts and ends — pass multi-write compositions to
  `ExecuteInTransactionAsync`. Passes an operation straight through when a single
  repository call already *is* the whole operation. Depends on `I<Feature>Repository` — it holds no
  `DbContext`, so every query still belongs to the repository. No rules, mapping, cache, or
  validation.
- **Repository** (`Data/`) — **data only**: EF Core queries against the Aspire-provided `DbContext`,
  plus `ExecuteInTransactionAsync` (the one thing it exposes that isn't a query — it takes the data
  layer's whole operation as a callback and runs it in a transaction, so EF stops here).
  Detail reads and writes return the **Domain entity**; a **list** read projects straight to its
  summary **ServiceModel** in SQL (counts without materializing child rows — the one place data
  touches an outbound model, to keep the projection). Each method is one self-contained data
  operation; sequencing two of them is the data layer's job, not its own. No rules, cache, or
  validation; may translate a DB constraint violation into the domain exception.

Each layer depends on the **interface** of the one below it (`IRecipeFacade` → `IRecipeBusiness` →
`IRecipeDataLayer` → `IRecipeRepository`), never on a concrete class or a lower layer's dependencies.

**Where a rule goes when it's ambiguous.** The split between business and data layer is by *reason*,
not by call count. If the sequencing is a **domain** decision — some rule says what may happen —
it's business (the unique-name check reads before it writes, and that stays in business because
"names are unique" is a rule). If the sequencing is a **persistence** consequence — the store simply
has to be left consistent — it's the data layer (orphaned taxonomy has to be reaped whether or not
anyone asks). Ask what a reviewer would call the extra call: a rule, or bookkeeping.

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

5. **Repository (`IRecipeRepository` / `RecipeRepository`)** → `Data/`. Add the query/persistence
   method. Detail reads and writes return the **Domain entity** (with the needed `Include`s); a list
   read projects to its summary **ServiceModel** in SQL. Writes accept a Domain entity. May translate
   a unique-index violation into the domain exception. Keep each method a single self-contained data
   operation. No rules, cache, or validation.

6. **DataLayer (`IRecipeDataLayer` / `RecipeDataLayer`)** → `Data/`. Add the method business calls,
   composing however many repository calls the operation takes into one. When a single repository
   call already is the whole operation, the method is a one-line pass-through — that is expected, and
   it is still the method business depends on, so the seam holds when the operation later grows a
   second call. **If the composition writes more than once, make it atomic** — hand the whole
   operation to the repository as a callback:
   ```csharp
   var result = await _repository.ExecuteInTransactionAsync(
       async token =>
       {
           // ... the repository calls that make up the operation ...
           return whatever;   // a throw on any leg rolls the whole thing back
       },
       ct);
   ```
   **A callback, not a `BeginTransactionAsync` that hands back a transaction — and that shape is
   forced, not chosen.** Aspire's Npgsql integration enables retry-on-failure, and its execution
   strategy refuses to run inside a transaction the caller opened itself ("does not support
   user-initiated transactions"): it can't replay a unit whose boundaries it doesn't own. Passing the
   whole unit in is what lets the two coexist.

   Two consequences worth internalising: the operation **may run more than once**, so it must be safe
   to repeat; and only work done *through this repository* is rolled back, so anything touching
   another store (blobs, a queue) belongs outside the callback. See
   `RecipeDataLayer.DeleteRecipeAsync` for both.

   Depends only on `IRecipeRepository`; no `DbContext` of its own.

7. **Business (`IRecipeBusiness` / `RecipeBusiness`)** → `Business/`. Add the method the facade
   calls. Detail reads: map the returned **entity → ServiceModel**. List reads: pass the data layer's
   projected summaries through. Writes: translate the **ViewModel → Domain** entity, apply
   data-dependent domain rules (throwing the domain exception on violation), call the data layer, and
   map the persisted **entity → ServiceModel**. Depends only on `IRecipeDataLayer`.

8. **Facade (`IRecipeFacade` / `RecipeFacade`)** → `Facade/`. Add the method the controller calls. It
   **validates** the ViewModel with the injected `IValidator<TViewModel>` (the global handler maps
   `ValidationException` → 400), applies **caching** of ServiceModels (read-through on queries;
   invalidate the affected keys on writes), and returns the ServiceModel. Depends on
   `IRecipeBusiness`, the validator, and the cache abstraction. No mapping, orchestration, or EF.

9. **Controller** → `Controllers/<Feature>Controller.cs`. Add a thin action that binds the ViewModel,
   calls the facade, and returns `ActionResult<ServiceModel>`. No logic beyond shaping the response.

10. **DI wiring.** Register the layers in `Program.cs` (scoped), and register validators:
    ```csharp
    builder.Services.AddScoped<IRecipeRepository, RecipeRepository>();
    builder.Services.AddScoped<IRecipeDataLayer, RecipeDataLayer>();
    builder.Services.AddScoped<IRecipeBusiness, RecipeBusiness>();
    builder.Services.AddScoped<IRecipeFacade, RecipeFacade>();
    ```
    Validators need no registration of their own: `Program.cs` already calls
    `AddValidatorsFromAssemblyContaining<Program>()`, which picks up every validator in the assembly.
    Don't add a second `AddValidatorsFromAssemblyContaining` line per feature.

11. **Cache backing.** Use the Aspire Redis client integration for the distributed cache (keyed to
    the AppHost `cache` resource) — no hardcoded connection details. Read-through + invalidate lives
    only in the facade, and it caches ServiceModels.

12. **Tests (per layer, mock the layer below).**
    - **Repository:** integration test against a real/containerized Postgres — the query returns the
      expected entities / summary service models.
    - **DataLayer:** unit test with a mocked `IRecipeRepository` — for a composed operation, that it
      calls the right repository methods **in the right order**, commits last, short-circuits
      correctly (a delete that found nothing reaps nothing), and does **not** commit when a leg
      throws; for a pass-through, that it delegates and returns the repository's answer unchanged.
      A mocked transaction only proves a commit was *asked for*, so back any atomic composition with
      one **real-database** test that a mid-composition failure leaves the store untouched.
    - **Business:** unit test with a mocked `IRecipeDataLayer` — entity→ServiceModel mapping and the
      list pass-through on reads, plus the VM→domain translation and unique-name rule on create.
    - **Facade:** unit test with a mocked `IRecipeBusiness`, a real validator, and an in-memory cache
      — cover a cache **hit**, a cache **miss**, and a **validation failure**.
    - **Endpoint:** integration test (`WebApplicationFactory`) for the happy path plus one
      validation failure — asserting on ServiceModels, posting ViewModels. Run `dotnet test`.

13. **Migration (only if the model changed).** `dotnet ef migrations add <Name>`, review, then
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
- [ ] Business translates VM→domain, applies domain rules, maps domain→ServiceModel; no validation,
      cache, EF, or multi-call data sequencing
- [ ] DataLayer composes repository calls into whole data operations (pass-throughs where one call
      suffices); no rules, mapping, cache, validation, or `DbContext`
- [ ] Any DataLayer composition that writes more than once is wrapped in a transaction and commits
      only on success
- [ ] Repository returns domain entities (list projects to its summary ServiceModel) and does queries
      only, one self-contained data operation per method; no rules, cache, or validation
- [ ] Each layer depends on the interface below it (`IFacade`→`IBusiness`→`IDataLayer`→`IRepository`)
- [ ] `DbContext` and cache obtained via the Aspire integrations (no hardcoded connection strings)
- [ ] Validation returns the shared error shape on failure
- [ ] Tests per layer pass, incl. facade cache-hit / cache-miss / validation-failure, the data
      layer's call-order/short-circuit assertions for any composed operation, and a real-database
      rollback test for any atomic one (`dotnet test`)
- [ ] Migration reviewed and committed, and `has-pending-model-changes` is clean (if the model changed)
