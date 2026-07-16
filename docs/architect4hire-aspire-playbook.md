# Stand up an Aspire solution with Claude Code

### The architect4hire playbook — SCRUB, `CLAUDE.md`, and a `.claude/` toolkit that enforces an architecture

> **Who this is for.** You want a production-shaped **Aspire + ASP.NET Core + Angular** solution,
> built with Claude Code, that holds its architecture under pressure — including the pressure of an
> agent writing most of the code. You bring your own domain. This playbook brings the structure.
>
> **What you'll have at the end.** A running local system (Postgres + API + Angular, orchestrated by
> Aspire), a `CLAUDE.md` written as a SCRUB prompt, a `.claude/` toolkit of rules, skills, subagents
> and hooks, and a first vertical slice through a layered architecture — with every prompt that got
> you there, reusable.
>
> **Time.** A focused afternoon to a running slice. The toolkit outlives the project.

---

## Why this is a document and not a template

The obvious way to ship a reusable architecture is a starter repo: clone it, find-and-replace the
name, go. We tried that. It failed, and the way it failed is the most useful thing in this playbook.

A rename-the-repo template has to substitute a project name across three casings (`ProjectName`,
`projectname`, `project-name`) and then survive everything that *isn't* name-shaped:

| Trap | Why the rename misses it |
|---|---|
| `UserSecretsId` | It's a GUID. Not name-shaped, so find/replace silently skips it — every clone shares one secrets store. |
| `Projects.MyApp_ApiService` | Source-generated from the `.csproj` *filename*. The rename is correct and the build still fails until `dotnet clean` + `rm -rf obj/`. |
| EF migration snapshots | They embed namespaces as strings. Patching them is worse than deleting and regenerating. |
| `MYAPP_WEB_URL` | Case-sensitive replace misses it (silent breakage); case-insensitive mangles it to `MyApp_WEB_URL` while the docs say `MYAPP_WEB_URL`. |
| ~230 `--myapp-` CSS tokens | The token *architecture* is the asset. The prefix is branding, and it's load-bearing in every stylesheet. |

Every one of those is a tax on cloning. **None of them exist if you generate the project fresh.**

And the deeper finding, the one that turns the failure into a method: when we went looking for the
code that enforces the architecture — a base controller, a generic repository, a DI extension
package — **there wasn't any.** There is no framework here. The layering is enforced by *prose*:
`CLAUDE.md`, three rules files, and one skill. Those files **are** the template engine.

So the artifact you want isn't a repo to rename. It's the prose — plus the prompts that turn the
prose into a working solution. That's what follows.

**The rule that falls out of this:** *never inherit a name you have to replace.* Neutral by
construction, from the first commit — see [Part 2.5](#25-six-names-you-should-never-brand).

---

## The worked example (which you will not clone)

Prompts written in the abstract produce abstract code. So this playbook carries one concrete domain
all the way through — **architect4hire.com**, a consultant lead-generation and portfolio site:

- **`CaseStudy`** — the aggregate root. Title, summary, client, year. Has an ordered child
  collection of **`Outcome`** (the measurable results) and a many-to-many taxonomy of
  **`Capability`** (Aspire, Azure, event-driven, …).
- **`Inquiry`** — the money path. A lead arrives from the public site, with one real domain rule:
  the same email may not open a second inquiry within 24 hours.

That shape — **aggregate root + ordered children + taxonomy + one write with a real rule** — is
chosen deliberately. It exercises every layer you're about to build. Your domain almost certainly
has the same shape under different nouns.

**Everywhere you see `<Angled>` placeholders, substitute your own.** The architect4hire values are
shown beside them as a filled-in example, never as a thing to copy.

---

## Part 0 — The domain card

Do this before you open Claude Code. Answer five questions once, and paste the result into the
prompts in Part 4. This is the single mechanism that makes a generic playbook produce *your* system.

```
DOMAIN CARD

PROJECT NAME:   <PascalCase, valid C# identifier — becomes the namespace root>
ONE-LINER:      <what this system does, in one sentence>
AGGREGATE ROOT: <the noun everything hangs off> (<3–6 fields>)
CHILDREN:       <ordered or unordered child collection(s), or "none">
TAXONOMY:       <many-to-many tag/category type, or "none">
FIRST WRITE:    <the create/update path> — domain rule: <the rule that can reject it>
RESOURCES:      Postgres (always) · cache <y/n> · blob storage <y/n>
```

**Filled in for architect4hire:**

```
DOMAIN CARD

PROJECT NAME:   Architect4Hire
ONE-LINER:      A consultant lead-gen and portfolio site: publish case studies, capture inquiries.
AGGREGATE ROOT: CaseStudy (title, summary, client, year, published)
CHILDREN:       Outcome (ordered — metric, result)
TAXONOMY:       Capability (many-to-many)
FIRST WRITE:    Create an Inquiry — domain rule: reject a second inquiry from the same email
                within 24 hours (409 Conflict)
RESOURCES:      Postgres · cache: yes · blob storage: no
```

A note on **PROJECT NAME**: pick it now and never change it. Also check it isn't a *substring* of an
ordinary identifier in your domain — the reason a neutral name like `App` is a worse choice than a
distinctive one. `Recipe ⊂ RecipeBox` is exactly the collision that made the old template dangerous.

---

## Part 1 — `CLAUDE.md` as a SCRUB prompt

### The problem `CLAUDE.md` actually solves

`CLAUDE.md` is loaded into context at the start of every session and survives compaction. That makes
it the highest-leverage file in the repo — and the easiest to ruin. The failure mode is entropy: it
becomes a junk drawer, nobody knows which section a new rule belongs in, contradictions accumulate,
and eventually it's long enough that it stops being read carefully by anyone, human or model.

**SCRUB is a structure that fights that.** Five sections, each with a distinct job:

| | Section | Answers | Failure it prevents |
|---|---|---|---|
| **S** | **Scope** | What are we building — and what are we *not*? | Unprompted feature-building |
| **C** | **Constraints** | What's fixed: stack, layout, conventions, commands | Invented structure, wrong commands |
| **R** | **Restrictions** | The explicit "do NOT"s | The specific mistakes you've already seen |
| **U** | **Usage** | What tooling exists and how the world runs | Reinventing what you already built |
| **B** | **Behavior** | How to proceed: plan, approve, test, report | Sprawling unreviewed changes |

Two properties make this worth the discipline:

1. **Every new rule has one obvious home.** When you learn something, you know where it goes. That's
   what keeps the file from becoming a junk drawer over months.
2. **Every misstep is diagnosable by section.** The agent built something you didn't ask for →
   Scope failed. It hardcoded a connection string → Restrictions failed. It hand-rolled a procedure
   you already have a skill for → Usage failed. It rewrote nine files without asking → Behavior
   failed. **You don't debug the model, you debug the section.** That single property is why a
   structured memory file beats a better-written unstructured one.

### The elegant part: the same five sections work as a prompt

SCRUB isn't only a file layout. It's the shape of a good instruction, at any scale:

```
SCOPE:        what to build/change + which part of the repo it touches
CONSTRAINT:   the rules to honor (stack, conventions, plan-first)
RESTRICTION:  explicit "do NOT" guardrails
UTILIZATION:  which skills / subagents / tools to use
BEHAVIOR:     how to proceed — plan, approve, small steps, test, report
```

The **U** shifts meaning with scale, and that's the trick: in the file it's **Usage** ("here is what
exists"); in a prompt it's **Utilization** ("use *this*, now"). The standing memory declares the
toolkit; the prompt selects from it. Same five questions, different altitude.

So `CLAUDE.md` is a **standing prompt**, and every task prompt in Part 4 is a *delta* against it.
That's why the task prompts can be short — they only say what's different today.

### The template

Save at the **repo root**, not in `.claude/`. Claude Code finds it by walking up from your working
directory, and the root copy survives `/compact`.

````markdown
# <ProjectName>

*Project memory, written as a SCRUB prompt — Scope, Constraints, Restrictions, Usage, Behavior.
Loaded every session. Every new rule has one obvious home, and every misstep is diagnosable by
section.*

## Scope

<One-liner from your domain card.> Built with **Aspire + ASP.NET Core + Angular**, developed with
Claude Code. The reusable toolkit lives in `.claude/`.

- **In bounds:** <the app> and the `.claude/` toolkit that builds it.
- **Out of bounds (don't build unprompted):** <the adjacent thing an agent will helpfully invent —
  name it explicitly>.

## Constraints

**Stack**

- **Orchestration:** Aspire 13 (AppHost + ServiceDefaults) on .NET 10
- **Backend:** ASP.NET Core Web API · EF Core (Npgsql) — `src/<ProjectName>.ApiService/`
- **Frontend:** Angular (standalone components, strict TS) — `src/web/`, run via `AddJavaScriptApp`
- **Data/infra (local containers via Aspire):** PostgreSQL, and a cache if/when needed

**Layout**

```
src/
├── <ProjectName>.AppHost/          # Aspire orchestrator — declares every resource
├── <ProjectName>.ServiceDefaults/  # shared telemetry, health checks, resilience, discovery
├── <ProjectName>.ApiService/       # ASP.NET Core API + EF Core
└── web/                            # Angular app (AddJavaScriptApp target)
```

**Architecture conventions** — area detail auto-loads from `.claude/rules/` (`aspire.md`,
`backend.md`, `frontend.md`). The essentials:

- **Aspire:** every resource is declared in the AppHost. The `DbContext` comes from the Aspire
  Npgsql integration, keyed to the AppHost resource name.
- **Backend:** thin controllers, boundary types at the edge (never expose EF entities), everything
  async, input validated at the edge.
- **Frontend:** standalone components, typed models mirroring the API's outbound types, HTTP only
  through services, `async` pipe (no leaked subscriptions).

**Canonical commands** (use these verbatim)

- Whole system: `aspire run` · add a resource package `aspire add <resource>`
- Backend (`src/<ProjectName>.ApiService/`): `dotnet test` · `dotnet ef migrations add <Name>` ·
  `dotnet ef database update`
- Frontend (`src/web/`): `npm install` · `ng test` · `ng build`

## Restrictions

- Don't hardcode connection strings or `localhost:port` — wire through the AppHost and service
  discovery / Aspire-injected config.
- Don't put business logic in the AppHost; it stays declarative.
- Don't run `ng serve` by hand — Aspire launches the client via `AddJavaScriptApp`.
- Don't hand-edit generated EF migrations except to review them.
- Don't commit `bin/`, `obj/`, `node_modules/`, or any secrets.

## Usage

- The world is **local**: Aspire's AppHost orchestrates the API, the Angular app, and all backing
  resources as local containers — no cloud dependencies. The Aspire dashboard is the front door for
  logs, traces, and health.
- Services find each other through service discovery / Aspire-injected config.
- The Angular app is the primary consumer of the API — keep the contract stable.
- Available tooling in `.claude/`: rules auto-load from `.claude/rules/`; task skills live in
  `.claude/skills/` (`add-endpoint`, `new-component`, `add-aspire-resource`); subagents are
  available but run **read-only**.

## Behavior

- Plan before any change touching more than one file; wait for approval on non-trivial work.
- Use the matching skill in `.claude/skills/` instead of freelancing.
- Run the relevant tests before calling a task done.
- Make edits in the main session so I can approve them — subagents stay read-only.
````

### Three things to get right

**Keep it short.** The real one this is drawn from is ~60 lines. Every line competes for attention
with the code. If a rule only matters in one area, it belongs in a path-scoped rule file (Part 2.1),
not here — that's the whole reason rules exist.

**Write Restrictions from scars, not from imagination.** Each "don't" should be a mistake you've
actually watched happen. A restriction nobody has ever violated is noise that dilutes the ones that
matter.

**Name your exceptions in the rule itself.** An unexplained self-contradiction reads as sloppiness,
and worse, an agent that finds one learns the rule is soft. Here is a real one, worth studying:

> Don't hardcode connection strings or `localhost:port`. **One sanctioned exception:** `.mcp.json`'s
> `appdb` server names `http://localhost:8765/sse`. MCP client config is read before the AppHost
> runs and has no service discovery to read from, so the address has to be literal — which is
> exactly why the AppHost *pins* that port (`WithEndpoint("http", e => e.Port = 8765)`) instead of
> letting Aspire assign a random one. The pin and the literal are two halves of one decision: change
> one, change the other. Nothing else gets to hardcode an address.

That paragraph does four jobs: states the exception, explains *why* it's forced, ties it to the
mechanism that makes it safe, and slams the door on generalization. That's the standard.

---

## Part 2 — The `.claude/` toolkit

### The taxonomy that keeps it coherent

Four artifact types. Confusing them is the most common way these folders rot:

| | Use it for | Loads |
|---|---|---|
| **Rule / `CLAUDE.md`** | Something Claude should **know** | Automatically |
| **Skill** | A procedure Claude should **follow** when a task matches | On demand |
| **Subagent** | Work Claude should **delegate** to keep the main context clean | When delegated |
| **Hook** | Something that must happen **no matter what Claude decides** | Deterministically |

The distinction that earns its keep is **hook vs. everything else**. Rules, skills and prompts are
all *persuasion* — the model usually complies. A hook is *enforcement* — it runs whether the model
agrees, forgets, or was never told. Anything you cannot afford to have skipped (secret scanning,
formatting) is a hook. Everything else is prose.

The target folder:

```
.claude/
├── settings.json                # hook wiring (committed)
├── settings.local.json          # personal overrides (git-ignored)
├── rules/{aspire,backend,frontend}.md
├── skills/{add-endpoint,new-component,add-aspire-resource}/SKILL.md
├── agents/{code-reviewer,test-gap-analyzer,api-contract-checker,skills-evals}.md
└── hooks/{format.sh,secret-guard.sh}
```

### 2.1 Rules — path-scoped knowledge

Rules are `CLAUDE.md` fragments that load **only when Claude touches matching files**. That's the
point: your Angular conventions cost nothing while working on the AppHost.

`.claude/rules/aspire.md` — note the `paths:` frontmatter, which is what scopes it:

```markdown
---
paths:
  - src/<ProjectName>.AppHost/**
  - src/<ProjectName>.ServiceDefaults/**
---
# Aspire rules — AppHost & ServiceDefaults

The AppHost is the single source of truth for the application model. Keep it declarative.

- **Declare every resource here.** Postgres, cache, the API, and the Angular app are all added in
  the AppHost (e.g. `AddPostgres(...).AddDatabase("appdb")`, `AddProject<...>("api")`,
  `AddJavaScriptApp("web", "../web", "start")`). Nothing outside the AppHost invents infrastructure.
- **Local-first.** Backing resources run as local containers — no cloud resources. An
  *emulator-backed* Azure resource is in bounds, because it is a local container:
  `AddAzureStorage("storage").RunAsEmulator(...)` runs Azurite exactly as `AddPostgres` runs
  Postgres. The test is *where it runs*, not what the API is called. What stays out is a resource
  needing a real subscription — anything reached with `AsExisting`, or provisioned for real.
- **Wire with the model, not with strings.** Connect services using `WithReference(...)` and order
  startup with `WaitFor(...)`. Never hardcode connection strings or `localhost:port`.
- **Cross-cutting config lives in ServiceDefaults.** OpenTelemetry, health checks, resilience and
  service discovery are configured once there; every service calls `AddServiceDefaults()`.
- **No business logic in the AppHost.** It orchestrates; it doesn't compute.
- **The Angular app is an `AddJavaScriptApp` resource.** Aspire runs it and injects the API endpoint.

When adding a new resource, use the `add-aspire-resource` skill. Verify exact API names
(`AddJavaScriptApp`, client-integration methods, package names) against https://aspire.dev — these
move between versions.
```

The "local-first" bullet is worth copying for its *shape*, not its content. A naive rule ("no Azure")
would ban `AddAzureStorage(...).RunAsEmulator(...)`, which is just Azurite in a container and
perfectly in bounds. The rule states the principle (*where it runs*), then names the boundary
(`AsExisting`, real provisioning). **Rules that ban surface syntax instead of stating the principle
produce agents that argue with you.**

`.claude/rules/backend.md`:

```markdown
---
paths:
  - src/<ProjectName>.ApiService/**
---
# Backend rules — ASP.NET Core + EF Core (Aspire)

- **Thin controllers.** Parse/validate input, call a service, return a typed result. No business
  logic or EF queries in controllers.
- **Boundary types at the edge.** Never return EF entities from an endpoint.
- **DbContext via Aspire.** Register the EF Core context through the Aspire Npgsql integration keyed
  to the AppHost resource name (the `appdb` database), not a raw connection string in
  `appsettings.json`.
- **Async all the way.** `async Task<...>` with `await`; never `.Result` or `.Wait()`.
- **Validate at the edge.** FluentValidation; on failure return the shared error shape, not a raw
  exception.
- **Service defaults.** `Program.cs` calls `AddServiceDefaults()`.
- **EF Core workflow.** Change the model, then `dotnet ef migrations add <Name>`, review, then
  `dotnet ef database update`. Commit the migration.
- **Naming.** PascalCase types/methods, `_camelCase` private fields, camelCase locals.

<Domain> (starting shape): `<AggregateRoot>`, `<Child>`, `<Taxonomy>`.
<e.g. CaseStudy has many ordered Outcomes and many Capabilities.>
```

`.claude/rules/frontend.md`:

```markdown
---
paths:
  - src/web/**
---
# Frontend rules — Angular + TypeScript (Aspire)

- **Standalone components** (no NgModules). One feature per folder.
- **Strict TypeScript.** No `any`. Model interfaces mirror the backend's outbound types exactly.
- **API base URL comes from Aspire.** The app is launched by `AddJavaScriptApp`, so read the API
  endpoint from injected environment/config — don't hardcode `localhost:port`.
- **Data access through services.** Components never call `HttpClient` directly; use a typed
  service. One service per resource (e.g. `<Root>Service`).
- **Subscriptions.** Prefer the `async` pipe. If you must subscribe, clean up with
  `takeUntilDestroyed` / `DestroyRef`.
- **Scaffolding.** Generate with `ng generate component <feature>/<name>` (or the `new-component`
  skill) so structure stays consistent.
- **Naming.** kebab-case filenames, PascalCase classes, camelCase members.

<Domain> UI (starting shape): `<root>-list`, `<root>-detail`, `<root>-form`, `<taxonomy>-filter`.
```

> ⚠️ **The `paths:` glob is a hard blocker.** If it silently stops matching — you renamed a folder,
> you typo'd a segment — the rule stops loading and *everything downstream runs unguided*, with no
> error. After any restructuring, run `/memory` and confirm all three rules still load. This is the
> single highest-value verification in the playbook.

### 2.2 Skills — procedures worth following exactly

A rule is a fact; a skill is a **recipe with a checklist**. Reach for a skill when a task has a
correct *sequence* and a definition of done. The big one — `add-endpoint` — is Part 3, because it's
not really a skill, it's the architecture.

The other two are small on purpose. `.claude/skills/new-component/SKILL.md`:

```markdown
---
name: new-component
description: >
  Scaffold a new Angular component in the <ProjectName> frontend. Use when creating UI — e.g. "add a
  <root>-detail component", "make a <taxonomy>-filter". Produces a standalone component wired to a
  typed service, following this repo's frontend conventions.
---

# Add an Angular component

Work in `src/web/`.

1. **Generate.** `ng generate component <feature>/<name>` — standalone, kebab-case files.
2. **Types.** Define/reuse a model interface mirroring the backend's outbound types exactly. No `any`.
3. **Data access.** Never call the API from the component. Use (or create) a typed service on
   `HttpClient`; read the API base URL from Aspire-injected config, not a hardcoded value.
4. **Template.** Render async data with the `async` pipe. If you must subscribe, clean up with
   `takeUntilDestroyed` / `DestroyRef`.
5. **Tests.** Update `.spec.ts` with a render test and one behavior test. Run `ng test`.

## Checklist before done
- [ ] Standalone component, kebab-case filenames
- [ ] Model interface mirrors the backend's outbound types
- [ ] API access through a typed service; base URL from injected config
- [ ] `async` pipe used (or subscriptions cleaned up)
- [ ] Tests pass (`ng test`)
```

The `description:` field is not documentation — **it is the routing logic.** It's what Claude reads
to decide whether this skill applies. Write it with the user's words in it ("add a detail
component", "make a filter"), not with your internal vocabulary. A skill with a vague description is
a skill that never fires.

`.claude/skills/add-aspire-resource/SKILL.md`:

```markdown
---
name: add-aspire-resource
description: >
  Add a new locally-orchestrated resource (database, cache, message queue, container, or another
  project) to the <ProjectName> Aspire AppHost and wire it into the services that use it. Use for
  requests like "add a Redis cache", "add a second database", "run this container alongside the
  API". Keeps everything local and driven through service discovery.
---

# Add an Aspire resource

Work primarily in `src/<ProjectName>.AppHost/`. The goal: declare the resource, then reference it —
never hardcode connection details.

1. **Add the hosting package** if needed: `aspire add <resource>` (or the `Aspire.Hosting.<Resource>`
   package). Confirm the exact package name at https://aspire.dev.
2. **Declare the resource in the AppHost.** e.g. `var cache = builder.AddRedis("cache");` or
   `var db = builder.AddPostgres("pg").AddDatabase("appdb");` Keep resources as local containers.
3. **Wire it into consumers.** Add `.WithReference(cache)` to the projects that use it, and
   `.WaitFor(cache)` so they start after it's healthy.
4. **Consume it in the service.** Use the matching Aspire client integration keyed to the resource
   name (e.g. `AddRedisClient("cache")`) — the connection is injected, not configured by hand.
5. **Verify.** `aspire run`, open the dashboard, confirm the resource is healthy and connected.

## Checklist before done
- [ ] Resource declared in the AppHost as a local container
- [ ] Consumers wired with `WithReference` + `WaitFor`
- [ ] Service consumes it via the Aspire client integration (name-keyed, injected)
- [ ] Dashboard shows the resource healthy and connected
```

### 2.3 Subagents — delegation with a blast radius of zero

Subagents run in their own context window and report back. Two reasons to use one: the work would
**flood your main context** (reading 30 files to find a bug), or you want a **fresh pair of eyes**
that isn't anchored on the reasoning that produced the code.

The convention that makes them safe: **every subagent here is read-only** — `tools: Read, Grep,
Glob, Bash` with Bash restricted to inspection. Edits happen in the main session where you approve
them. A subagent that can write is a subagent whose changes you didn't review.

`.claude/agents/code-reviewer.md`:

```markdown
---
name: code-reviewer
description: >
  Reviews recent code changes for quality, convention adherence, and likely bugs. Use right after
  writing or modifying code. Read-only — reports findings, does not edit.
tools: Read, Grep, Glob, Bash
model: sonnet
---

You are a senior code reviewer for the **<ProjectName>** repo (Aspire + ASP.NET Core + Angular).

Your job is to review changes and report — never modify files. Use `Bash` only for read-only
inspection such as `git diff` and `git status`.

## How to review
1. Look at what changed (`git diff`), then read surrounding code for context.
2. Check against this repo's conventions:
   - **Aspire:** resources declared in the AppHost; no hardcoded connection strings or
     `localhost:port`; services wired with `WithReference`/`WaitFor`; AppHost stays declarative.
   - **Backend:** thin controllers, no EF entities exposed at the boundary, async throughout,
     DbContext via the Aspire integration, input validated at the edge.
   - **Frontend:** standalone components, typed models mirroring the API's outbound types, HTTP
     through services, API base URL from injected config, no leaked subscriptions.
3. Flag likely bugs, missing error handling, and missing tests.

## Report format
- **Blockers** — must fix before merge (bugs, convention violations, hardcoded config, security)
- **Suggestions** — worth improving but not blocking
- **Nits** — style/minor

For each item: file + line, what's wrong, and the concrete fix. If nothing is wrong, say so plainly.
Be specific and brief.
```

The other three, in brief — full patterns follow the same shape:

- **`test-gap-analyzer`** — maps changed code to its tests and ranks what's missing *by risk*
  (untested validation and error branches over trivial getters). Run it before you call a slice
  done.
- **`api-contract-checker`** — the one that pays for itself in this stack. It diffs the API's
  outbound types against the Angular interfaces that mirror them. Two languages, no compiler
  spanning them: **drift here is invisible until runtime.** This subagent is the compiler you don't
  have.
- **`skills-evals`** — audits whether generated code actually followed the skill that was supposed
  to produce it. It closes the loop: when prose is your template engine, you need a test for the
  prose.

`skills-evals` contains the single sharpest instruction in the toolkit, worth quoting exactly:

> **Read the relevant SKILL.md first, every time.** Never audit from memory of what the skill says —
> skills change, and a stale rule in your head produces a confidently wrong finding.
>
> `CLAUDE.md` (Constraints + Restrictions) is the tiebreaker. **If a skill contradicts CLAUDE.md,
> that is itself a finding** — report the skill as the drifted artifact, and do not fault code for
> following the correct path.

That's a precedence order between your prose artifacts — `CLAUDE.md` > skills > code — and it means
the auditor can find bugs *in the template engine itself*, not just in the output.

### 2.4 Hooks — the only enforcement in the building

`.claude/settings.json`:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Edit|Write|MultiEdit",
        "hooks": [{ "type": "command", "command": ".claude/hooks/secret-guard.sh" }]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "Edit|Write|MultiEdit",
        "hooks": [{ "type": "command", "command": ".claude/hooks/format.sh" }]
      }
    ]
  }
}
```

`.claude/hooks/secret-guard.sh` — **`exit 2` denies the write.** This is the one that must not be
persuadable:

```bash
#!/usr/bin/env bash
# PreToolUse guard: deny writes containing secret-shaped strings. exit 2 = deny.
set -euo pipefail
payload="$(cat)"
patterns='(sk-[A-Za-z0-9_-]{16,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|password[[:space:]]*=[[:space:]]*["'\''][^"'\'' ]{6,}|(postgres|redis)://[^:@/]+:[^@/]+@)'
if printf '%s' "$payload" | grep -Eiq "$patterns"; then
  echo "secret-guard: blocked a write with a secret-shaped string. Use Aspire-injected config, not literals." >&2
  exit 2
fi
exit 0
```

Note how it pairs with the Restriction "no hardcoded connection strings": the prose teaches the
*habit*, the hook makes the habit *unskippable*. Belt and braces, and they reinforce each other —
the hook's error message even restates the rule.

`.claude/hooks/format.sh` formats the edited file. It carries a lesson that cost real debugging time:

```bash
#!/usr/bin/env bash
# PostToolUse hook: format the file Claude just edited. Never blocks the edit.
set -euo pipefail
input="$(cat)"

if command -v jq >/dev/null 2>&1; then
  file="$(printf '%s' "$input" | jq -r '.tool_input.file_path // empty')"
else
  file="$(printf '%s' "$input" \
    | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -1 | sed 's/.*"file_path"[[:space:]]*:[[:space:]]*"//; s/"$//')"
fi

[ -z "${file:-}" ] && exit 0
[ -f "$file" ] || exit 0

case "$file" in
  *.cs)
    dotnet format --include "$file" >/dev/null 2>&1 || true
    ;;
  *.ts|*.html|*.scss|*.css|*.json)
    # prettier is a devDependency of the Angular app, not of the repo root. A bare `npx prettier`
    # here finds no node_modules at the root and silently downloads a floating version from the
    # registry — so the hook would format with a different prettier than the app pins, and would
    # need the network to run at all. Invoke the app's own pinned binary instead.
    prettier="$(git rev-parse --show-toplevel)/src/web/node_modules/.bin/prettier"
    if [ -x "$prettier" ]; then
      "$prettier" --write "$file" >/dev/null 2>&1 || true
    else
      # Not silently: a missing formatter should be fixable, not invisible. Still exit 0 — a format
      # hook never blocks an edit.
      echo "format.sh: prettier not installed; skipped $file (run: npm ci --prefix src/web)" >&2
    fi
    ;;
esac
exit 0
```

Three transferable lessons in that file:

1. **`|| true` on a formatter, never on a guard.** A format failure must not block an edit; a secret
   must.
2. **Invoke the pinned binary, not `npx <tool>`.** The original ran `npx prettier` from the repo
   root, which has no `node_modules` — so it silently fetched a floating version from the network, or
   did nothing at all. `|| true` swallowed the evidence. **A hook that fails silently is worse than
   no hook**, because you believe you're covered.
3. **Missing tooling gets a message, not silence.** Note the `else` branch still exits 0 — it warns
   without blocking. Fixable beats invisible.

After cloning, hooks need the executable bit — this is the one setup step people miss:

```bash
chmod +x .claude/hooks/*.sh
```

### 2.5 Six names you should never brand

This is the failed template's most valuable inheritance. Five of six coupling variables were branded
for **no reason at all** — they were never domain concepts, just names someone typed once. Choose
neutral from day one and there is nothing to rename, ever:

| Variable | The branded trap | Use instead | Renamed later by |
|---|---|---|---|
| DB resource | `recipesdb` | **`appdb`** | nobody — static forever |
| Blob container | `recipe-images` | **`uploads`** | nobody |
| CSS token prefix | `--rb-` | **`--app-`** | nobody |
| E2E override env | `RECIPEBOX_WEB_URL` | **`APP_WEB_URL`** | nobody |
| Web project path | `src/RecipeBox` | **`src/web`** | nobody |
| DbContext | `RecipeDbContext` | **`AppDbContext`** | nobody |
| **Project name** | `RecipeBox` | *your name* | **you, once, at `aspire new`** |

The discipline: **exactly one name in the repo carries your brand — the project name — and you
choose it at creation.** Everything else is structural and stays neutral. Your *domain* nouns
(`CaseStudy`, `Inquiry`) live in entities and routes where they belong; they never leak into
infrastructure identifiers.

The payoff compounds. `AppDbContext` means the test factory's pooled-context descriptor loop keys on
a permanently generic type. `src/web` means the frontend rule's `paths:` glob never changes.
`APP_WEB_URL` means the sharpest trap in the table simply doesn't exist. You get all of that for
free, by typing a different string once.

---

## Part 3 — The architecture: `add-endpoint`

This skill is the deliverable. Everything else is scaffolding around it. It encodes a strict layered
pipeline that Claude follows on every route — which is what makes an agent-built API stay coherent
across a hundred endpoints instead of becoming a hundred personal styles.

### The pipeline

```
Controller  →  Facade              →  Business                →  DataLayer          →  Repository
  (HTTP:        (validate the VM +      (translate VM→domain,      (compose repository    (EF queries;
   VM in,        cache; return SM)       apply domain rules,        calls into whole       returns
   SM out)                               domain→SM)                 data operations)       entities)
```

Each layer depends on the **interface** of the one below it
(`I<F>Facade` → `I<F>Business` → `I<F>DataLayer` → `I<F>Repository`), never on a concrete class or a
lower layer's dependencies.

### The three model types — the core idea

A request enters as a **ViewModel** and a response leaves as a **ServiceModel**. Those are the only
types on the wire. In between, work is done on **Domain** entities.

**There is no separate DTO layer** — and that's deliberate, not an omission. The domain entity *is*
the internal shape, so a loaded entity maps directly to a service model. You get the guarantee that
matters (no EF entity ever reaches the controller; no view model ever reaches the DB) without the
ceremony of a fourth type that's a field-for-field copy of a third.

| Type | Folder | Lives between | Who creates it |
|---|---|---|---|
| **ViewModel** | `Managers/Models/ViewModels/` | client → controller → facade | model binder |
| **Domain** entity | `Managers/Models/Domain/` | business ↔ data ↔ EF | business (from the VM) / EF (on load) |
| **ServiceModel** | `Managers/Models/ServiceModels/` | business → facade → controller → client | business (from the entity) |

### Layout (type-first)

```
<ProjectName>.ApiService/
├── Controllers/            # <Feature>Controller.cs — HTTP surface (ViewModel in, ServiceModel out)
├── Facade/                 # I<Feature>Facade  + <Feature>Facade   (validate VM + cache + return SM)
├── Business/               # I<Feature>Business + <Feature>Business (VM→domain, rules, domain→SM)
├── Data/                   # AppDbContext
│                           #   I<Feature>DataLayer  + <Feature>DataLayer  (compose data operations)
│                           #   I<Feature>Repository + <Feature>Repository (EF queries)
├── Managers/
│   ├── Validators/         # FluentValidation validators for the view models
│   ├── Models/{ViewModels,ServiceModels,Domain}/
│   ├── Mappers/            # VM→domain, domain→ServiceModel (extension methods)
│   └── Infrastructure/     # cross-cutting (e.g. the global exception handler)
├── Migrations/
└── Program.cs
```

### Strict responsibilities

- **Controller** — HTTP only: bind the ViewModel, call the facade, return
  `ActionResult<ServiceModel>`. No validation, cache, logic, or data access; never sees an entity.
- **Facade** — the boundary: **validates** the ViewModel, handles **caching** of ServiceModels
  (read-through on queries, invalidate on writes), returns ServiceModels. No orchestration, mapping,
  or EF.
- **Business** — **domain rules and translation**: ViewModel → Domain entity, apply data-dependent
  rules (*"reject a second inquiry from this email within 24 hours"*), map Domain → ServiceModel.
  No validation, caching, or EF.
- **DataLayer** — **composes data operations**: turns one logical read or write into however many
  repository calls it takes, so business asks once and does no sequencing. Owns the **transaction
  boundary**. Holds no `DbContext`.
- **Repository** — **data only**: EF queries against the Aspire-provided `DbContext`. Detail reads
  and writes return the Domain entity; a **list** read projects straight to its summary ServiceModel
  in SQL (counts without materializing child rows — the one place data touches an outbound model, to
  keep the projection).

### The two hard-won bits

Anyone can write "thin controllers." These two are why the skill is worth reading:

**1. Where a rule goes when it's ambiguous.** The split between business and data layer is by
*reason*, not by call count:

> If the sequencing is a **domain** decision — some rule says what may happen — it's business. If
> the sequencing is a **persistence** consequence — the store simply has to be left consistent —
> it's the data layer. **Ask what a reviewer would call the extra call: a rule, or bookkeeping.**

The unique-name check reads before it writes, and stays in business, because "names are unique" is a
rule. Reaping orphaned taxonomy rows on delete is the data layer, because it has to happen whether
or not anyone asks.

That heuristic is the difference between a layering people follow and one they argue about.

**2. Why the transaction is a callback.** This shape is *forced*, not chosen — and the reason is
pure Aspire:

```csharp
var result = await _repository.ExecuteInTransactionAsync(
    async token =>
    {
        // ... the repository calls that make up the operation ...
        return whatever;   // a throw on any leg rolls the whole thing back
    },
    ct);
```

Aspire's Npgsql integration enables **retry-on-failure**, and its execution strategy refuses to run
inside a transaction the caller opened itself — *"does not support user-initiated transactions."* It
cannot replay a unit whose boundaries it doesn't own. Passing the whole unit in is what lets
resilience and atomicity coexist.

Two consequences to internalize:

- The operation **may run more than once**, so it must be safe to repeat.
- **Only work done through this repository is rolled back** — anything touching another store
  (blobs, a queue) belongs outside the callback.

This is exactly the kind of knowledge that is invisible in code review and catastrophic in
production. It belongs in a skill, where the agent reads it *before* writing the code, not in a
comment where it's discovered afterward.

### The steps, condensed

The skill walks 13 numbered steps; the order is the point — **bottom-up, so every layer is written
against something that already exists:**

1. **ViewModel** → the inbound type · 2. **ServiceModel** → the outbound type · 3. **Validator**
(shape/format only — data-dependent rules go in business) · 4. **Mappers** (`ToEntity()`,
`ToServiceModel()`) · 5. **Repository** · 6. **DataLayer** · 7. **Business** · 8. **Facade** ·
9. **Controller** · 10. **DI wiring** · 11. **Cache backing** · 12. **Tests per layer** ·
13. **Migration** (only if the model changed).

Step 10, verbatim, because it prevents a specific recurring bug:

```csharp
builder.Services.AddScoped<I<Feature>Repository, <Feature>Repository>();
builder.Services.AddScoped<I<Feature>DataLayer, <Feature>DataLayer>();
builder.Services.AddScoped<I<Feature>Business, <Feature>Business>();
builder.Services.AddScoped<I<Feature>Facade, <Feature>Facade>();
```

> Validators need no registration of their own: `Program.cs` already calls
> `AddValidatorsFromAssemblyContaining<Program>()`, which picks up every validator in the assembly.
> **Don't add a second `AddValidatorsFromAssemblyContaining` line per feature.**

Note it keys on `<Program>`, not on some specific validator type. Keying on a domain type is how you
get a registration line that has to be edited every time the domain changes — and an agent that
helpfully adds a redundant one per feature.

### Testing — per layer, mock the layer below

The layering earns its keep here. Each layer has exactly one collaborator to fake:

- **Repository:** integration test against containerized Postgres.
- **DataLayer:** unit test with a mocked repository — right calls, **right order**, commits last,
  short-circuits correctly, does **not** commit when a leg throws. **A mocked transaction only
  proves a commit was *asked for*** — so back any atomic composition with one **real-database** test
  that a mid-composition failure leaves the store untouched.
- **Business:** mocked data layer — mapping, list pass-through, and the domain rule.
- **Facade:** mocked business, **real validator**, in-memory cache — cover a cache **hit**, a cache
  **miss**, and a **validation failure**.
- **Endpoint:** `WebApplicationFactory` — happy path plus one validation failure.

That mocked-transaction caveat is the most valuable sentence in the testing section. It's the
difference between a green suite and a correct one.

### The checklist

Copy this verbatim into your skill. It's what `skills-evals` audits against, and what turns "follow
the architecture" into something falsifiable:

```markdown
## Checklist before done
- [ ] Files live in the type-first folders — controller/facade/business/data at the project root;
      validators, models, mappers, infrastructure under `Managers/`
- [ ] Only ViewModels enter and only ServiceModels leave — no EF entity crosses the controller boundary
- [ ] Controller does HTTP only — no validation, cache, logic, or data access
- [ ] Facade owns validation + caching; no orchestration, mapping, or EF
- [ ] Business translates VM→domain, applies domain rules, maps domain→ServiceModel; no validation,
      cache, EF, or multi-call data sequencing
- [ ] DataLayer composes repository calls into whole data operations (pass-throughs where one call
      suffices); no rules, mapping, cache, validation, or `DbContext`
- [ ] Any DataLayer composition that writes more than once is wrapped in a transaction and commits
      only on success
- [ ] Repository returns domain entities (list projects to its summary ServiceModel) and does queries
      only, one self-contained data operation per method
- [ ] Each layer depends on the interface below it
- [ ] `DbContext` and cache obtained via the Aspire integrations (no hardcoded connection strings)
- [ ] Validation returns the shared error shape on failure
- [ ] Tests per layer pass, incl. facade cache-hit / cache-miss / validation-failure, the data
      layer's call-order/short-circuit assertions, and a real-database rollback test for any atomic
      composition (`dotnet test`)
- [ ] Migration reviewed and committed, `has-pending-model-changes` clean (if the model changed)
```

---

## Part 4 — The scaffolding prompts

Run these **in order, one at a time**. Each assumes `CLAUDE.md` and `.claude/` are already in place —
that's what makes them short. They're deltas against a standing prompt.

**How to run them:**

- **Read the plan, not the code.** Every prompt says *plan first, wait for approval*. This is the
  single biggest quality lever in the whole playbook — catching a wrong approach costs a sentence;
  catching it after the code exists costs an afternoon.
- **`/clear` between big steps.** Rules and skills reload automatically; stale logs don't.
- **`/rewind`** instead of stacking correction prompts onto a polluted context.

### Prerequisites

.NET 10 SDK · Node.js · Docker (running) · the `aspire` CLI · Claude Code.

Pin your toolchain before you start. In `global.json` — and note the second half, which is the piece
a naive setup misses:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" },
  "msbuild-sdks": { "Aspire.AppHost.Sdk": "13.4.6" }
}
```

The AppHost SDK arrives via an `Sdk=` attribute, so **Central Package Management cannot centralize
it.** Pin it here or it drifts independently of every other version in the repo.

### Prompt 0 — Scaffold the solution

```
SCOPE: Stand up the <ProjectName> solution skeleton only (no business logic). Create an Aspire 13
solution on .NET 10 with these projects under src/: <ProjectName>.AppHost (orchestrator),
<ProjectName>.ServiceDefaults, and <ProjectName>.ApiService (ASP.NET Core Web API). In the AppHost,
declare a local PostgreSQL resource with a database named "appdb", register the ApiService, and
register the Angular app in src/web so Aspire launches it.

CONSTRAINT: Follow .claude/rules/aspire.md. All resources are local containers. Verify the exact
Aspire commands, template names, and API (AddJavaScriptApp, package names) against
https://aspire.dev before running anything — do not guess.

RESTRICTION: Do NOT add domain models, endpoints, or UI yet. Do NOT hardcode any connection string
or localhost:port. Do NOT add cloud resources. Do NOT install packages you can't justify. Name the
database resource "appdb" and the web folder "src/web" — these stay neutral by design; do not brand
them with the project name.

UTILIZATION: Use the aspire CLI/templates; use the aspire-init or aspireify skill if available.
Use plan mode.

BEHAVIOR: First show me your plan: the exact projects, the AppHost resource wiring, and the commands
you'll run. Wait for my approval. Then scaffold, and finish by running `aspire run` and telling me
what the dashboard shows.
```

> **Why "verify against aspire.dev before running anything" is in every Aspire prompt:** this
> surface moves between versions. `AddNpmApp` became `AddJavaScriptApp`. A model that pattern-matches
> a plausible-but-retired API name produces code that fails at build, or worse, at startup. The
> rule file says it too — the prompt reinforces it at the moment it matters most.

### Prompt 1 — Domain model + EF Core

```
SCOPE: Add the <domain> to <ProjectName>.ApiService: entities <AggregateRoot> (<fields>),
<Child> (<fields>, ordered), and <Taxonomy>, with these relationships: <a Root has many ordered
Children; Roots and Taxonomy are many-to-many>. Register the EF Core DbContext (named AppDbContext)
through the Aspire Npgsql integration keyed to "appdb". Create the initial migration.

CONSTRAINT: Follow .claude/rules/backend.md and .claude/rules/aspire.md. Entity configuration goes
in one IEntityTypeConfiguration<T> class per entity under Managers/Models/Domain/; AppDbContext
keeps only its DbSet properties plus ApplyConfigurationsFromAssembly.

RESTRICTION: The DbContext MUST come from the Aspire integration, not a raw connection string in
appsettings. Do NOT apply the migration yet — create it and stop. Do NOT add API endpoints in this
step. Do NOT put entity configuration in OnModelCreating.

UTILIZATION: Use plan mode.

BEHAVIOR: Plan the entities and relationships and show me the model before writing. Wait for
approval. Generate the migration, show me the generated file, and stop for review before any
`database update`.
```

**Worked example (architect4hire):** entities `CaseStudy` (title, summary, client, year, published),
`Outcome` (metric, result, ordered), `Capability`; a CaseStudy has many ordered Outcomes, and
CaseStudies↔Capabilities are many-to-many.

> The `IEntityTypeConfiguration<T>` constraint is there because `OnModelCreating` becomes a domain
> dumping ground — a 57-line method every feature appends to and nobody reads. One config class per
> entity keeps `AppDbContext` a ~10-line file that a skill can edit trivially. **Better architecture
> anyway; free if you say so on day one.**

### Prompt 2 — The API

```
SCOPE: Implement the first endpoints in <ProjectName>.ApiService: list <roots> (with optional filter
by <taxonomy>), get one <root> with its <children> and <taxonomy>, and <the first write>. Include
view models, service models, validators, mappers, and tests.

CONSTRAINT: Follow .claude/rules/backend.md and the add-endpoint skill's layering exactly.

RESTRICTION: Do NOT expose EF entities across the API boundary. Do NOT put logic in controllers.
Everything async. Do NOT touch the Angular app. Do NOT add a second
AddValidatorsFromAssemblyContaining line — Program.cs already registers validators assembly-wide.

UTILIZATION: Use the add-endpoint skill for each route. When implementation is done, delegate the
review to the code-reviewer subagent, then the skills-evals subagent to audit the layering.

BEHAVIOR: Plan the endpoints, the three model types per route, and where the domain rule lives
(business vs data layer — say which and why) and wait for approval. Implement, run `dotnet test`
until green, then run the reviewers and summarize their findings for me.
```

**Worked example:** `GET /casestudies?capability=aspire`, `GET /casestudies/{id}`,
`POST /inquiries` — the domain rule (no second inquiry from an email within 24 hours) is
**business**, because "one inquiry per email per day" is a rule, not bookkeeping. It throws a domain
conflict exception that the global handler maps to 409.

The "say which and why" clause is doing real work: it forces the ambiguous-rule heuristic to be
applied *out loud, in the plan*, where you can correct it for the cost of one sentence.

### Prompt 3 — Angular shell + data service

```
SCOPE: Set up the Angular app in src/web (strict TypeScript, standalone components). Add a typed
<Root>Service on HttpClient plus model interfaces that mirror the API's service models exactly. The
service must read the API base URL from the Aspire-injected config/environment.

CONSTRAINT: Follow .claude/rules/frontend.md and .claude/rules/aspire.md. CSS custom properties use
the --app- prefix, not a project-branded one.

RESTRICTION: Do NOT hardcode the API URL. Do NOT call HttpClient from components — only from the
service. No `any`.

UTILIZATION: Use plan mode.

BEHAVIOR: Show me how you'll read the injected API URL and the shape of the models/service before
writing. Wait for approval. Implement, run `ng test`, and report.
```

### Prompt 4 — Components

```
SCOPE: Build the core UI: <root>-list (cards + <taxonomy> filter), <root>-detail (<children> +
<taxonomy>), <root>-form (create/edit), and a <taxonomy>-filter. Wire them to <Root>Service.

CONSTRAINT: Follow .claude/rules/frontend.md.

RESTRICTION: Standalone components only. Use the async pipe (or clean up subscriptions). Models must
match the service models exactly. Do NOT bypass <Root>Service.

UTILIZATION: Use the new-component skill for each component. Delegate the final review to the
code-reviewer subagent and a contract check to api-contract-checker.

BEHAVIOR: Plan the component tree and data flow, wait for approval, implement, run `ng test`, run
the reviewers, and summarize.
```

**Worked example:** `case-study-list` (cards + capability filter), `case-study-detail` (ordered
outcomes + capability chips), `inquiry-form` (the public lead capture), `capability-filter`.

### Prompt 5 — End-to-end run + verification

```
SCOPE: Bring the whole system up and verify it works end to end: `aspire run`, then confirm the
dashboard shows Postgres, the API, and the Angular app all healthy, and that the <root> list loads
in the browser from real data. Apply the pending migration if needed.

CONSTRAINT: Follow .claude/rules/aspire.md.

RESTRICTION: Fix only wiring/config issues that block the end-to-end flow. Do NOT add new features
or refactor unrelated code.

UTILIZATION: Use the aspire CLI and dashboard; use the test-gap-analyzer subagent to tell me what's
under-tested before I call this done.

BEHAVIOR: Report a short health summary (each resource, up/down), what you fixed, and the
test-gap-analyzer's prioritized list of missing tests. Ask before applying the migration.
```

### Prompt 6 — Add the cache

Only if your domain card says cache: yes. This one exists mainly to prove the toolkit works — you're
now adding infrastructure through a skill rather than by hand:

```
SCOPE: Add a local Redis cache to the AppHost and use it to cache the <root> list, with sensible
invalidation on create/update.

CONSTRAINT: Follow .claude/rules/aspire.md and the add-endpoint skill — caching lives ONLY in the
facade, and it caches ServiceModels.

RESTRICTION: Local container only. No hardcoded connection details — wire via WithReference and the
Aspire client integration. Keep the AppHost declarative. Do NOT put cache logic in business, the
data layer, or the repository.

UTILIZATION: Use the add-aspire-resource skill. Delegate the review to the code-reviewer subagent.

BEHAVIOR: Plan the cache wiring and invalidation strategy, wait for approval, implement, verify in
the dashboard, run the reviewer, and summarize.
```

### Prompt 7 — CI

```
SCOPE: Add .github/workflows/ci.yml running on push to main and on PRs, with three jobs: backend
(setup-dotnet using global-json-file → restore → build -warnaserror → dotnet test), frontend
(npm ci → ng test --watch=false → npm run build), and format.

CONSTRAINT: The format job MUST invoke exactly what .claude/hooks/format.sh invokes — same tools,
same pinned versions.

RESTRICTION: Do NOT add an e2e job to the PR workflow — it needs the aspire CLI, Docker, and every
backing container up, and a flaky required check is worse than no check. Put e2e in a separate
workflow on workflow_dispatch plus a nightly schedule.

UTILIZATION: Use plan mode.

BEHAVIOR: Plan the workflow file, wait for approval, then implement.
```

> **Why the format job must mirror the hook exactly:** otherwise the hook formats one way and CI
> fails the other, and every PR turns into a formatting argument between two of your own tools.
> Same reason `TreatWarningsAsErrors` belongs in CI (`-warnaserror`) rather than in
> `Directory.Build.props` — you want the ratchet in the gate, not in every local build.

---

## Part 5 — Operational templates

Scaffolding is one-time. **These are forever.** Copy a block, fill the `<...>`, delete lines that
don't apply.

Once scaffolded, most day-to-day work needs no bespoke prompt — the rules, skills, subagents and
hooks carry the structure, so "add a published filter to the case study list" is enough. Reach for
these only when a task is **non-trivial or risky**.

### Template A — Feature delivery (vertical slice)

*Your everyday default, for any capability spanning API + UI.*

```
SCOPE: Deliver <feature> end to end: <API change> and <UI change>. Scope is this feature only.

CONSTRAINT: Follow the rules in .claude/rules/. Match existing patterns rather than inventing new
ones.

RESTRICTION: Do NOT change unrelated files, schemas, or public API contracts. Do NOT add
dependencies without asking. No hardcoded config.

UTILIZATION: Use the add-endpoint and/or new-component skills. Delegate review to the code-reviewer
subagent.

BEHAVIOR: Plan the vertical slice (data → API → UI) and wait for approval. Implement in small steps,
run `dotnet test` and `ng test` green, run the code-reviewer, and summarize.
```

### Template B — Database / migration change

*Migrations are the closest thing here to irreversible. The guardrails are deliberately tight.*

```
SCOPE: Make this schema/data change: <describe>. Produce the EF migration and update the affected
models, queries, and tests.

CONSTRAINT: Follow .claude/rules/backend.md and .claude/rules/aspire.md. The DbContext comes from
the Aspire integration.

RESTRICTION: Create the migration but do NOT run `dotnet ef database update` until I approve. Do NOT
drop or rename columns without an explicit, reversible plan. Do NOT run destructive SQL or touch
existing data without a stated rollback. No raw connection strings.

UTILIZATION: Use plan mode. After the migration is generated, delegate to the code-reviewer.

BEHAVIOR: Show me three things before applying: (1) the model change, (2) the generated migration
file, (3) the rollback story. Wait for approval. Then apply, confirm the schema, and run the tests.
```

### Template C — Refactor / cross-cutting change

*The point is Scope discipline — stopping "while I'm here" sprawl.*

```
SCOPE: Refactor <target> to <goal>. Behavior must not change. Before editing, list every file you
intend to touch and why.

CONSTRAINT: Follow the rules in .claude/rules/. Keep public API contracts and test expectations
stable.

RESTRICTION: Do NOT change behavior or unrelated code. Do NOT expand beyond the listed files without
checking in first. No opportunistic edits.

UTILIZATION: Use plan mode; use the Explore subagent to map usages first. Delegate review to the
code-reviewer subagent.

BEHAVIOR: First return the impact map (files + reason). Wait for approval. Refactor in small,
test-green steps — run the suite after each. If the blast radius grows beyond the map, stop and
re-plan with me.
```

The impact map is the whole trick. A refactor that names its files up front has a **falsifiable
scope** — when the blast radius grows, both of you can see it, and "stop and re-plan" has a trigger
rather than being a vibe.

### Template D — Debug / harden

```
SCOPE: Diagnose and fix <bug/symptom>, and add a regression test.
  (Harden variant: review <area> for correctness, security, and missing tests.)

CONSTRAINT: Follow the rules in .claude/rules/.

RESTRICTION: Make the MINIMAL change that fixes the root cause — do NOT refactor around it or
suppress the symptom without understanding the cause. No new dependencies.

UTILIZATION: Use the Explore subagent to locate the cause; use the test-gap-analyzer for coverage
gaps; run /security-review for the harden variant.

BEHAVIOR: First reproduce the issue and state your root-cause hypothesis with evidence. Wait for my
nod on the diagnosis. Then apply the minimal fix, add a regression test, run the suite, and
summarize what changed and why.
```

"State your root-cause hypothesis with evidence, wait for my nod on the **diagnosis**" is the
important line. Approving a diagnosis is cheap and fast; the failure mode it prevents — a confident
fix to the wrong thing, plus a regression test that locks in the misunderstanding — is expensive.

---

## Part 6 — Definition of done

Run this before you believe any of it:

| Check | Green means |
|---|---|
| `dotnet build && dotnet test` | Backend compiles and passes |
| `cd src/web && npm ci && npm run build && ng test` | Frontend compiles and passes |
| `aspire run` | Dashboard shows every resource healthy; the app loads real data |
| `dotnet ef migrations has-pending-model-changes` | Model and migrations agree |
| **`/memory`** | **All three rules load** |
| `@skills-evals` on the first slice | The generated code actually followed the skill |
| `@api-contract-checker` | The Angular models match the API's outbound types |

Two of those deserve emphasis.

**`/memory` is the one people skip.** A `paths:` glob that silently stops matching produces no
error — it produces an agent that quietly stops following your backend conventions. Everything
downstream still *looks* fine. Check it after any restructuring.

**`@skills-evals` is the test for your prose.** If your architecture is enforced by documents, then
the documents are production code, and untested production code rots. This is the only thing
standing between "we have a layering" and "we had a layering."

---

## The habits that make it stick

- **Approve the plan, not the code.** The value is catching a wrong approach *before it exists*. If
  the plan is off, correct it and re-plan rather than editing after the fact.
- **One prompt = one clean context.** `/clear` before a new big task. Rules and skills reload
  automatically; your stale logs don't.
- **Let the guardrails work.** You wrote the "no hardcoded connection strings" rule, taught the
  reviewer to block it, and made the hook deny it. The RESTRICTION line just reinforces it at the
  moment of maximum temptation.
- **Use `/rewind`** instead of stacking correction prompts on a polluted context.
- **Promote repeats to skills.** If you fill in the same operational template two or three times,
  that recurring shape wants to be a skill. Write it, and the prompt disappears. **This is the
  engine of the whole system** — every skill here started as a prompt someone got tired of typing.
- **Fix drift where it starts.** When the agent does something wrong, don't just correct the output
  — ask which SCRUB section failed, and fix *that*. Scope, Constraint, Restriction, Usage, Behavior:
  one of them was wrong or missing. The correction that only fixes today's code will be needed again
  tomorrow.

---

## Why this holds

Every reusable-architecture story eventually confronts the same fork: **encode the architecture in
code, or in prose?**

Code is enforced but rigid — a base class or a generic repository dictates the shape and fights you
at every edge case. Prose is flexible but unenforced. The usual answer is code, because prose can't
be checked.

This playbook takes the other fork, on the grounds that the tradeoff has actually changed:

1. **Prose is now executable.** A skill isn't documentation the agent might read — it's the
   procedure it follows.
2. **Prose is now testable.** `skills-evals` audits code against the skill that was meant to produce
   it. `api-contract-checker` is a compiler for a boundary no compiler spans. `/memory` verifies the
   rules load. That's a test suite for your documents.
3. **Prose has no rename tax.** Nothing to find-and-replace. Nothing to `dotnet clean`. No
   `UserSecretsId` silently shared across every clone. **You don't inherit a name; you choose one.**

That's the whole method. Write the architecture down carefully, put it where the agent reads it
before writing, hook the things you can't afford to have skipped, and audit the prose like code.

Then hand it a domain card and let it build.

---

*Built from [claudebox](https://github.com/) — a working Aspire + ASP.NET Core + Angular showcase
where every pattern above is compiled, tested, and running. The playbook is the reusable half; the
repo is the fully-worked example.*
