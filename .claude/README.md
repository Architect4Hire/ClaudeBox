# The `.claude/` folder

This folder is the reusable Claude Code toolkit for **RecipeBox** — a recipe site built with
**Aspire + ASP.NET Core + Angular**, all orchestrated locally by Aspire. The `.claude/` toolkit
is as much the point of the repo as the app itself.

> **Important:** the project's main memory file, `CLAUDE.md`, lives at the **repo root**, one
> level *above* this folder — not inside it. Claude Code auto-discovers `CLAUDE.md` by walking up
> from your working directory, and the root copy survives `/compact`.

## What each piece is, and when it loads

| Path | What it is | When it enters context |
|---|---|---|
| `settings.json` | Shared project settings (incl. hook wiring). Committed. | Read at session start. |
| `rules/aspire.md` | AppHost/orchestration conventions. Path-scoped to the AppHost. | Loads when Claude touches AppHost/ServiceDefaults. |
| `rules/backend.md` | ASP.NET Core + EF Core conventions. Path-scoped to the API. | Loads when Claude touches the API. |
| `rules/frontend.md` | Angular conventions. Path-scoped to `client/`. | Loads when Claude touches the client. |
| `skills/add-endpoint/` | Playbook for adding an API endpoint. | On demand, when the task matches. |
| `skills/new-component/` | Playbook for adding an Angular component. | On demand, when the task matches. |
| `skills/add-aspire-resource/` | Playbook for adding a locally-orchestrated resource. | On demand, when the task matches. |
| `agents/code-reviewer.md` | Read-only reviewer subagent (Aspire-aware). | When delegated, or `@code-reviewer`. |
| `agents/test-gap-analyzer.md` | Read-only test-gap subagent. | When delegated, or `@test-gap-analyzer`. |
| `hooks/format.sh` | Formats the edited file after each edit. | Runs via the `PostToolUse` hook in `settings.json`. |

Rule of thumb:
- **Rule / CLAUDE.md** = something Claude should *know*.
- **Skill** = a procedure Claude should *follow* when a task matches.
- **Subagent** = work Claude should *delegate* to keep the main context clean.
- **Hook** = something that must happen *no matter what Claude decides*.

## Not committed (personal / local)
- `settings.local.json` — personal overrides, git-ignored on purpose.
- Anything ending in `.local.*`.

## After cloning
```bash
chmod +x .claude/hooks/format.sh
```
Then open a session: `/memory` confirms the rules load, `/agents` shows the subagents.

## Two Aspire tie-ins worth knowing
- Aspire ships an official **`aspireify`** skill that works with Claude Code — it can scan the
  repo and wire the AppHost for you. Handy when standing the solution up.
- The `format.sh` hook and the skills call `dotnet`/`ng`/`aspire`; they're harmless no-ops until
  those tools and the projects exist.

## Verify before trusting
Aspire and Claude Code both ship fast. The `settings.json` hook syntax, subagent frontmatter,
and exact Aspire API names (`AddNpmApp`, client integrations, package names) are the most likely
things to drift — confirm against https://code.claude.com/docs and https://aspire.dev.
