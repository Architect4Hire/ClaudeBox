# src

The application lives here. It's scaffolded by driving Claude Code with the SCRUB prompts in
[`../docs/scrub-scaffolding-prompts.md`](../docs/scrub-scaffolding-prompts.md).

Intended layout once scaffolded:

```
src/
├── RecipeBox.AppHost/          # Aspire orchestrator — declares every resource
├── RecipeBox.ServiceDefaults/  # shared telemetry, health checks, resilience, discovery
├── RecipeBox.ApiService/       # ASP.NET Core API + EF Core (Npgsql)
└── RecipeBox/                  # Angular app (launched by Aspire via AddNpmApp)
```

Start with **Prompt 0** in the scaffolding doc (or `aspire init` to create the solution, then the
prompts to fill it in). The path-scoped rules in `.claude/rules/` will start applying as soon as
these folders exist.
