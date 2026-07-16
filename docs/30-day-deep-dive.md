# RecipeBox — Remaining Build Plan (detailed, day-by-day)

You've already built Weeks 1–2 and part of Week 3 (Aspire skeleton, Postgres, **Redis cache**, the full layered API, all four Angular components, the whole test suite, three skills, two subagents, and the formatter hook). This plan covers **only what's left**, broken into 11 focused days you can review before you touch anything. Each day is one clean deliverable.

**Repo facts baked into these steps** (so nothing drifts):

- Angular app lives at **`src/RecipeBox`** (AppHost points at `../RecipeBox`). Ignore any earlier "move to `src/client`" note — you're consistent as-is.
- Aspire resources are named `postgres`, `recipesdb`, `cache`, and the web app `web`.
- API is layered **Controller → Facade → Business → Data**; models split into ViewModels / ServiceModels; migrations auto-apply on `aspire run` in Development.
- Tests live in **`src/RecipeBox.Tests`**; run from repo root with `dotnet test`.

**The daily loop (same as before):** `/clear` → give the instruction → **read the plan, approve it** → let it work (hooks fire) → verify → `/rewind` if it goes bad → `git commit` → jot two lines in a build log.

> ⚠️ = a version-sensitive command (Aspire/Claude Code ship monthly). Confirm it against the linked docs before running.

---

## Day 1 — Verify the whole stack actually works *(~45 min)*

You moved fast; lock in that everything runs before adding more. This day writes almost no code — it's a checkpoint.

**Goal:** green suites, a healthy dashboard, and a confirmed cache-invalidation *behavior* (not just the wired resource).

**Steps:**

1. **Start the system:** from the repo root, `aspire run`. Wait for the dashboard.
2. **Confirm four healthy resources:** `postgres`, `cache`, `api`, `web` — all green. Open the `api` logs and confirm migrations applied on startup (no schema errors).
3. **Click the app** (open `web`'s endpoint): list → click a recipe → detail shows ingredients and steps **in order** → create a recipe → edit it → filter by category. Note anything broken.
4. **Backend tests:** in a second terminal, from the repo root, `dotnet test`. Expect green.
5. **Frontend tests:** from `src/RecipeBox`, `ng test --watch=false --browsers=ChromeHeadless`. Expect green.
6. **Prove cache invalidation** (the Redis resource is wired for read-through caching — confirm the write path busts it): call `GET /recipes` (via the app or Swagger), create a new recipe, call `GET /recipes` again. **The new recipe must appear.** If it doesn't, the create path isn't invalidating the list cache.
   - If it's stale, `/clear` and: *"The recipe list is served from the Redis cache but a newly created recipe doesn't appear until the cache expires. Find where the list is cached and invalidate (or update) it on create/update/delete. Follow `.claude/rules/backend.md`. Plan first, then add a test that proves a created recipe shows up immediately."*
7. **Commit:** `git commit -am "day 1: verified stack + cache invalidation"`.

**If it breaks:** `web` unhealthy → the `PORT`/start-script wiring; check `src/RecipeBox/package.json`. A test hangs → make sure you passed `--watch=false`. `api` unhealthy → open its logs; a migration or DB-connection error shows there.
**Learned:** how to read your own system's health before trusting it.

---

## Day 2 — Finish the guardrails: the secret-guard hook *(~30 min)*

You have the `PostToolUse` formatter. The missing half of your Restrictions layer is a `PreToolUse` guard that *blocks* secret-shaped writes deterministically.

**Goal:** a write containing a secret-shaped string is denied before it lands.

**Steps:**

1. **Create the hook script** at `.claude/hooks/secret-guard.sh`. This is a robust starting point that scans the tool-call payload for secret-shaped strings and denies on a match (⚠️ confirm the current PreToolUse input shape + exit-code-2 "deny" semantics at https://code.claude.com/docs/en/hooks):
   
   ```bash
   #!/usr/bin/env bash
   # PreToolUse guard: deny writes containing secret-shaped strings. exit 2 = deny.
   set -euo pipefail
   payload="$(cat)"
   patterns='(sk-[A-Za-z0-9]{16,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|password[[:space:]]*=[[:space:]]*["'\''][^"'\'' ]{6,}|(postgres|redis)://[^:@/]+:[^@/]+@)'
   if printf '%s' "$payload" | grep -Eiq "$patterns"; then
    echo "secret-guard: blocked a write with a secret-shaped string. Use Aspire-injected config, not literals." >&2
    exit 2
   fi
   exit 0
   ```

2. **Make it executable:** `chmod +x .claude/hooks/secret-guard.sh`.

3. **Wire it in `.claude/settings.json`** alongside your existing formatter (keep the `PostToolUse` block you already have):
   
   ```json
   {
    "hooks": {
      "PreToolUse": [
        { "matcher": "Edit|Write|MultiEdit",
          "hooks": [ { "type": "command", "command": ".claude/hooks/secret-guard.sh" } ] }
      ],
      "PostToolUse": [
        { "matcher": "Edit|Write|MultiEdit",
          "hooks": [ { "type": "command", "command": ".claude/hooks/format.sh" } ] }
      ]
    }
   }
   ```

4. **Test the deny path:** `/clear`, then ask Claude to write a throwaway file containing `const k = "sk-live-abcdef0123456789xyz";`. The write should be **blocked**. `/rewind` the attempt.

5. **Test the allow path:** ask for a normal one-line edit and confirm it goes through (and the formatter still fires). Commit.

**If it breaks:** guard doesn't block → it must exit non-zero (2), and the matcher/event name must match your version's schema (re-check the hooks doc). Formatter stopped firing → you replaced the `PostToolUse` block instead of keeping it; both must be present.
**Learned:** the deny half of the hook lifecycle — a Restriction that's code, not a hope.
**Docs:** Hooks https://code.claude.com/docs/en/hooks

---

## Day 3 — A second read-only subagent (+ a skill-eval doc) *(~30 min)*

You have `code-reviewer` and `test-gap-analyzer`. Add one narrow specialist that catches a class of bug you actually risk.

**Goal:** a third read-only subagent that reports something specific by file + field.

**Steps:**

1. **`/clear`**, then have Claude write it (contract-drift is a great fit — you have API DTOs and Angular models that must stay in sync):
   
   > "Create a read-only `api-contract-checker` subagent in `.claude/agents/` with tools Read, Grep, Glob only. Its job: compare the API's ViewModels/ServiceModels in `src/RecipeBox.ApiService/Managers/Models` against the Angular interfaces in `src/RecipeBox/src/app/models/recipe.models.ts`, and report every field-name or type mismatch as `file → field → issue`. Give it a sharp `description` that triggers on phrasings like 'check contract drift' or 'do the DTOs and models match'. Show me the file before writing."
   > *(Prefer `migration-reviewer` instead if EF migrations worry you more — it flags destructive column drops/renames.)*

2. **Keep it read-only.** Read/search tools only — a subagent can't surface permission prompts, so it must never edit; the main session makes fixes.

3. **Run it** against the current code and confirm the output is specific (names the file and field), not vague. Fix any real drift it finds.

4. **(Optional, 10 min) `docs/skill-evals.md`** — 3–5 concrete "did it do the right thing?" checks per skill (e.g., add-endpoint: "returns DTOs not EF entities", "controller stays thin", "validation covered by a test"). Your tests prove the code; this documents the skill bar.

5. **Commit.**

**If it breaks:** output too generic → tighten its system prompt to demand `file → field → issue` and forbid general commentary. Doesn't trigger → the `description` describes what it *is* instead of *when to use it*; rewrite as trigger conditions.
**Learned:** the subagent discipline — sharpen the `description` first, keep it read-only, widen only when proven.
**Docs:** Subagents https://code.claude.com/docs/en/sub-agents

---

## Day 4 — Connect MCP: Postgres + Playwright *(~30–45 min)*

Give Claude Code eyes on your database and browser.

**Goal:** Claude can query `recipesdb` and open the app through MCP tools.

**Steps:**

1. **Connect two servers** and manage with `/mcp`: a **Postgres MCP** (inspect/query `recipesdb`) and a **Playwright MCP** (drive the Angular UI). ⚠️ MCP setup and config location move — check the current MCP docs from the hub before wiring. Keep it to these two.
2. **Mind the trust boundary.** MCP servers are third-party code whose tools your agent can call — connect only ones you trust; credentials for the Postgres MCP should come from your env/Aspire, never pasted into chat.
3. **Prove Postgres MCP:** *"Using the Postgres MCP, show the row count for each recipe table and the 5 most recently created recipes."* You want real rows back.
4. **Prove Playwright MCP:** with `aspire run` up, *"Using the Playwright MCP, open the app and tell me the heading on the recipe-list page."*
5. **Commit** any project-level MCP config (never secrets).

**If it breaks:** server shows disconnected in `/mcp` → check its logs there; a bad connection string or missing binary is the usual cause. Playwright can't reach the app → confirm the `web` URL from the dashboard and pass that.
**Learned:** MCP — external tools as native capabilities, and their trust boundaries.
**Docs:** MCP course https://anthropic.skilljar.com/introduction-to-model-context-protocol · hub https://code.claude.com/docs

---

## Day 5 — A committed Playwright E2E spec *(~30–45 min)*

Turn the browser walkthrough into a durable regression test you'll reuse in CI.

**Goal:** a green, headless `recipe-flow` spec committed to the repo.

**Steps:**

1. **`/clear`**, with `aspire run` up in another terminal:
   
   > "Using the Playwright MCP, walk the full flow — create a recipe → view its detail (ingredients + ordered steps) → edit it → filter by category — and report any step that fails. Then turn the successful walk into a reusable spec at `src/RecipeBox/e2e/recipe-flow.spec.ts` using role/text selectors (not brittle CSS)."

2. **Harden selectors.** If it used fragile CSS, ask it to switch to `getByRole`/`getByText` so the test survives markup tweaks.

3. **Run headless:** ⚠️ confirm your Playwright runner, then e.g. `npx playwright test` from `src/RecipeBox`. Get it green.

4. **Commit** the spec.

**If it breaks:** flaky waits → have Claude add explicit `expect(...).toBeVisible()` waits instead of fixed timeouts. Test can't find the app → the base URL; wire it from an env var, don't hardcode.
**Learned:** promoting an agent-driven walkthrough into a committed regression test.
**Docs:** Playwright https://playwright.dev/docs/intro

---

## Day 6 — One feature through the full stack + observability *(~45 min, heavier)*

Ship a single feature routed deliberately through every primitive, then learn to read the traces it produces.

**Goal:** a feature built + reviewed + smoked across skill → subagent → hooks → MCP, and a trace you can read.

**Steps:**

1. **`/clear`**, then ship "search recipes by ingredient" (a genuinely useful feature you likely don't have yet):
   
   > "Deliver 'search recipes by ingredient' end to end. Plan first. Use the `add-endpoint` skill for the API (a filtered list route) and `new-component` for a search box wired to `RecipeService`. When code is done, run the `code-reviewer` subagent; the formatter + secret-guard hooks fire on their own; then smoke the search flow via the Playwright MCP. Follow `.claude/rules/`."
   > *(Already have search? Swap in any small vertical slice — the point is one feature through every layer.)*

2. **Watch the layers compose.** You should barely restate conventions — rules + skills carry them, the reviewer checks, hooks enforce, MCP smokes.

3. **Observability tour:** with `aspire run` up, open the dashboard → **Traces**, and find one `GET /recipes` request. Confirm spans for **API → Postgres → cache**. Check `/health` reflects the real dependencies.

4. **Verify** search works in the browser, tests green, review clean. Commit; note in your build log which layer earned its place (and any rule you'd move to a different layer).

**If it breaks:** search returns everything → the filter isn't applied in the query; check the repository/EF `Where`. Trace missing spans → confirm ServiceDefaults is added in the project you're hitting.
**Learned:** composing the whole toolkit on one feature — the "right tool, right layer" payoff — and reading your telemetry.
**Docs:** Aspire dashboard https://aspire.dev/dashboard/overview/

---

## Day 7 — UX polish pass *(~30–45 min)*

Move the app from "works" to "shippable." Check what's already there before adding.

**Goal:** graceful loading / empty / error states, pagination, and a basic a11y pass.

**Steps:**

1. **`/clear`**, then:
   
   > "In `src/RecipeBox`, add pagination to the recipe list and proper loading / empty / error states everywhere data is fetched. Then do an accessibility sweep — form labels, focus order, color contrast, keyboard nav on list and detail. Follow `.claude/rules/frontend.md`. Plan first, implement, then `ng test`."

2. **Verify like a user:**
   
   - Throttle the network in browser dev tools → you should see loading states.
   - Point at an empty DB (or filter to a category with nothing) → empty state, not a blank page.
   - Stop the API (Ctrl+C the `api` resource) → the UI shows an error state, not a silent hang.

3. **`ng test`** green from `src/RecipeBox`; commit.

**If it breaks:** spinner never resolves → the loading flag isn't cleared in the error branch; make sure `finally`/`catch` resets it.
**Learned:** the gap between compiling and shippable.

---

## Day 8 — Package the toolkit as a plugin *(~30 min)*

Bundle your `.claude/` work into one installable unit.

**Goal:** the plugin installs clean in a fresh session and its pieces load.

**Steps:**

1. **`/clear`**, then assemble it. ⚠️ Plugin/marketplace layout is the fastest-moving piece here — verify against the current plugins docs before finalizing.
   
   > "Package `.claude/` as a plugin: add `.claude-plugin/plugin.json` and a `marketplace.json` bundling the three skills (`add-endpoint`, `new-component`, `add-aspire-resource`), the three subagents (`code-reviewer`, `test-gap-analyzer`, `api-contract-checker`), and the hooks (`format.sh`, `secret-guard.sh`). Show me the manifests before writing. ⚠️ Verify the schema against the current Claude Code plugins docs."

2. **Install in a clean session** to prove it's real:
   
   - `/plugin marketplace add Architect4Hire/ClaudeBox`
   - `/plugin install recipebox@<marketplace-name>`

3. **Verify** each piece is available in that fresh session (skills fire, subagents listed, hooks present).

4. **Commit** and tag `plugin-v1`.

**If it breaks:** install fails → the manifest schema is off for your version; re-read the docs and fix `plugin.json`. Pieces don't load → paths inside the manifest must point at the real `.claude/` locations.
**Learned:** plugins — one installable unit for skills/subagents/hooks/MCP.
**Docs:** Plugins (hub) https://code.claude.com/docs · Official marketplace https://github.com/anthropics/claude-plugins-official

---

## Day 9 — One Agent SDK automation *(~45 min)*

Run the same agent loop headless, emitting structured output another step can consume.

**Goal:** a committed script under `tools/` that returns strict JSON (and does something real).

**Steps:**

1. **Install the SDK.** ⚠️ Confirm the package name from the docs — currently `@anthropic-ai/claude-agent-sdk` (Node) or `claude-agent-sdk` (Python).
2. **Pick one useful job** and have Claude write it:
   - **Seed generator** (recommended — you'll want data for the capstone): *"Write an Agent SDK script `tools/seed.mjs` that generates 12 realistic recipes as strict JSON matching the `POST /recipes` ViewModel — no prose, no code fences, JSON only — then POSTs them to the running API. Parse defensively (strip stray fences before JSON.parse)."*
   - **or Drift scanner:** emit `{ "mismatches": [ ... ] }` comparing DTOs to Angular models (the headless twin of Day 3's subagent).
3. **Run it** with `aspire run` up: `node tools/seed.mjs`. Confirm real output — and for the seeder, the recipes appearing in the browser.
4. **Commit.**

**If it breaks:** `JSON.parse` throws → the model wrapped output in ```json fences; strip them and tighten the prompt to "JSON only." Auth errors → the SDK needs credentials configured per its docs.
**Learned:** the Agent SDK — the agent loop, embeddable, driven to structured output.
**Docs:** Agent SDK (from the hub) https://docs.claude.com/en/docs/claude-code/overview

---

## Day 10 — CI + deployment thinking *(~45 min, heavier)*

Make the pipeline do the boring, reliable work — then produce deploy artifacts without deploying.

**Goal:** PRs get auto-reviewed + tested; coherent publish artifacts exist.

**Steps:**

1. **`/clear`**, then add CI. ⚠️ Confirm the current Claude Code GitHub Action usage.
   
   > "Add a GitHub Actions workflow at `.github/workflows/ci.yml` that runs on every PR: `dotnet test`, `ng test` (headless), the `src/RecipeBox/e2e/recipe-flow.spec.ts` Playwright job, and a Claude Code review on changed files. Put any credentials in repo secrets, never in the YAML. Show me the workflow before writing. ⚠️ Verify the Claude Code Action reference against the current docs."

2. **Open a throwaway PR** (trivial change on a branch) and watch the checks run in the PR's Checks tab. Try `/security-review` locally on the branch too.

3. **Deployment artifacts (don't deploy):** run `aspire publish` ⚠️ to emit a target (e.g., Docker Compose), read the AppHost's publishing manifest to see how `postgres`/`cache`/`api`/`web` map to deployable units, and commit the artifacts (or a `docs/deployment.md` note describing them).

4. **Commit / merge** once green.

**If it breaks:** review job fails on auth → credentials must be repo secrets referenced by the Action, per its docs. `aspire publish` unknown → the command/targets changed; check the deployment docs for the current invocation.
**Learned:** headless/CI reuses your settings, hooks, and permissions; the local→deployable path.
**Docs:** Claude Code GitHub Actions (hub) https://docs.claude.com/en/docs/claude-code · Aspire deployment https://aspire.dev/deployment/overview/

---

## Day 11 — Capstone: seed, docs, demo, ship *(~45–60 min, heavier)*

Make the "built with Claude Code" story land in five minutes.

**Goal:** a fresh clone runs from the README, and the SCRUB→toolkit method is documented.

**Steps:**

1. **Seed real data:** run your Day-9 seeder (or add a dozen recipes by hand) so the app looks alive.
2. **Fresh-clone test:** clone the repo to a new folder, follow your own README steps, and confirm `aspire run` comes up healthy. If it doesn't, your docs are wrong — fix the docs, not your memory of the steps.
3. **README polish:** `/clear`, then *"Update the root README: add an architecture diagram (Aspire resources → API → Angular, with Postgres + cache), embed a demo GIF/screenshots, and link `docs/scrub-prompts.md` and this build plan so a reader sees exactly how it was built. Plan the structure first."*
4. **Operating manual:** create `docs/operating-manual.md` — one page mapping SCRUB → your toolkit: Scope → `CLAUDE.md` + skill descriptions; Constraints → `.claude/rules/` + skill bodies; Restrictions → hooks + read-only subagents; Utilization → skills / MCP / plugin / SDK; Behavior → subagent prompts + skill bodies.
5. **Record** a short screen capture of the dashboard + app for the README.
6. **Tag `v1.0`** and push.

**If it breaks:** fresh clone won't start → a missing prerequisite or an undocumented step; add it to the README's setup section.
**Learned:** the difference between a repo that works and one that *teaches* how it was built.

---

## Remaining definition of done

- Both suites green; four resources healthy; cache invalidation confirmed.
- Secret-guard hook blocking; a third read-only subagent in use.
- MCP inspection working; a committed, green Playwright E2E spec.
- One feature shipped through the full stack; traces readable.
- UX states + a11y pass; plugin installs clean; one Agent SDK automation.
- CI reviews + tests every PR; publish artifacts exist.
- Seeded demo, README with diagram, and a SCRUB→toolkit operating manual; tagged `v1.0`.

## Suggested pacing

You cleared ~two weeks in a day, so these 11 are deliberately light — most are 30–45 min. Natural pairings if you want to move faster: **Days 4+5** (MCP then the E2E spec that uses it), **Days 8+9** (plugin then SDK), **Days 10+11** (CI/deploy then capstone). Days 1, 2, and 6 are the ones worth not rushing — Day 1 because it de-risks everything after it, Day 2 because a guardrail is worth getting right, Day 6 because it's where the whole toolkit proves it composes.

> ⚠️ recap — confirm these against current docs before running: the PreToolUse hook schema (Day 2), MCP setup (Day 4), the Playwright runner (Day 5), plugin/marketplace layout (Day 8), the Agent SDK package name (Day 9), and the Claude Code Action + `aspire publish` (Day 10). Everything else is stable.