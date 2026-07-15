# RecipeBox

A recipe site built to showcase real-world skills with **Aspire + ASP.NET Core + Angular**,
developed with Claude Code. Everything runs **locally**: Aspire's AppHost orchestrates the API,
the Angular app, and all backing resources (Postgres, cache) as local containers — no cloud
dependencies. The Aspire dashboard is the front door for logs, traces, and health.

> "Localized resources" here means *locally orchestrated by Aspire*. If i18n/multi-language
> content is also wanted, that's a separate layer — not assumed here.

## Stack
- **Orchestration:** Aspire 13 (AppHost + ServiceDefaults) on .NET 10
- **Backend:** ASP.NET Core Web API · EF Core (Npgsql) — `src/RecipeBox.ApiService/`
- **Frontend:** Angular (standalone components, strict TS) — `src/client/`, run via `AddJavaScriptApp`
- **Data/infra (local containers via Aspire):** PostgreSQL, and a cache if/when needed

## Layout
```
src/
├── RecipeBox.AppHost/          # Aspire orchestrator — declares every resource
├── RecipeBox.ServiceDefaults/  # shared telemetry, health checks, resilience, discovery
├── RecipeBox.ApiService/       # ASP.NET Core API + EF Core
└── RecipeBox/                     # Angular app (AddJavaScriptApp target)
```

## Commands (use these verbatim)
Whole system (from repo root or the AppHost folder):
- Run everything + dashboard: `aspire run`
- Add an Aspire resource package: `aspire add <resource>`

Backend (from `src/RecipeBox.ApiService/`):
- Test: `dotnet test`
- Add migration: `dotnet ef migrations add <Name>`
- Apply migrations: `dotnet ef database update`

Frontend (from `src/RecipeBox/`):
- Install: `npm install`  ·  Test: `ng test`  ·  Build: `ng build`
- (Don't `ng serve` by hand — Aspire launches it via `AddJavaScriptApp`.)

## Non-negotiable conventions
Area detail loads automatically from `.claude/rules/` (`aspire.md`, `backend.md`, `frontend.md`).
The essentials:
- **Aspire:** every resource is declared in the AppHost. No hardcoded connection strings or
  `localhost:port` — services find each other through service discovery / Aspire-injected config.
- **Backend:** thin controllers, DTOs at the boundary (never expose EF entities), everything
  async, input validated at the edge. The DbContext comes from the Aspire Npgsql integration
  keyed to the AppHost resource name.
- **Frontend:** standalone components, typed models mirroring DTOs, HTTP only through services,
  `async` pipe (no leaked subscriptions).

## How I want you to work
- Plan before any change touching more than one file; wait for approval on non-trivial work.
- Use the matching skill in `.claude/skills/` (`add-endpoint`, `new-component`,
  `add-aspire-resource`) instead of freelancing.
- Run the relevant tests before calling a task done.
- Subagents are read-only — make edits in the main session so I can approve them.

## Don't
- Don't hardcode connection strings/URLs — wire through the AppHost and service discovery.
- Don't put business logic in the AppHost; it stays declarative.
- Don't hand-edit generated EF migrations except to review them.
- Don't commit `bin/`, `obj/`, `node_modules/`, or any secrets.

## The point of this repo
Two things at once: a genuinely good recipe app, and a public demonstration of driving Aspire +
ASP.NET + Angular with Claude Code. The reusable toolkit lives in `.claude/`.
