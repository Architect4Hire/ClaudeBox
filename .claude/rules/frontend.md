---
paths:
  - src/client/**
---
# Frontend rules — Angular + TypeScript (Aspire)

- **Standalone components** (no NgModules). One feature per folder.
- **Strict TypeScript.** No `any`. Model interfaces mirror the backend DTOs exactly.
- **API base URL comes from Aspire.** The app is launched by `AddJavaScriptApp`, so read the API
  endpoint from injected environment/config — don't hardcode `localhost:port`.
- **Data access through services.** Components never call `HttpClient` directly; use a typed
  service. One service per resource (e.g. `RecipeService`, `CategoryService`).
- **Subscriptions.** Prefer the `async` pipe. If you must subscribe, clean up with
  `takeUntilDestroyed` / `DestroyRef`.
- **Scaffolding.** Generate with `ng generate component <feature>/<name>` (or the
  `new-component` skill) so structure stays consistent.
- **Naming.** kebab-case filenames, PascalCase classes, camelCase members.

Recipe UI (starting shape): `recipe-list`, `recipe-detail`, `recipe-form`, plus a
`category-filter`.
