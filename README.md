# 🍳 RecipeBox

A recipe site built with **Aspire + ASP.NET Core + Angular**, developed hand-in-hand with
**Claude Code**. Everything runs locally: Aspire's AppHost orchestrates the API, the Angular app,
and all backing resources (PostgreSQL, cache) as local containers — no cloud dependencies.

This repo is two things at once: a genuinely good full-stack app, and a public, reproducible
demonstration of driving a real .NET stack agentically with Claude Code.

## Stack
- **Orchestration:** Aspire 13 (AppHost + ServiceDefaults) on .NET 10
- **Backend:** ASP.NET Core Web API · EF Core (Npgsql)
- **Frontend:** Angular (standalone components, strict TypeScript)
- **Local infra (Aspire-managed containers):** PostgreSQL, and a cache when needed

## Repo layout
```
.
├── CLAUDE.md          # project constitution — auto-loaded by Claude Code every session
├── .claude/           # the Claude Code toolkit: skills, subagents, hooks, rules
├── docs/              # the plan and the prompts this repo was built with
├── src/               # the application (scaffolded via the prompts in docs/)
├── .gitignore
└── README.md
```

## How this repo was built
The application in `src/` is scaffolded by driving Claude Code with a set of **SCRUB** prompts —
one controlled, plan-first step at a time. The full sequence is in
[`docs/scrub-scaffolding-prompts.md`](docs/scrub-scaffolding-prompts.md); the broader learning
arc is in [`docs/30-day-claude-code-plan.md`](docs/30-day-claude-code-plan.md).

The `.claude/` folder is what makes those prompts work: reusable **skills** (`add-endpoint`,
`new-component`, `add-aspire-resource`), read-only **subagents** (`code-reviewer`,
`test-gap-analyzer`), a formatting **hook**, and path-scoped **rules** for Aspire, the backend,
and the frontend. See [`.claude/README.md`](.claude/README.md) for what each piece does.

## Getting started
1. Prerequisites: .NET 10 SDK, the Aspire CLI, Node.js, and a container runtime (Docker/Podman).
2. Scaffold `src/` by running the prompts in `docs/scrub-scaffolding-prompts.md` (or `aspire init`
   to start the solution, then the prompts to fill it in).
3. Run the whole system with the dashboard:
   ```bash
   aspire run
   ```
   Aspire brings up PostgreSQL, waits for it to be healthy, then starts the API and the Angular
   app. The dashboard opens in your browser with logs, traces, and health for every resource.

> Verify exact Aspire commands and API names against https://aspire.dev — the framework ships
> fast and some surface (e.g. `AddNpmApp`, package names) moves between versions.

## License
MIT.
