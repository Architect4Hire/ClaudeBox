# RecipeBox — SCRUB Scaffolding Prompts

A sequence of **SCRUB** prompts to scaffold the Aspire + ASP.NET Core + Angular recipe app in
Claude Code, yourself, one controlled step at a time. Each prompt leans on the skills, rules, and
subagents already in your `.claude/` folder.

## How to use these
- Run them **in order**, one at a time. Don't paste the whole file at once.
- Each assumes `CLAUDE.md` (repo root) and the `.claude/` folder are already in place.
- Every prompt asks Claude to **plan first and wait for your approval** before editing — that's
  the single biggest quality lever, so actually read the plan before saying go.
- Use `/clear` between the big prompts to keep context lean; the rules and skills reload on their
  own when relevant.
- After each feature, the prompt hands off to the `code-reviewer` subagent — let it run.

## The reusable SCRUB skeleton
```
SCOPE:        what to build + which part of the repo it touches
CONSTRAINT:   the rules to honor (stack, conventions, plan-first)
RESTRICTION:  explicit "do NOT" guardrails
UTILIZATION:  which skills / subagents / tools to use
BEHAVIOR:     how to proceed — plan, approve, small steps, test, report
```

---

## Prompt 0 — Scaffold the Aspire solution
```
SCOPE: Stand up the RecipeBox solution skeleton only (no business logic). Create an Aspire 13
solution on .NET 10 with these projects under src/: RecipeBox.AppHost (orchestrator),
RecipeBox.ServiceDefaults, and RecipeBox.ApiService (ASP.NET Core Web API). In the AppHost,
declare a local PostgreSQL resource with a database named "recipesdb", register the ApiService,
and register the Angular app in src/client as an npm app so Aspire launches it.

CONSTRAINT: Follow .claude/rules/aspire.md. All resources are local containers. Verify the exact
Aspire commands, template names, and API (AddNpmApp, package names) against https://aspire.dev
before running anything — do not guess.

RESTRICTION: Do NOT add domain models, endpoints, or UI yet. Do NOT hardcode any connection
string or localhost:port. Do NOT add cloud/Azure resources. Do NOT install packages you can't
justify.

UTILIZATION: Use the aspire CLI/templates; use the aspireify skill if available. Use plan mode.

BEHAVIOR: First show me your plan: the exact projects, the AppHost resource wiring, and the
commands you'll run. Wait for my approval. Then scaffold, and finish by running `aspire run` and
telling me what the dashboard shows.
```

---

## Prompt 1 — Domain model + EF Core
```
SCOPE: Add the recipe domain to RecipeBox.ApiService: entities Recipe (name, description,
servings), Ingredient (name, quantity, unit), Step (ordered instruction), and Category/Tag, with
sensible relationships (a Recipe has many Ingredients and ordered Steps; Recipes and Categories
are many-to-many). Register the EF Core DbContext through the Aspire Npgsql integration keyed to
"recipesdb". Create the initial migration.

CONSTRAINT: Follow .claude/rules/backend.md and .claude/rules/aspire.md.

RESTRICTION: The DbContext MUST come from the Aspire integration, not a raw connection string in
appsettings. Do NOT apply the migration yet — create it and stop. Do NOT add API endpoints in
this step.

UTILIZATION: Use plan mode.

BEHAVIOR: Plan the entities and relationships and show me the model before writing. Wait for
approval. Generate the migration, show me the generated file, and stop for review before any
`database update`.
```

---

## Prompt 2 — Recipes API
```
SCOPE: Implement the first endpoints in RecipeBox.ApiService: list recipes (with optional filter
by category), get one recipe with its ingredients and ordered steps, and create a recipe with
ingredients. Include DTOs, a service layer, thin controllers, validation, and tests.

CONSTRAINT: Follow .claude/rules/backend.md.

RESTRICTION: Do NOT expose EF entities across the API boundary — map to DTOs. Do NOT put logic in
controllers. Everything async. Do NOT touch the Angular app.

UTILIZATION: Use the add-endpoint skill for each route. When implementation is done, delegate the
review to the code-reviewer subagent.

BEHAVIOR: Plan the endpoints, DTOs, and validation, and wait for approval. Implement, run
`dotnet test` until green, then run the code-reviewer and summarize its findings for me.
```

---

## Prompt 3 — Angular shell + data service
```
SCOPE: Set up the Angular app in src/client (strict TypeScript, standalone components). Add a
typed RecipeService on HttpClient plus model interfaces that mirror the API DTOs exactly. The
service must read the API base URL from the Aspire-injected config/environment.

CONSTRAINT: Follow .claude/rules/frontend.md and .claude/rules/aspire.md.

RESTRICTION: Do NOT hardcode the API URL. Do NOT call HttpClient from components — only from the
service. No `any`.

UTILIZATION: Use plan mode.

BEHAVIOR: Show me how you'll read the injected API URL and the shape of the models/service before
writing. Wait for approval. Implement, run `ng test`, and report.
```

---

## Prompt 4 — Recipe components
```
SCOPE: Build the core UI: recipe-list (cards + category filter), recipe-detail (ingredients +
ordered steps), recipe-form (create/edit), and a category-filter. Wire them to RecipeService.

CONSTRAINT: Follow .claude/rules/frontend.md.

RESTRICTION: Standalone components only. Use the async pipe (or clean up subscriptions). Models
must match the DTOs. Do NOT bypass RecipeService.

UTILIZATION: Use the new-component skill for each component. Delegate the final review to the
code-reviewer subagent.

BEHAVIOR: Plan the component tree and data flow, wait for approval, implement, run `ng test`, run
the code-reviewer, and summarize.
```

---

## Prompt 5 — End-to-end run + verification
```
SCOPE: Bring the whole system up and verify it works end to end: `aspire run`, then confirm the
dashboard shows Postgres, the API, and the Angular app all healthy, and that the recipe list
loads in the browser from real data. Apply the pending migration if needed.

CONSTRAINT: Follow .claude/rules/aspire.md.

RESTRICTION: Fix only wiring/config issues that block the end-to-end flow. Do NOT add new features
or refactor unrelated code.

UTILIZATION: Use the aspire CLI and dashboard; use the test-gap-analyzer subagent to tell me
what's under-tested before I call this done.

BEHAVIOR: Report a short health summary (each resource, up/down), what you fixed, and the
test-gap-analyzer's prioritized list of missing tests. Ask before applying the migration.
```

---

## Prompt 6 — Stretch: add a cache resource
```
SCOPE: Add a local Redis cache to the AppHost and use it to cache the recipe list, with sensible
invalidation on create/update.

CONSTRAINT: Follow .claude/rules/aspire.md and .claude/rules/backend.md.

RESTRICTION: Local container only. No hardcoded connection details — wire via WithReference and
the Aspire client integration. Keep the AppHost declarative.

UTILIZATION: Use the add-aspire-resource skill. Delegate the review to the code-reviewer subagent.

BEHAVIOR: Plan the cache wiring and invalidation strategy, wait for approval, implement, verify in
the dashboard, run the reviewer, and summarize.
```

---

## Pro tips for driving these
- **Approve the plan, not the code.** The value is in catching a wrong approach before 200 lines
  exist. If the plan is off, correct it and re-plan rather than editing after the fact.
- **One prompt = one clean context.** `/clear` before Prompt 2, 3, 4… so old logs don't crowd the
  window. Your rules and skills reload automatically.
- **Let the guardrails work.** You wrote the "no hardcoded connection strings" rule and taught the
  reviewer to block it — you don't need to repeat it in every prompt, but the RESTRICTION line
  reinforces it when it matters most.
- **When something drifts**, use `/rewind` instead of stacking correction prompts on a polluted
  context.
- **Adapt the skeleton.** For any new task, fill in the five SCRUB lines and point UTILIZATION at
  whichever skill/subagent fits. If no skill fits and you keep repeating a prompt, that's the
  signal to write a new skill.
