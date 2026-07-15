# RecipeBox — 30-Day End-to-End Build Plan

A day-by-day plan to build **RecipeBox** (Aspire + ASP.NET Core + Angular) end to end while
leveling up Claude Code — skills, subagents, hooks, MCP, plugins, and the Agent SDK. Scoped for
~1 focused hour/day; heavier days are flagged so you can split them across a weekend.

Each day gives: **Goal → Do → Verify → CC skill learned → Links.** Commands are the current
happy path; Aspire and Claude Code both ship fast, so treat the linked docs as the source of
truth and confirm exact API names before standardizing.

**Reference hubs (bookmark these):**

- Claude Code docs: https://docs.claude.com/en/docs/claude-code/overview · fast reference: https://code.claude.com/docs
- Aspire docs: https://aspire.dev · What's new in Aspire 13: https://aspire.dev/whats-new/aspire-13/
- Anthropic Academy (free courses): https://anthropic.skilljar.com

---

## How the pieces map (the mental model)

| Layer          | RecipeBox use                                                      | Claude Code primitive                          |
| -------------- | ------------------------------------------------------------------ | ---------------------------------------------- |
| Orchestration  | AppHost declares Postgres, API, Angular; dashboard for logs/traces | `aspire.md` rule + `add-aspire-resource` skill |
| Backend        | ASP.NET Core API + EF Core (Npgsql)                                | `backend.md` rule + `add-endpoint` skill       |
| Frontend       | Angular launched via `AddNpmApp`                                   | `frontend.md` rule + `new-component` skill     |
| Quality gates  | format on save, block secrets                                      | hooks in `settings.json`                       |
| Delegated work | reviews, test-gap analysis                                         | `code-reviewer`, `test-gap-analyzer` subagents |
| External tools | inspect DB, drive the browser                                      | MCP servers (Postgres, Playwright)             |
| Distribution   | bundle the toolkit                                                 | a plugin                                       |
| Automation     | headless audits, CI review                                         | Agent SDK + GitHub Action                      |

---

# Week 1 — Foundations & the Aspire skeleton

## Day 0 — Prerequisites (~30 min, do before Day 1)

**Goal:** a working toolchain.
**Do:**

- Install the **.NET 10 SDK**, **Node.js (LTS)**, and a container runtime (**Docker Desktop** or **Podman**).
- Install the **Aspire CLI**: `curl -sSL https://aspire.dev/install.sh | bash` (macOS/Linux) or `irm https://aspire.dev/install.ps1 | iex` (Windows); then `aspire --version`.
- Install EF tooling: `dotnet tool install --global dotnet-ef`.
- Install **Angular CLI**: `npm i -g @angular/cli`.
- Confirm **Claude Code** works in the VS Code extension.
  **Links:** Aspire setup https://aspire.dev/get-started/ · Angular CLI https://angular.dev/tools/cli

## Day 1 — Repo + the Claude Code brain

**Goal:** a clean repo where Claude Code already knows your conventions.
**Do:**

- Create the repo, drop in your `CLAUDE.md` (root) and `.claude/` folder, `README.md`, `.gitignore`, `docs/`, `src/`.
- Open the repo in VS Code, start Claude Code, run `/init` (then trim the generated file), `/memory` (confirm rules load), and `/context` (see what's loaded).
  **Verify:** `/memory` lists `CLAUDE.md` plus your `.claude/rules/*`.
  **CC skill:** the memory system — root `CLAUDE.md` survives `/compact`; `.claude/rules/*.md` load automatically and can be path-scoped.
  **Links:** Memory https://code.claude.com/docs/en/memory · Overview https://docs.claude.com/en/docs/claude-code/overview

## Day 2 — Scaffold the Aspire solution

**Goal:** AppHost + ServiceDefaults + an empty API, running with a dashboard.
**Do (use SCRUB Prompt 0, plan-first):**

- From `src/`, create the Aspire solution and an ASP.NET Core Web API project. Either use `aspire init` in a solution you create, or start from an Aspire template (`aspire new`) and add a `webapi` project: `dotnet new webapi -o RecipeBox.ApiService`. Ensure `RecipeBox.AppHost` references the API and calls `builder.AddProject<Projects.RecipeBox_ApiService>("api")`.
- Every service should call `builder.AddServiceDefaults();` (telemetry, health, resilience, discovery).
  **Verify:** `aspire run` opens the dashboard and shows `api` healthy.
  **CC skill:** plan mode + permission modes — approve the plan before edits.
  **Links:** Add Aspire to an app https://aspire.dev/get-started/add-aspire-existing-app/ · The `aspireify` Claude Code skill is mentioned there and can wire the AppHost for you.

## Day 3 — Add PostgreSQL (a local, orchestrated resource)

**Goal:** a Postgres container Aspire manages, referenced by the API.
**Do:**

- In the AppHost, add the hosting package `Aspire.Hosting.PostgreSQL`, then:
  
  ```csharp
  var pg = builder.AddPostgres("pg");
  var recipesdb = pg.AddDatabase("recipesdb");
  builder.AddProject<Projects.RecipeBox_ApiService>("api")
         .WithReference(recipesdb)
         .WaitFor(recipesdb);
  ```
  
  Aspire runs `docker.io/library/postgres` with auto-generated credentials — no connection string to manage.
  **Verify:** dashboard shows the `pg` container starting, then `recipesdb`, then `api` waiting for health.
  **CC skill:** letting the `aspire.md` rule enforce "declare resources in the AppHost, no hardcoded strings."
  **Links:** Postgres EF get-started https://aspire.dev/integrations/databases/efcore/postgres/postgresql-get-started/ · Hosting/client reference https://aspire.dev/integrations/databases/efcore/postgres/postgresql-client/

## Day 4 — EF Core model + first migration

**Goal:** the recipe domain in the database.
**Do (SCRUB Prompt 1, plan-first):**

- Add `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` to the API and register the context keyed to the resource name:
  
  ```csharp
  builder.AddNpgsqlDbContext<RecipeDbContext>("recipesdb");
  ```
- Model `Recipe`, `Ingredient`, `Step`, `Category` (Recipe → many Ingredients & ordered Steps; Recipe ↔ Category many-to-many).
- Create the migration: `dotnet ef migrations add Init` (stop and review before applying).
  **Verify:** review the generated migration; `dotnet ef database update` creates the tables (inspect via the dashboard/pgAdmin).
  **CC skill:** `/rewind` and checkpoints — undo a wrong turn instead of stacking corrections.
  **Links:** Connect with EF Core https://aspire.dev/integrations/databases/efcore/postgres/postgresql-connect/ · EF migrations https://learn.microsoft.com/ef/core/managing-schemas/migrations/

## Day 5 — Register the Angular app in Aspire *(heavier day)*

**Goal:** Angular running under Aspire, talking to the API through service discovery.
**Do:**

- `ng new client` inside `src/` (CSS, no SSR to start).
- Add `Aspire.Hosting.NodeJS` to the AppHost and register the app:
  
  ```csharp
  builder.AddNpmApp("web", "../client", "start")
         .WithReference(api)
         .WithHttpEndpoint(env: "PORT")
         .WithExternalHttpEndpoints();
  ```
- Make the `start` script honor the injected `PORT` (Angular doesn't by default). Install `run-script-os` and set:
  
  ```json
  "start": "run-script-os",
  "start:win32": "ng serve --port %PORT%",
  "start:default": "ng serve --port $PORT"
  ```
- Add `client/proxy.conf.js` that proxies `/api` to the API endpoint Aspire injects (env var like `services__api__http__0`). Inspect the exact env var name on the dashboard's resource → Environment tab and read it in the proxy config via `process.env`.
  **Verify:** `aspire run` shows `pg`, `api`, and `web` all healthy; the Angular app loads and can reach `/api`.
  **CC skill:** reading + applying the `frontend.md` and `aspire.md` rules together.
  **Links:** JavaScript apps in Aspire https://aspire.dev/integrations/frameworks/javascript/ · Node/npm apps https://github.com/dotnet/docs-aspire/blob/main/docs/get-started/build-aspire-apps-with-nodejs.md · Angular+Aspire walkthrough (community) https://timdeschryver.dev/blog/how-to-include-an-angular-project-within-net-aspire · Sample repo https://github.com/dotnet/aspire-samples

## Day 6 — First real endpoint (skills deep dive)

**Goal:** `GET /recipes` returning real data, built via your skill.
**Do (SCRUB Prompt 2, partial):**

- Ask Claude to implement `GET /recipes` (list + optional category filter) using the `add-endpoint` skill: DTOs, service, thin controller, validation, tests.
- Read how skills actually fire: description-triggered auto-invocation, `/reload-skills` after edits, and `disallowed-tools` in frontmatter.
  **Verify:** `dotnet test` green; hit the endpoint through the dashboard/Swagger.
  **CC skill:** skills — where they live, how the `description` triggers them, live-reload behavior.
  **Links:** Skills https://code.claude.com/docs/en/skills · Anthropic skills examples https://github.com/anthropics/skills

## Day 7 — Consolidate + first subagent review

**Goal:** clean week-one code, reviewed by a delegate.
**Do:** Run the `code-reviewer` subagent (`@code-reviewer`) over the week's changes; fix blockers. Write a 5-line retro.
**Verify:** reviewer reports no blockers; `aspire run` still green end to end.
**CC skill:** subagents — isolated context, read-only, delegation via `description`.
**Links:** Subagents https://code.claude.com/docs/en/sub-agents · Academy course https://anthropic.skilljar.com/introduction-to-subagents

**Week 1 done when:** `aspire run` brings up Postgres + API + Angular, and `GET /recipes` returns seeded data in the browser.

---

# Week 2 — Build the core app & master skills

## Day 8 — Recipes CRUD

**Goal:** full create/read/update/delete for recipes.
**Do:** Extend with `add-endpoint`: `GET /recipes/{id}`, `POST /recipes`, `PUT /recipes/{id}`, `DELETE /recipes/{id}`. Keep controllers thin, DTOs at the boundary.
**Verify:** `dotnet test`; exercise each route.
**CC skill:** running one skill repeatedly and noticing where it needs sharpening.

## Day 9 — Nested ingredients & ordered steps

**Goal:** a recipe with its ingredients and step order.
**Do:** Model + endpoints for adding/reordering ingredients and steps; handle the many-to-many with categories. Add validation (e.g., a recipe needs ≥1 ingredient).
**Verify:** create a full recipe with ingredients + steps via the API; tests cover the empty-ingredients case.

## Day 10 — Improve the `add-endpoint` skill itself

**Goal:** a skill that pays for itself.
**Do:** Add a helper template (a controller/service/DTO skeleton) the skill references; tighten the `description` so it fires reliably; consider a `context: fork` variant that runs research in an `Explore` agent.
**Verify:** ask for a new endpoint in fresh phrasing and confirm the skill auto-triggers.
**CC skill:** progressive disclosure, skill helper files, description tuning, forked skills.
**Links:** Skills https://code.claude.com/docs/en/skills

## Day 11 — Angular data layer

**Goal:** typed access to the API from Angular.
**Do (SCRUB Prompt 3):** Build `RecipeService` (and `CategoryService`) on `HttpClient`, model interfaces mirroring DTOs, base URL from the Aspire-injected config/proxy — never hardcoded.
**Verify:** a smoke component logs a fetched recipe list.

## Day 12 — Recipe list & detail components

**Goal:** the browse experience.
**Do (SCRUB Prompt 4, partial):** `recipe-list` (cards + filter) and `recipe-detail` (ingredients + ordered steps) via the `new-component` skill; `async` pipe throughout.
**Verify:** `ng test`; click through list → detail in the browser.

## Day 13 — Create/edit + filtering

**Goal:** write path + discovery.
**Do:** `recipe-form` (reactive form, create/edit) and `category-filter`. Surface API validation errors in the form.
**Verify:** create and edit a recipe end to end through the UI.

## Day 14 — Testing & evaluating your skills

**Goal:** confidence, not vibes.
**Do:** `dotnet test` + `ng test` green; run `test-gap-analyzer` and close top gaps; write 3–5 concrete "did it do the right thing" checks for each skill.
**Verify:** a short eval checklist committed under `docs/`.
**CC skill:** treating skills/subagents as things you verify.

**Week 2 done when:** you can browse, view, create, edit, and filter recipes in the browser, backed by the API and Postgres.

---

# Week 3 — Agentic depth: subagents, hooks, MCP

## Day 15 — Built-in subagents & forked skills

**Goal:** use delegation deliberately.
**Do:** Try the built-in `Explore` (fast, read-only) and `Plan` agents on real questions ("where is recipe validation handled?"). Convert one research playbook into a `context: fork` skill.
**CC skill:** when delegation helps vs. hurts; forked-skill pattern.
**Links:** Subagents https://code.claude.com/docs/en/sub-agents · Skills (fork) https://code.claude.com/docs/en/skills

## Day 16 — A second custom subagent

**Goal:** more leverage.
**Do:** Add a narrow subagent — e.g., `migration-reviewer` (flags risky EF migrations) or `api-contract-checker` (DTO ↔ Angular model drift). Keep it read-only.
**Verify:** run it against a real change; the output is specific and actionable.

## Day 17 — Hooks: deterministic guardrails

**Goal:** things that happen no matter what the agent decides.
**Do:** Confirm the `PostToolUse` formatter fires (`dotnet format` / `prettier`). Add a `PreToolUse` secret-guard that blocks writes containing secret-shaped strings (exit code 2 denies the call). Wire both in `settings.json`.
**Verify:** trigger an edit and watch the formatter run; test the guard on a dummy secret.
**CC skill:** hook lifecycle events (`PreToolUse`, `PostToolUse`, `Stop`, `SubagentStop`) and exit-code semantics.
**Links:** Hooks https://code.claude.com/docs/en/hooks

## Day 18 — Add a cache resource (Aspire skill)

**Goal:** a second orchestrated resource, used for real.
**Do:** Use `add-aspire-resource` to add Redis (`AddRedis("cache")`), reference it from the API, and cache the recipe list with invalidation on create/update.
**Verify:** dashboard shows `cache` healthy; repeated list calls hit the cache.
**Links:** Aspire integrations overview https://aspire.dev/integrations/ · Redis https://aspire.dev/integrations/databases/redis/

## Day 19 — MCP: connect external tools

**Goal:** give Claude Code eyes on your data and browser.
**Do:** Connect a **Postgres MCP** (inspect/query `recipesdb`) and a **Playwright MCP** (drive the Angular UI). Manage with `/mcp`. Keep it to a few servers.
**Verify:** ask Claude to query recipe rows via the Postgres MCP and to open the app via Playwright MCP.
**CC skill:** MCP — external tools as native capabilities; trust boundaries.
**Links:** MCP course https://anthropic.skilljar.com/introduction-to-model-context-protocol · Claude Code MCP docs (from the hub) https://code.claude.com/docs

## Day 20 — Browser E2E via MCP

**Goal:** a real end-to-end smoke test.
**Do:** Have Claude use the Playwright MCP to walk the create → view → edit → filter flow and report failures; capture the steps as a Playwright spec you can rerun in CI.
**Verify:** the flow passes headless.
**Links:** Playwright https://playwright.dev/docs/intro

## Day 21 — Orchestration mini-project

**Goal:** all layers, one feature.
**Do:** Ship "search recipes by ingredient": plan mode → `add-endpoint` skill → `code-reviewer` subagent → hooks enforce format/secrets → Playwright MCP smoke. Retro on "right tool, right layer."
**Verify:** feature works end to end and is reviewed.

**Week 3 done when:** the app has caching, guardrail hooks, custom subagents, and MCP-driven inspection + browser tests.

---

# Week 4 — Production polish, plugin, Agent SDK, CI

## Day 22 — Aspire observability

**Goal:** see the system.
**Do:** Explore the dashboard's Traces, Metrics, and Structured logs. Confirm the API's `/health` reflects the Postgres health check; add custom health checks if useful. ServiceDefaults already wires OpenTelemetry.
**Verify:** a traced request shows API → Postgres spans.
**Links:** Aspire dashboard https://aspire.dev/dashboard/overview/

## Day 23 — App polish

**Goal:** it feels finished.
**Do:** Pagination on the list, loading/empty/error states, form UX, and an accessibility pass (labels, focus, contrast) on the Angular side.
**Verify:** rough edges gone; `ng test` green.

## Day 24 — Package a plugin

**Goal:** your toolkit becomes installable.
**Do:** Add `.claude-plugin/plugin.json` and a `marketplace.json`; bundle your skills, subagents, and hooks. Install via `/plugin marketplace add <owner>/<repo>` then `/plugin install recipebox@<marketplace>`.
**Verify:** the plugin installs and its pieces load in a fresh session.
**CC skill:** plugins — one installable unit for skills/subagents/hooks/MCP.
**Links:** Plugins/marketplace (from the hub) https://code.claude.com/docs · Official marketplace https://github.com/anthropics/claude-plugins-official

## Day 25 — Agent SDK: first headless run

**Goal:** Claude Code as a library.
**Do:** `npm install @anthropic-ai/claude-agent-sdk` (or `pip install claude-agent-sdk`). Write a one-shot script that runs over the repo and prints a result (e.g., "list endpoints missing input validation").
**Verify:** the script runs headless and returns useful output.
**CC skill:** the Agent SDK — the same agent loop, embeddable.
**Links:** Claude Code docs hub (Agent SDK section) https://docs.claude.com/en/docs/claude-code/overview

## Day 26 — A useful agentic program

**Goal:** automation you'd rerun.
**Do:** Build an SDK program that emits **structured JSON** — e.g., generate seed recipe data, or scan for DTO/Angular-model drift and output a report. Prompt it to return JSON only, then parse it.
**Verify:** valid JSON you can feed into another step.

## Day 27 — CI: review + tests on every PR

**Goal:** the pipeline does the boring work.
**Do:** Add a GitHub Action that runs Claude Code review on changed files plus `dotnet test` and `ng test`; try `/security-review` locally too. Reuse the Day-20 Playwright spec as an E2E job.
**Verify:** open a PR and watch the checks run.
**CC skill:** headless/CI mode reuses your settings, hooks, and permissions.
**Links:** Claude Code GitHub Actions (from the hub) https://docs.claude.com/en/docs/claude-code · GitHub Actions https://docs.github.com/actions

## Day 28 — Deployment thinking (scoped)

**Goal:** know how it would ship.
**Do:** Generate deployment artifacts with `aspire publish` (e.g., a Docker Compose target) and read the AppHost's publishing manifest. This PoC stays local, but produce the artifacts to show you understand the path.
**Verify:** publish output exists and is coherent.
**Links:** Aspire deployment https://aspire.dev/deployment/overview/

**Week 4 done when:** the toolkit is a plugin, an SDK script automates a real check, and CI reviews + tests every PR.

---

# Capstone

## Day 29 — End-to-end verification + seed + demo

**Goal:** a convincing, runnable demo.
**Do:** Seed a dozen real recipes; run the full system with `aspire run`; execute the Playwright smoke; fix any gaps; capture a short screen recording of the dashboard + app.
**Verify:** a fresh clone runs with documented steps.

## Day 30 — Documentation & portfolio polish

**Goal:** the "built with Claude Code" story lands.
**Do:** Flesh out `README` with an architecture diagram, screenshots/GIF, and a link to `docs/scrub-scaffolding-prompts.md` showing exactly how it was built. Write a one-page operating manual mapping SCRUB → your `.claude/` toolkit. Publish.
**Verify:** a stranger could clone, run, and understand the build approach in 5 minutes.

---

## Definition of done (whole project)

- `aspire run` brings up Postgres, Redis, the API, and Angular — all healthy on the dashboard.
- Recipes: browse, filter, view (with ingredients + ordered steps), create, edit, delete, search.
- Caching with invalidation; guardrail hooks; custom subagents; MCP inspection + Playwright E2E.
- A `.claude/` plugin, an Agent SDK automation, and CI that reviews + tests PRs.
- A README that tells the Aspire + Claude Code + SCRUB story with a diagram and a demo.

## Consolidated links

**Aspire:** get-started https://aspire.dev/get-started/ · Aspire 13 https://aspire.dev/whats-new/aspire-13/ · Postgres EF https://aspire.dev/integrations/databases/efcore/postgres/postgresql-get-started/ · JS/Angular https://aspire.dev/integrations/frameworks/javascript/ · Node apps https://github.com/dotnet/docs-aspire/blob/main/docs/get-started/build-aspire-apps-with-nodejs.md · dashboard https://aspire.dev/dashboard/overview/ · deploy https://aspire.dev/deployment/overview/
**Claude Code:** overview https://docs.claude.com/en/docs/claude-code/overview · reference https://code.claude.com/docs · memory https://code.claude.com/docs/en/memory · skills https://code.claude.com/docs/en/skills · subagents https://code.claude.com/docs/en/sub-agents · hooks https://code.claude.com/docs/en/hooks
**Learning:** Academy https://anthropic.skilljar.com · subagents course https://anthropic.skilljar.com/introduction-to-subagents · MCP course https://anthropic.skilljar.com/introduction-to-model-context-protocol
**Stack:** Angular https://angular.dev · EF Core https://learn.microsoft.com/ef/core/ · Playwright https://playwright.dev/docs/intro

> Reminder: Aspire and Claude Code both move fast. If a command or API name here doesn't match
> what you see, trust the linked official docs — and let your `.claude/` rules and the
> `code-reviewer` subagent catch drift as you go.
