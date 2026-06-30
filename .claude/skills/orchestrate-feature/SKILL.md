---
name: orchestrate-feature
description: Orchestrate the parallel implementation of a large, already-planned QuibbleStone feature on a feat/{slug} umbrella branch using worktree-isolated builder subagents, with code-review + ci-check gates and main-session app verification at wave boundaries. Use when the user says "orchestrate feature", "start the orchestrator", "implement {feature} in parallel", or names a planned feature to build out. Not for small/unplanned changes.
disable-model-invocation: true
allowed-tools: Bash, Read, Write, Edit, Grep, Glob, Workflow, Agent, Skill
---

# Orchestrate Feature (QuibbleStone)

Drive the standard for building a large, already-planned, story-driven feature: parallel builder subagents on
isolated worktrees, serial gated integration onto a `feat/{slug}` umbrella branch, and main-session verification at
wave boundaries. **Read the source of truth first:**
[`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../../docs/FEATURE_ORCHESTRATION_PLAYBOOK.md). This skill executes
that playbook; the doc governs. Tracker is **GitHub** via `gh` - see
[`docs/GITHUB_TRACKER.md`](../../../docs/GITHUB_TRACKER.md). The repo `README.md` is the project charter.

This is a **single repo** (`api/` + `web/` + `infra/`), so there is no cross-repo / typed-client-regen machinery -
the api <-> web contract is the SignalR hub signatures consumed by `web/src/signalr/useGameHub.ts`, kept in sync by
hand.

Argument: the feature slug (e.g. `session-engine`). If omitted, ask which planned feature.

## Effort

- **This orchestrator session: high.** The expensive, error-prone decisions live here: planning the dependency
  DAG, sizing builders by file-footprint, integrating + resolving merge conflicts, adjudicating reviews, reading
  verification results.
- **Builder subagents: medium by default.** Precise brief (ACs + the `implementation.md` per-story note + the
  reuse map) against established patterns. **Low** for trivial/mechanical stories; **high** for novel/stateful.
  Web stories run on `frontend-agent`.
- **Per-story code-review / verify stage: high.** Adversarial review is where extra reasoning pays off.

## Context

- Branch: !`git branch --show-current`
- Status: !`git status --short`

## Step 0 - Preflight (stop if any fails)

1. Confirm the feature is planned: `docs/features/{slug}/feature.md` + `implementation.md` (with a DAG-ready Wave
   Plan) + `NN-<slug>.md` story files exist, and each story carries a GitHub Story issue number. **If planning is
   not done, stop and run the `story-agent` in a separate session first** - do not plan and build in one session.
2. Confirm the **planning-docs PR is merged** and you are on an up-to-date `main`. Do not start otherwise.
3. Read `implementation.md` and every story file for ACs + per-story tech notes + the reuse map.

## Step 1 - Plan the DAG

Produce a wave plan from `implementation.md` waves + each story's file footprint. Per story record: issue number,
files it creates/owns, depends-on, can-run-with. Foundation (shared theme/components, the template-model schema,
the engine interface, the child-safety filter) is serialized first; the API/hub -> consuming-web chain is
serialized; everything else fans out only where file footprints are disjoint. **Watch the shared files** -
`web/src/theme.ts`, `web/src/fontawesome.ts`, `web/src/signalr/useGameHub.ts`, `api/src/Hubs/GameHub.cs`: two
stories editing the same one must serialize or merge. Show the plan and the per-wave concurrency to the user before
fanning out.

## Step 2 - Umbrella + worktrees

- Create/confirm `feat/{slug}` umbrella (`git switch -c feat/{slug}` off up-to-date `main`).
- Builders run on isolated worktrees (`Agent` `isolation: "worktree"`). For web worktrees, junction `node_modules`
  to the main checkout; **remove the junction before removing a worktree** (footgun: the recursive delete follows
  the junction and wipes the shared install).

## Step 3 - Per-wave fan-out (Workflow) + gated integration

For each wave, **use a Workflow** (this is the explicit opt-in) that fans out the file-disjoint stories to
worktree-isolated builders. Each builder brief is self-contained:

- the story file (ACs) + its `implementation.md` per-story note + the relevant reuse-map rows;
- the **builder guardrails** (verbatim from the playbook's "Builder guardrails" section): MUI-via-theme (no
  hex/raw-px) + shared AppBar/Button; FontAwesome-only (no `@mui/icons-material`); one SignalR connection via
  `useGameHub`; `import.meta.env` config (no secrets in `VITE_`); TS strict (no `any`, guard not `!`, props as
  `interface {C}Props`); react-hook-form; big tap targets; API controllers-vs-hubs separation + async-all-the-way +
  no committed secrets; **child safety** on every free-text surface (filter + family-safe toggle + no PII); no
  deliberately-excluded deps (Functions/Redux/Zustand/i18n/MSAL); verbose header comment on key files;
  hyphens/colons/parens not em dashes;
- schema'd return `{ summary, filesTouched, ciStatus, openQuestions }`;
- a `code-review` verify stage per story in the pipeline (**Gate 1**).

Then, in the main session:

1. Merge each **review-clean, ci-check-green** builder branch onto the umbrella **serially**; resolve overlap
   conflicts.
2. After each merge, run the `ci-check` skill on the merged result and `code-review` on the integrated delta
   (**Gate 2**) until green and warning-clean.

Do not merge a builder branch that is not `code-review`-clean and `ci-check`-green.

## Step 4 - Verification checkpoint (wave boundary)

Boot the app against the latest umbrella and walk the wave's journeys with the user (the main session holds the
servers):

```bash
dotnet run --project api/QuibbleStone.Api.csproj      # http://localhost:5180
cd web && npm install && npm run dev                   # http://localhost:5173
```

- Prereqs: **none** beyond the repo (rooms are ephemeral in-memory; Storage/Key Vault unused; free FontAwesome).
- Drive the journeys via the browser MCP (Playwright Chromium at `/opt/pw-browsers/chromium`). For multiplayer /
  real-time stories use **two browser contexts** (create room in one, join by code in the other; confirm roster ->
  round -> shared reveal sync with no refresh).
- Confirm unrelated surfaces (Home, Join) are unchanged (regression).
- Collect feedback, fold it into fix items / the next wave and the feature's Decisions log.
  **Do not proceed without the user's sign-off.**

Use the `run` / `verify` skills if present.

## Step 5 - PRs + tracker + Gate 3 (GitHub)

```bash
# Open or update the draft PR for this feature's umbrella:
gh pr create --draft --base main --head feat/{slug} \
  --title "feat({slug}): <feature title>" \
  --body "$(cat <<'EOF'
<summary>

Closes #<story-issue-1>
Closes #<story-issue-2>
EOF
)"

# Gate 3: wait for remote CI, then promote.
gh pr checks --watch
gh pr view --json statusCheckRollup -q '.statusCheckRollup[].conclusion'
# Only after Actions are green AND verification is signed off:
gh pr ready
```

- As stories build, flip the body-text `**Status:**` line and swap the `status:*` label
  (`status:todo` -> `status:in-progress` -> `status:in-review`); close on merge. The markdown story file is
  canonical (see `docs/GITHUB_TRACKER.md`).

## Don't

- Don't fan out stories with overlapping file footprints in parallel - serialize them or give them to one builder.
- Don't let a builder hold a long-running dev server - verification is the main session's job.
- Don't proceed past a verification checkpoint without the user's sign-off.
- Don't merge a builder branch that isn't `code-review`-clean and `ci-check`-green.
- Don't `gh pr ready` before `gh pr checks` is green.
- Don't bypass any gate (`--no-verify`, `--max-warnings=999`) without telling the user.
- Don't pull parked Phase 2-4 ideas into a Slice 1 wave (CLAUDE.md section 7).
