# Adoption Notes: Feature-Orchestration Pattern

What landed when QuibbleStone adopted the parallel-builder / umbrella-branch feature-orchestration pattern, what
changed from the generalized template bundle, and the decisions made along the way. Source bundle:
`orchestrationtemplate.zip` (a generalized copy of a working setup, adapted - not imposed - to this repo).

## What landed

**New files**

- `docs/FEATURE_ORCHESTRATION_PLAYBOOK.md` - source of truth for the pattern: phases, the 3-gate model, builder
  guardrails, the per-wave Workflow skeleton. Adapted to QuibbleStone (single repo, no i18n, real verification
  recipe).
- `docs/GITHUB_TRACKER.md` - `gh` cheat sheet + status mapping, **rewritten to document this repo's live
  Epic/Feature/Story sub-issue scheme** (not the template's milestone scheme).
- `.claude/skills/orchestrate-feature/SKILL.md` - the `/orchestrate-feature {slug}` driver (user-invoked;
  `disable-model-invocation: true`).
- `docs/features/_template/` - copy-from templates in this repo's format: `feature.md`, `NN-story.md`, and the
  net-new `implementation.md`.

**Folded into existing files (kept ours, added only what was missing - nothing overwritten)**

- `.claude/skills/ci-check/SKILL.md` - added the Gate 1/Gate 2 framing, a grep-based **project sanity check**
  (FontAwesome-only, one SignalR connection, no hardcoded colors), and a forward note to add `npm run test:unit`
  when the test harness lands. Commands unchanged (they already mirror `ci.yml`).
- `.claude/agents/code-review.md` - added the orchestration role (Gate 1 per-story, Gate 2 integrated-delta), a
  machine-readable `clean` **Verdict** line the Workflow reads, and AC<->test story-discipline checks.
- `.claude/agents/story-agent.md` - added `implementation.md` authorship (now required per feature) and a richer
  GitHub-sync section matching the live Epic/Feature/Story hierarchy + `status:*` labels.
- `docs/features/README.md` - documented `implementation.md` and `_template/`.

## What changed from the template (and why)

- **Tracker rewritten, not adopted.** The template proposed Milestones + `type:story` + `feat:*` + `status:*` +
  `i18n-pending` + `claude-managed`. This repo already had 32 issues in an Epic -> Feature -> Story **sub-issue**
  hierarchy with labels `epic` / `feature` / `story` / `feature:{slug}` and **body-text status**. We documented the
  live scheme and added only the `status:*` labels.
- **All i18n removed.** The stack deliberately has no i18n framework (CLAUDE.md / `frontend-agent`). The template's
  locale-key CI check, i18n ACs, translation-completeness review section, and `i18n-pending` label were dropped and
  replaced with QuibbleStone invariants (child safety, MUI-theme styling, FontAwesome-only, one SignalR
  connection).
- **Single-repo, no codegen client.** The template's cross-repo machinery (mirrored folders, tracking issue,
  sibling PRs) and "regenerate the typed client" step do not apply. The api <-> web contract is the SignalR hub
  signatures consumed by `web/src/signalr/useGameHub.ts`, kept in sync by hand; the API/hub -> consuming-web chain is
  serialized without a codegen step.
- **ci-check kept ours.** The template's lint/ts:check/unit/e2e/locale pipeline does not match reality (no ESLint,
  no test harness yet, no i18n). The existing API-build + web-build + Bicep-validate pipeline is what `ci.yml`
  actually runs.
- **Story format kept ours.** `feature.md` + `NN-<slug>.md` with a header-line status (README section 11), not the
  template's `FEATURE.md` + `S01-example.md` YAML-frontmatter format. `implementation.md` is lowercase to match
  `feature.md`.
- **Verification recipe is real.** `dotnet run` (API, :5180) + `npm run dev` (web, :5173), browser MCP via the
  pre-installed Playwright Chromium, two browser contexts for the 2-player real-time check. No DB/infra to seed
  (rooms are ephemeral in-memory; Storage/Key Vault unused; free FontAwesome).
- **GitHub, not Azure DevOps.** No `az boards` / `az repos` survived; all tracker ops are `gh`. (Bicep `az`
  commands in `ci-check` are infra validation, unrelated to ADO.)

## Decisions made

| Decision | Choice | Note |
|---|---|---|
| Status representation | **Body-text status + `status:*` labels** | Orchestrator swaps the label and flips the markdown line. Four new labels (see below). |
| `implementation.md` strictness | **Required for every feature** | `story-agent` authors it; `orchestrate-feature` Step 0 requires it. |
| `_template/` contents | **Full kit** | `feature.md` + `NN-story.md` + `implementation.md`, in this repo's format. |
| Label creation | **Create only the `status:*` labels** | All other labels already exist on the 32 issues. |
| PR-review doc | **No separate doc** | The `code-review` agent is the checklist (backed by README section 4 + CLAUDE.md). |

## Follow-ups (not done in this setup pass)

- **Create the four `status:*` labels.** This environment has no `gh` CLI and the GitHub MCP exposes no
  create-label tool, so the labels were **not** auto-created. Run the block from `docs/GITHUB_TRACKER.md` (or use
  the GitHub UI):
  ```bash
  gh label create "status:todo"        -c "#cccccc" -d "Story not started"
  gh label create "status:in-progress" -c "#fbca04" -d "Story actively being built"
  gh label create "status:in-review"   -c "#0e8a16" -d "Story built, in code review / PR"
  gh label create "status:blocked"     -c "#b60205" -d "Story blocked (see issue comment)"
  ```
  Optionally backfill `status:todo` onto the open Story issues.
- **Backfill `implementation.md` for the 9 existing Slice-1 features.** The convention is now "required for every
  feature", but the nine already-specified features (`design-system`, `platform-devops`, `session-engine`,
  `template-model`, `game-modes`, `single-player`, `group-play`, `the-reveal`, `child-safety`) do not have one yet.
  Each needs a reuse map + a file-footprint Wave Plan before it can be orchestrated - a per-feature planning task
  for the `story-agent`, best done just before orchestrating that feature.
- **Restart the session** so the new `/orchestrate-feature` skill registers (skills load at session start).

## How to use it

1. **Plan** (own session): run `story-agent` to produce/refresh `docs/features/{slug}/` (`feature.md` +
   `implementation.md` + stories) and sync issues; open + merge the planning-docs PR.
2. **Orchestrate** (fresh session): type `/orchestrate-feature {slug}`; it validates the plan, then runs gated
   waves of worktree builders and stacks the draft PR. Three gates: local `ci-check` + `code-review` before
   integrate (Gates 1+2), remote Actions green via `gh pr checks --watch` before `gh pr ready` (Gate 3).
