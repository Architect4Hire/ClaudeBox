---
name: new-component
description: >
  Scaffold a new Angular component in the RecipeBox frontend. Use when creating UI — e.g. "add a
  recipe-detail component", "make a category-filter". Produces a standalone component wired to a
  typed service, following this repo's frontend conventions.
---

# Add an Angular component

Work in `src/RecipeBox/`.

1. **Generate.** `ng generate component <feature>/<name>` — standalone, kebab-case files.
2. **Types.** Define/reuse a model interface that mirrors the backend ServiceModels exactly. No `any`.
3. **Data access.** Never call the API from the component. Use (or create) a typed service on
   `HttpClient`; read the API base URL from Aspire-injected config, not a hardcoded value.
4. **Template.** Render async data with the `async` pipe. If you must subscribe, clean up with
   `takeUntilDestroyed` / `DestroyRef`.
5. **Tests.** Update `.spec.ts` with a render test and one behavior test. Run `ng test`.

## Recipe UI notes
Typical components: `recipe-list` (cards + filter), `recipe-detail` (ingredients + ordered
steps), `recipe-form` (create/edit), `category-filter`. Keep presentational and data concerns
separate where it helps testability.

## Checklist before done
- [ ] Standalone component, kebab-case filenames
- [ ] Model interface mirrors the backend ServiceModels
- [ ] API access through a typed service; base URL from injected config
- [ ] `async` pipe used (or subscriptions cleaned up)
- [ ] Tests pass (`ng test`)
