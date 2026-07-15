# RecipeBox — SCRUB Prompts

Prompts for driving Claude Code on RecipeBox (Aspire + ASP.NET Core + Angular), all wired to the
skills, rules, and subagents in your `.claude/` folder.

Two parts:

- **Part 1 — Scaffolding:** a one-time sequence, run in order, to stand the app up.
- **Part 2 — Operational templates:** reusable prompts for the recurring, high-stakes moments the
  agent won't self-guard (features, migrations, refactors, debugging). Fill in the blanks and go.

Once the app is scaffolded, most day-to-day work needs no bespoke prompt — your rules, skills,
subagents, and hooks carry the structure, so short instructions are enough. Reach for Part 2 only
when a task is non-trivial or risky. And when you reuse one of these two or three times, promote
it to a skill so you stop needing the prompt at all.

## The reusable SCRUB skeleton

```
SCOPE:        what to build/change + which part of the repo it touches
CONSTRAINT:   the rules to honor (stack, conventions, plan-first)
RESTRICTION:  explicit "do NOT" guardrails
UTILIZATION:  which skills / subagents / tools to use
BEHAVIOR:     how to proceed — plan, approve, small steps, test, report
```

## How to use these

- Run the Part 1 prompts **in order**, one at a time. Don't paste the whole file at once.
- Each assumes `CLAUDE.md` (repo root) and the `.claude/` folder are already in place.
- Every prompt asks Claude to **plan first and wait for approval** before editing — read the plan
  before you say go; that's the biggest quality lever.
- Use `/clear` between big steps to keep context lean; rules and skills reload on their own.

---

# Part 1 — Scaffolding (run once, in order)

## Prompt 0 — Scaffold the Aspire solution

```
SCOPE: Stand up the RecipeBox solution skeleton only (no business logic). Create an Aspire 13
solution on .NET 10 with these projects under src/: RecipeBox.AppHost (orchestrator),
RecipeBox.ServiceDefaults, and RecipeBox.ApiService (ASP.NET Core Web API). In the AppHost,
declare a local PostgreSQL resource with a database named "recipesdb", register the ApiService,
and register the Angular app in src/RecipeBox as an npm app so Aspire launches it.

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

# Part 2 — Operational templates (reuse anytime)

These are for the moments the agentic layer won't self-guard. Copy a block, fill the `<...>`,
and run. Delete lines that don't apply.

## Template A — Feature delivery (vertical slice)

*Use for any new capability spanning API + UI. This is your everyday default.*

```
SCOPE: Deliver <feature> end to end: <API change> and <UI change>. Scope is this feature only.

CONSTRAINT: Follow the rules in .claude/rules/. Match existing patterns rather than inventing new
ones.

RESTRICTION: Do NOT change unrelated files, schemas, or public API/DTO contracts. Do NOT add
dependencies without asking. No hardcoded config.

UTILIZATION: Use the add-endpoint and/or new-component skills. Delegate review to the
code-reviewer subagent.

BEHAVIOR: Plan the vertical slice (data → API → UI) and wait for approval. Implement in small
steps, run `dotnet test` and `ng test` green, run the code-reviewer, and summarize.
```

## Template B — Database / migration change

*Use for any schema or data change. Migrations are the closest thing here to irreversible, so the
guardrails are deliberately tight.*

```
SCOPE: Make this schema/data change: <describe>. Produce the EF migration and update the affected
DTOs, queries, and tests.

CONSTRAINT: Follow .claude/rules/backend.md and .claude/rules/aspire.md. The DbContext comes from
the Aspire integration.

RESTRICTION: Create the migration but do NOT run `dotnet ef database update` until I approve. Do
NOT drop or rename columns without an explicit, reversible plan. Do NOT run destructive SQL or
touch existing data without a stated rollback. No raw connection strings.

UTILIZATION: Use plan mode. After the migration is generated, delegate to the code-reviewer (or a
migration-reviewer subagent if you have one).

BEHAVIOR: Show me three things before applying: (1) the model change, (2) the generated migration
file, (3) the rollback story. Wait for approval. Then apply, confirm the schema, and run the
tests.
```

## Template C — Refactor / cross-cutting change

*Use when changing structure without changing behavior. The point is Scope discipline — stopping
"while I'm here" sprawl.*

```
SCOPE: Refactor <target> to <goal>. Behavior must not change. Before editing, list every file you
intend to touch and why.

CONSTRAINT: Follow the rules in .claude/rules/. Keep public API/DTO contracts and test
expectations stable.

RESTRICTION: Do NOT change behavior or unrelated code. Do NOT expand beyond the listed files
without checking in first. No opportunistic edits.

UTILIZATION: Use plan mode; use the Explore subagent to map usages first. Delegate review to the
code-reviewer subagent.

BEHAVIOR: First return the impact map (files + reason). Wait for approval. Refactor in small,
test-green steps — run the suite after each. If the blast radius grows beyond the map, stop and
re-plan with me.
```

## Template D — Debug / harden

*Use to fix a bug (minimal, root-cause) or to harden an area before shipping.*

```
SCOPE: Diagnose and fix <bug/symptom>, and add a regression test.
  (Harden variant: review <area> for correctness, security, and missing tests.)

CONSTRAINT: Follow the rules in .claude/rules/.

RESTRICTION: Make the MINIMAL change that fixes the root cause — do NOT refactor around it or
suppress the symptom without understanding the cause. No new dependencies.

UTILIZATION: Use the Explore subagent to locate the cause; use the test-gap-analyzer for coverage
gaps; run /security-review for the harden variant.

BEHAVIOR: First reproduce the issue and state your root-cause hypothesis with evidence. Wait for
my nod on the diagnosis. Then apply the minimal fix, add a regression test, run the suite, and
summarize what changed and why.
```

---

## Pro tips

- **Approve the plan, not the code.** The value is catching a wrong approach before it exists. If
  the plan is off, correct it and re-plan rather than editing after the fact.
- **One prompt = one clean context.** `/clear` before a new big task so old logs don't crowd the
  window. Rules and skills reload automatically.
- **Let the guardrails work.** You wrote the "no hardcoded connection strings" rule and taught the
  reviewer to block it — the RESTRICTION line just reinforces it when it matters most.
- **Use `/rewind`** instead of stacking correction prompts on a polluted context.
- **Scaffolding is one-time; operational templates are reusable.** Don't write a fresh prompt per
  feature — apply Template A and lean on your skills.
- **Promote repeats to skills.** If you fill in the same operational template two or three times,
  that recurring shape wants to be a skill. Write it, and the prompt disappears.
