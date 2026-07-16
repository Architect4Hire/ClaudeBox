# Packaging RecipeBox as a Claude Code template

> **Status:** planned, not started. **Blocked on** the in-flight recipe-image feature landing
> (see [Blocked on](#-blocked-on-the-in-flight-image-feature) below).
>
> This is a working plan, not a rule source. It describes work to be done; when it's done, this
> document becomes history and belongs in `docs/build-log/`.

## Context

RecipeBox is two things at once: a working recipe app, and a public demonstration of driving an
Aspire + ASP.NET Core + Angular stack with Claude Code. The reusable half — the `.claude/` toolkit,
the layered API architecture, the Aspire wiring, the test factory — is currently welded to the
recipe domain. The goal is to extract it into a drop-in folder (`d:\architect4hire\template`) that
can be `git init`'d as a new repo and turned into any domain by running `/new-project`.

**The central finding from exploration:** the architecture is enforced by *prose, not code*. There
are no base classes, no generic repository, no DI extension methods.
[.claude/skills/add-endpoint/SKILL.md](../.claude/skills/add-endpoint/SKILL.md) and
[.claude/rules/](../.claude/rules/) **are** the template engine. So this is mostly a rename + docs
job, not a code-extraction job.

**The simplification that makes it tractable:** five of the six coupling variables are branded for
no reason. Neutralize them *in the template* and `/new-project` never touches them again.

| Variable | Today | Becomes | Renamed later by |
|---|---|---|---|
| DB resource | `recipesdb` | `appdb` | nobody — static forever |
| Blob container | `recipe-images` | `uploads` | nobody |
| CSS token prefix | `--rb-` | `--app-` | nobody |
| E2E override env | `RECIPEBOX_WEB_URL` | `APP_WEB_URL` | nobody |
| Web project path | `src/RecipeBox` | `src/web` | nobody |
| DbContext | `RecipeDbContext` | `AppDbContext` | nobody |
| **Project name** | `RecipeBox` | `RecipeBox` | **`/new-project` — the only rename** |

Entity names (`Recipe`, `Ingredient`, …) are never substituted — those files *move* to `reference/`
and the new slice is *generated* fresh. So the `Recipe ⊂ RecipeBox` find/replace hazard evaporates.

## End state: two repos, one story

- **`claudebox`** (this repo) stays the living, compiled, CI-verified showcase of the architecture.
- **`template`** is the derived starter. Its `reference/` folder is *regenerated from claudebox*,
  never hand-maintained — so the exemplar can't rot. Its README links to claudebox as the
  fully-worked example.

## Decisions already made

- Distribution: clone the folder + run `/new-project`. Not a `dotnet new` package.
- The recipe slice ships **already in `reference/`**, outside the solution, not compiled. User
  deletes it after their first real slice lands.
- Base state: git HEAD, **not** a dirty working tree. Keep `AddAzureStorage`/Azurite in the AppHost
  as a demonstrated blob resource, minus the recipe-image slice.
- Template keeps literal `RecipeBox` names (not `{{Mustache}}`, which breaks compilation; not a
  neutral name like `App`, which is a substring of ordinary identifiers and far riskier to replace).
- Template folder + repo name: `template`.

## ⚠️ Blocked on: the in-flight image feature

Phase A edits claudebox in place. At the time of writing the working tree had a half-wired image
feature (`IRecipeImageStore`, `BlobRecipeImageStore`, `RecipeImage`, the `AddRecipeImage` migration)
that `Program.cs` registered nowhere, and `RecipeImage.cs` referenced a `RecipeImageSniffer` type
that didn't exist. Plus an untracked `pagination/` component. **Finish and commit that work before
Phase A starts.**

---

## Phase A — Prep claudebox (in place)

Every item here is an improvement to RecipeBox on its own merits *and* removes work from
`/new-project`. Ordered by leverage.

**A1. Neutralize the five branded variables.**
`recipesdb`→`appdb` in [AppHost.cs](../src/RecipeBox.AppHost/AppHost.cs),
[Program.cs](../src/RecipeBox.ApiService/Program.cs), `.mcp.json`, `RecipeDbContextFactory.cs`,
`RecipeApiFactory.cs`, [rules/aspire.md](../.claude/rules/aspire.md),
[rules/backend.md](../.claude/rules/backend.md) · `recipe-images`→`uploads` · `--rb-`→`--app-`
(~230 refs across `styles.css` + component CSS) · `RECIPEBOX_WEB_URL`→`APP_WEB_URL` ·
`git mv src/RecipeBox src/web` (+ `AppHost.cs` `"../web"`, `rules/frontend.md` frontmatter,
`angular.json` outputPath, `package.json` name).

**A2. `RecipeDbContext`→`AppDbContext`; extract EF config to `IEntityTypeConfiguration<T>`.**
`OnModelCreating` is a 57-line domain dumping ground. One config class per entity under
`Managers/Models/Domain/`; `AppDbContext` keeps 5 `DbSet` lines + `ApplyConfigurationsFromAssembly`.
Better architecture regardless — and it makes the context a ~10-line file the skill edits trivially.

**A3. Generic `DomainConflictException` base.** `DomainExceptionHandler` is generic except one line
naming `RecipeNameConflictException`. Add the base, make the recipe exception derive from it, map
the base→409. The handler becomes 100% generic.

**A4. Split `RecipeApiFactory` → `AppApiFactory` + domain hook.** Keep the reusable parts — the
pooled-DbContext descriptor-removal loop (necessary because Aspire registers a *pooled* context;
dropping only the options leaves `IDbContextPool<>`/`IScopedDbContextLease<>` dangling), the
kept-open SQLite `:memory:` connection, the cache swap, `SeedAsync`/`QueryAsync` — plus a
`protected virtual ConfigureDomainServices`. This is the most reusable test asset in the repo;
after A2 it keys on `AppDbContext` and becomes permanently generic.

**A5.** `AddValidatorsFromAssemblyContaining<CreateRecipeViewModelValidator>()` → `<Program>()`.

**A6. Fix `format.sh` — latent bug.** [hooks/format.sh](../.claude/hooks/format.sh) runs
`npx prettier` with cwd at the repo root, which has **no `node_modules`** (prettier is a devDep
under the Angular app only). `|| true` swallows the failure, so the frontend half of the format hook
is plausibly a silent no-op today. Use `npx --prefix src/web prettier --write "$file"`.
**Verify by running it before assuming.**

**A7. Sanction the `.mcp.json` exception.** `.mcp.json` hardcodes `http://localhost:8765/sse`,
contradicting CLAUDE.md's "never hardcode `localhost:port`". It's *justified* — `.mcp.json` has no
service discovery, which is exactly why the AppHost pins the port — but an unexplained
self-contradiction reads as sloppiness on a public repo. Reword the Restriction to name it as the
one sanctioned exception and point at the pin.

**A8. Repo hygiene.** Delete `.agents/` — it's a **git-tracked, byte-identical duplicate of all 6
vendored skills** (36 files, leftover from `aspire agent init`; nothing reads it, and shipping it
guarantees drift). `git rm -r .playwright-mcp/` (22 tracked browser-session artifacts, not in
`.gitignore`) and gitignore it. Add `*.csproj.user`.

**A9. Fix the five known drifts** — the repo's own evidence that prose rots. `client/`→`src/web` in
[.claude/README.md](../.claude/README.md) and
[agents/test-gap-analyzer.md](../.claude/agents/test-gap-analyzer.md) · the `.claude/README.md`
table (lists 2 agents + 1 hook; missing `api-contract-checker`, `skills-evals`, `secret-guard.sh`,
all 6 vendored skills) · stray unmatched ``` fence at the end of
[add-endpoint/SKILL.md](../.claude/skills/add-endpoint/SKILL.md) · docs README links to two files
that don't exist (`scrub-scaffolding-prompts.md`, `30-day-claude-code-plan.md`) ·
`recipe.service.ts` missing `delete()` though the API and e2e test have it.

**A10. Build props.** `global.json` — SDK pin **plus `msbuild-sdks: { "Aspire.AppHost.Sdk": "13.4.6" }`**
(it's an `Sdk=` attribute, so CPM *cannot* centralize it — this is the piece a naive migration
misses) · `Directory.Build.props` (net10.0, Nullable, ImplicitUsings; **no** `TreatWarningsAsErrors`
— pass `-warnaserror` in CI only) · `Directory.Packages.props` (CPM + `$(AspireVersion)`).
`13.4.6` goes from 6 places to 2.

✅ **Verify:** `dotnet build && dotnet test` green · `cd src/web && npm ci && npm run build && ng test`
green · `git grep -c 13.4.6` = 2 · `aspire run` smoke test · **`/memory` shows all 3 rules still
loading** (the `paths:` frontmatter is a hard blocker — if a glob silently stops matching,
everything downstream runs unguided). Commit.

## Phase B — CI in claudebox

`.github/workflows/` exists but is **empty**. Three jobs in `ci.yml` (push to main + PRs):
`backend` (`setup-dotnet` with `global-json-file` → restore → `build -warnaserror` → `dotnet test`;
Testcontainers Postgres works on `ubuntu-latest`) · `frontend` (`npm ci` → `ng test --watch=false`
→ `npm run build`; Vitest/jsdom is already headless-safe) · `format` — **must invoke exactly what
`format.sh` invokes**, or the hook formats one way and CI fails the other.

`e2e.yml` on `workflow_dispatch` + nightly `schedule`, **not** PRs — it needs the aspire CLI, Docker,
Postgres, Redis and Azurite all up. A showcase wants a green badge; a flaky e2e job on every PR
undermines that. Set `APP_WEB_URL` in CI to skip `aspire describe` discovery.

Do B before C: CI is the only thing keeping A's refactors honest.

## Phase C — Create `d:\architect4hire\template`

Copy post-Phase-A claudebox, then quarantine the slice.

`git mv` into `reference/recipes/{api,web,tests}/`: `Controllers/RecipesController.cs`, `Facade/*`,
`Business/*`, `Data/{Recipe,IRecipe}*.cs`, `Managers/Models/**`, `Managers/Validators/*`,
`Managers/Mappers/*`, `Tests/Recipes/**`, `web/src/app/{models,services,recipes}/**`. Nothing needs
build exclusion — `reference/` sits at the repo root, outside every `.csproj` directory, so
MSBuild's default globs never see it.

**Stays in `src/`** (after Phase A these contain zero domain refs): `Data/IDataTransaction.cs`,
`Data/EfDataTransaction.cs`, `Managers/Infrastructure/DomainExceptionHandler.cs`,
`Managers/Infrastructure/DomainConflictException.cs`, `Data/AppDbContext.cs`,
`Data/AppDbContextFactory.cs`, `Tests/Infrastructure/AppApiFactory.cs`, `proxy.conf.js`,
`playwright.config.ts`, the whole Angular shell + design-token architecture, ServiceDefaults.

Then: trim `Program.cs` DI lines, `AppDbContext` DbSets, `app.routes.ts` · **delete `Migrations/`
wholesale** · drop `settings.local.json` (13KB, 124 entries, machine-specific: absolute
`//c/Users/chica/...` paths, dead PIDs, ~15 Wikimedia recipe-photo downloads) · `docs/` becomes
`docs/build-log/` (the exercise docs verbatim, headed *historical, not a rule source*) plus new
`architecture.md` and `template-guide.md`.

### Final tree

```
d:\architect4hire\template\
├── .github/workflows/{ci.yml,e2e.yml}
├── .claude/
│   ├── README.md · settings.json
│   ├── agents/{code-reviewer,test-gap-analyzer,api-contract-checker,skills-evals}.md
│   ├── hooks/{format.sh,secret-guard.sh}
│   ├── rules/{aspire,backend,frontend}.md
│   └── skills/
│       ├── new-project/SKILL.md          # NEW
│       ├── add-endpoint/ · new-component/ · add-aspire-resource/
│       └── {aspire,aspire-init,aspire-orchestration,aspire-deployment,
│            aspire-monitoring,playwright-cli}/    # 6 vendored, untouched
├── reference/recipes/{api,web,tests}/ + README.md   # not compiled; delete after first slice
├── global.json · Directory.Build.props · Directory.Packages.props
├── RecipeBox.slnx · CLAUDE.md · README.md · LICENSE · .mcp.json · .gitignore
├── docs/{README.md,architecture.md,template-guide.md,build-log/}
└── src/
    ├── RecipeBox.AppHost/ · RecipeBox.ServiceDefaults/
    ├── RecipeBox.ApiService/    # generic seams only; Controllers/ + Managers/Models/ empty
    ├── RecipeBox.Tests/         # AppApiFactory + generic tests
    └── web/
```

## Phase D — The `/new-project` skill

`.claude/skills/new-project/SKILL.md`.

**Interview (4 questions, then print a plan summary and stop for approval — CLAUDE.md already
mandates this):** project name (PascalCase, valid C# identifier, not `RecipeBox`) · one-line domain
description (feeds CLAUDE.md Scope + README) · first slice (aggregate root, 3–6 fields, child
collections y/n, taxonomy y/n — mirror the recipe shape so `reference/` maps cleanly) · backing
resources (Postgres always; Redis y/n; blob y/n).

**Phase 0 — Preflight, before any mutation.** `git status --porcelain` must be empty (refuse
otherwise — this is the whole safety net; every phase is `git checkout .`-recoverable). Build+test
green now. `dotnet`/`node`/`aspire` present.

**Phase 1 — Rename `RecipeBox` → `<Name>`.** Directories → filenames → contents, in that order.
Replace **three casings**: `RecipeBox`, `recipebox`, `recipe-box` (that last catches
`package.json`'s `"name"`). Do **not** add `Recipe`→`<Entity>` — entities are generated, not
substituted. Exclude `docs/build-log/` — it's a record of building RecipeBox; renaming falsifies
history.

- **Regenerate `UserSecretsId`** (`dotnet user-secrets init --force`). A GUID isn't
  `RecipeBox`-shaped, so find/replace **silently misses it** and every clone would share one secrets
  store.
- **`dotnet clean` + delete `bin/`/`obj/` before rebuilding.** The `Projects.*` types are
  source-generated from the csproj *filename*; stale `obj/` makes `Projects.RecipeBox_ApiService`
  linger or the new one fail to appear. Silent, confusing failure.
- ✅ `git grep -il recipebox -- . ':!docs/build-log'` empty · build/test/npm green · `/memory` shows
  the 3 rules loading.

**Phase 2 — Generate the first slice.** Read `reference/recipes/**` + `add-endpoint/SKILL.md`,
follow its 13 steps. ✅ build green · a test per layer passes · `@skills-evals` audit clean.

**Phase 3 — Migrations.** `dotnet ef migrations add InitialCreate`. Must run *after* the recipe
entities are out of `AppDbContext`. (`migrations add` never opens a connection —
`AppDbContextFactory`'s placeholder string exists exactly for this, which is why it survives to
`src/`.) ✅ `has-pending-model-changes` clean.

**Phase 4 — Rewrite `.claude/` domain prose.** Rules' trailing "domain (starting shape)" lines ·
`add-endpoint`'s domain notes + step-10 DI block + `IRecipe*` chain (the layering prose — the
valuable 95% — is already domain-neutral) · `new-component` · `add-aspire-resource` ·
`code-reviewer` (one word) · `test-gap-analyzer` · `api-contract-checker` (hardest — welded to
specific type pairs; needs a "one C# boundary dir, one TS models file" placeholder pair) ·
`skills-evals` routing table · `.claude/README.md` · CLAUDE.md Scope/Layout.

*Why after Phase 2:* rewriting the skill first means describing code that doesn't exist yet.
**Tradeoff:** Phase 2 runs against a skill whose domain notes still say "Recipe" — fine, they're
illustrative and `reference/` is right there, but the skill must say so or Claude will "helpfully"
fix them mid-run.

**Phase 5 — Finish.** Rewrite root README, seed sample data, `aspire run` smoke test, `npm run e2e`.
Tell the user to delete `reference/` and commit.

### Traps a naive find/replace hits

| Trap | Answer |
|---|---|
| `.mcp.json` db name + pinned port 8765 | **Never touched** — Phase A made it `appdb`, port stays pinned |
| `mcp__recipesdb__*` tool names | Gone with the above. Harmless today only because `settings.json` has no `permissions` block — would silently break any allowlist a user later adds |
| `UserSecretsId` | Not `RecipeBox`-shaped → silently missed. Explicit regeneration |
| EF migrations | Deleted wholesale, regenerated. Never patched — their snapshots embed namespaces |
| `--rb-` prefix (~230 refs) | Phase A → `--app-`. The token *architecture* is the asset; the prefix is branding |
| `RECIPEBOX_WEB_URL` | **Sharpest trap.** Case-sensitive replace misses it (silent breakage); case-insensitive mangles it to `<Name>_WEB_URL` while docs say `<NAME>_WEB_URL`. Phase A → `APP_WEB_URL`. Dead |
| Pooled-DbContext descriptor loop | Keys on `typeof(RecipeDbContext)` → A2 makes it `AppDbContext`, permanently generic |
| `Projects.RecipeBox_ApiService` | Falls out of the rename **but requires `dotnet clean`** |

## Phase E — The README

`template/README.md`, the front door. Must carry the "this is a recipe app you are about to rename"
framing in its first screen — that's the honest cost of literal names.

Sections: what you get (stack + the `.claude/` toolkit, one screen) · **quickstart**
(use-this-template → clone → `chmod +x .claude/hooks/*.sh` → `claude` → `/new-project` →
`aspire run`) · what `/new-project` asks and does · the architecture in brief, linking
`docs/architecture.md` for *why* and `add-endpoint` for *how* (don't duplicate — that's a fifth
drift source) · the `.claude/` toolkit table with the Rule=know / Skill=follow / Subagent=delegate /
Hook=must happen rule of thumb (the best thing in the current `.claude/README.md`) · `reference/` —
what it is, delete it when done · prerequisites (.NET 10, Node, Docker, aspire CLI) · a link to
claudebox as the fully-worked example.

Also: `docs/architecture.md` (the *why*: why the facade owns caching, why the data layer owns
transactions, why there's no DTO layer) · `docs/template-guide.md` (what `/new-project` does, what
to do after, how to extend `.claude/`) · **tighten `skills-evals.md`**, which says "`docs/` is
historical narrative, never audit against it" — once `architecture.md` exists that's wrong; narrow
it to `docs/build-log/`.

## Phase F — Dry run, then ship

**This is the load-bearing step.** A `/new-project` skill that has never been executed end-to-end is
a hypothesis. Copy `template/` to a scratch dir, `git init`, run `/new-project` for a real throwaway
domain, and require: green build, green tests, `aspire run` boots, the generated slice passes
`@skills-evals`. Then fix the skill and repeat. **Budget ≥2 iterations.**

Then `git init` the real repo, push, tick GitHub's **"Template repository"** setting (gives users a
"Use this template" button and clean history), tag `v1.0.0`.

---

## Verification summary

| Phase | Green means |
|---|---|
| A | claudebox builds, tests pass, `aspire run` boots, `/memory` loads 3 rules, `git grep -c 13.4.6` = 2 |
| B | CI badge green on a PR |
| C | `template/` builds with an empty API; `dotnet test` passes on generic tests only |
| D | — (skill is prose; proven in F) |
| E | A stranger can go clone → running app from the README alone |
| F | Scratch clone: `/new-project` → build + test + `aspire run` all green, `@skills-evals` clean |
