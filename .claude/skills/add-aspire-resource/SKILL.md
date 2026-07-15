---
name: add-aspire-resource
description: >
  Add a new locally-orchestrated resource (database, cache, message queue, container, or another
  project) to the RecipeBox Aspire AppHost and wire it into the services that use it. Use for
  requests like "add a Redis cache", "add a second database", "run this container alongside the
  API". Keeps everything local and driven through service discovery.
---

# Add an Aspire resource

Work primarily in `src/RecipeBox.AppHost/`. The goal: declare the resource, then reference it —
never hardcode connection details.

1. **Add the hosting package** if needed: `aspire add <resource>` (or add the
   `Aspire.Hosting.<Resource>` package). Confirm the exact package name at https://aspire.dev.
2. **Declare the resource in the AppHost.** e.g.
   `var cache = builder.AddRedis("cache");` or
   `var db = builder.AddPostgres("pg").AddDatabase("recipesdb");`
   Keep resources as local containers for this PoC.
3. **Wire it into consumers.** Add `.WithReference(cache)` to the projects that use it, and
   `.WaitFor(cache)` so they start after it's healthy.
4. **Consume it in the service.** In the ApiService, use the matching Aspire client integration
   keyed to the resource name (e.g. `AddRedisClient("cache")`) — the connection is injected, not
   configured by hand.
5. **Verify.** Run `aspire run`, open the dashboard, and confirm the resource is healthy and the
   consuming service connected.

## Rules
- Everything stays local — no cloud/Azure resources in this PoC.
- No connection strings or ports in application code; the AppHost + service discovery own that.
- The AppHost stays declarative.

## Checklist before done
- [ ] Resource declared in the AppHost as a local container
- [ ] Consumers wired with `WithReference` + `WaitFor`
- [ ] Service consumes it via the Aspire client integration (name-keyed, injected)
- [ ] Dashboard shows the resource healthy and connected
