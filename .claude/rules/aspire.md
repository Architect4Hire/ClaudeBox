---
paths:
  - src/RecipeBox.AppHost/**
  - src/RecipeBox.ServiceDefaults/**
---
# Aspire rules — AppHost & ServiceDefaults

The AppHost is the single source of truth for the application model. Keep it declarative.

- **Declare every resource here.** Postgres, cache, the API, and the Angular app are all added
  in the AppHost (e.g. `AddPostgres(...).AddDatabase("recipesdb")`, `AddProject<...>("api")`,
  `AddJavaScriptApp("web", "../client", "start")`). Nothing outside the AppHost invents infrastructure.
- **Local-first.** Backing resources run as local containers for development — no cloud/Azure
  resources in this PoC.
- **Wire with the model, not with strings.** Connect services using `WithReference(...)` and
  order startup with `WaitFor(...)`. Never hardcode connection strings or `localhost:port`;
  Aspire injects endpoints and connection info via environment/service discovery.
- **Cross-cutting config lives in ServiceDefaults.** OpenTelemetry, health checks, resilience,
  and service discovery are configured once there; every service calls `AddServiceDefaults()`.
- **No business logic in the AppHost.** It orchestrates; it doesn't compute.
- **The Angular app is an `AddJavaScriptApp` resource.** Aspire runs it and injects the API endpoint —
  the frontend reads the API base URL from the injected config, not a hardcoded value.

When adding a new resource, use the `add-aspire-resource` skill. Verify exact API names
(`AddJavaScriptApp`, client-integration methods, package names) against https://aspire.dev — these
move between versions.
