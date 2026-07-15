---
paths:
  - src/RecipeBox.ApiService/**
---
# Backend rules — ASP.NET Core + EF Core (Aspire)

- **Thin controllers.** Parse/validate input, call a service, return a typed result. No
  business logic or EF queries in controllers.
- **DTOs at the boundary.** Never return EF entities from an endpoint — map to a DTO.
- **DbContext via Aspire.** Register the EF Core context through the Aspire Npgsql integration
  keyed to the AppHost resource name (e.g. the `recipesdb` database), not by reading a raw
  connection string from `appsettings.json`.
- **Async all the way.** `async Task<...>` with `await`; never `.Result` or `.Wait()`.
- **Validate at the edge.** Data annotations or FluentValidation; on failure return the shared
  error shape, not a raw exception.
- **Service defaults.** `Program.cs` calls `AddServiceDefaults()` so telemetry, health checks,
  and resilience are consistent with the rest of the system.
- **EF Core workflow.** Change the model, then `dotnet ef migrations add <Name>`, review, then
  `dotnet ef database update`. Commit the migration.
- **Naming.** PascalCase types/methods, `_camelCase` private fields, camelCase locals.

Recipe domain (starting shape): `Recipe`, `Ingredient`, `Step`, `Category`/`Tag`. A recipe has
many ingredients and ordered steps.
