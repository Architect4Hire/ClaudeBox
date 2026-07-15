# RecipeBox

*Project memory, written as a SCRUB prompt — Scope, Constraints, Restrictions, Usage, Behavior. Loaded every session. Every new rule has one obvious home, and every misstep is diagnosable by section.*

## Scope

RecipeBox is a recipe site built to showcase real-world skills with **Aspire + ASP.NET Core + Angular**, developed with Claude Code. It does two things at once: it's a genuinely good recipe app, and a public demonstration of driving an Aspire + ASP.NET + Angular stack with Claude Code. The reusable toolkit lives in `.claude/`.

- **In bounds:** the recipe app and the `.claude/` toolkit that builds it.
- **Out of bounds (don't build unprompted):** i18n / multi-language content. "Localized resources" here means *locally orchestrated by Aspire*, nothing more — if multi-language is ever wanted, that's a separate layer, not assumed here.

## Constraints

**Stack**

- **Orchestration:** Aspire 13 (AppHost + ServiceDefaults) on .NET 10
- **Backend:** ASP.NET Core Web API · EF Core (Npgsql) — `src/RecipeBox.ApiService/`
- **Frontend:** Angular (standalone components, strict TS) — `src/RecipeBox/`, run via `AddJavaScriptApp`
- **Data/infra (local containers via Aspire):** PostgreSQL, and a cache if/when needed

**Layout**

```
src/
├── RecipeBox.AppHost/          # Aspire orchestrator — declares every resource
├── RecipeBox.ServiceDefaults/  # shared telemetry, health checks, resilience, discovery
├── RecipeBox.ApiService/       # ASP.NET Core API + EF Core
└── RecipeBox/                  # Angular app (AddJavaScriptApp target)
```

**Architecture conventions** — area detail auto-loads from `.claude/rules/` (`aspire.md`, `backend.md`, `frontend.md`). The essentials:

- **Aspire:** every resource is declared in the AppHost. The `DbContext` comes from the Aspire Npgsql integration, keyed to the AppHost resource name.
- **Backend:** thin controllers, DTOs at the boundary (never expose EF entities), everything async, input validated at the edge.
- **Frontend:** standalone components, typed models mirroring DTOs, HTTP only through services, `async` pipe (no leaked subscriptions).

**Canonical commands** (use these verbatim)

- Whole system (repo root or the AppHost folder): run everything + dashboard `aspire run` · add a resource package `aspire add <resource>`
- Backend (`src/RecipeBox.ApiService/`): `dotnet test` · `dotnet ef migrations add <Name>` · `dotnet ef database update`
- Frontend (`src/RecipeBox/`): `npm install` · `ng test` · `ng build`

## Restrictions

- Don't hardcode connection strings or `localhost:port` — wire through the AppHost and service discovery / Aspire-injected config.
- Don't put business logic in the AppHost; it stays declarative.
- Don't run `ng serve` by hand — Aspire launches the client via `AddJavaScriptApp`.
- Don't hand-edit generated EF migrations except to review them.
- Don't commit `bin/`, `obj/`, `node_modules/`, or any secrets.

## Usage

- The world is **local**: Aspire's AppHost orchestrates the API, the Angular app, and all backing resources (Postgres, cache) as local containers — no cloud dependencies. The Aspire dashboard is the front door for logs, traces, and health.
- Services find each other through service discovery / Aspire-injected config — never hardcoded addresses.
- The Angular app is the primary consumer of the API — keep the contract stable.
- Available tooling in `.claude/`: rules auto-load from `.claude/rules/`; task skills live in `.claude/skills/` (`add-endpoint`, `new-component`, `add-aspire-resource`); subagents are available but run **read-only**.

## Behavior

- Plan before any change touching more than one file; wait for approval on non-trivial work.
- Use the matching skill in `.claude/skills/` instead of freelancing.
- Run the relevant tests before calling a task done.
- Make edits in the main session so I can approve them — subagents stay read-only.