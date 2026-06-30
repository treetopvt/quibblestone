# GitHub Tracker Cheat Sheet (QuibbleStone)

How the story markdown in `docs/features/` maps to GitHub, and the `gh` commands the `story-agent` and the
`orchestrate-feature` skill use. **Markdown is canonical for content** (the spec); **GitHub is canonical for
visibility** (status, the work queue). Keep title, body, ACs, and status in sync. If they diverge, the repo wins.

> This documents the scheme **already in use** in this repo (the 32 Epic/Feature/Story issues created at planning
> time), plus the `status:*` labels adopted for the orchestration loop. It is intentionally *not* the generic
> milestone-based scheme - QuibbleStone uses a sub-issue hierarchy, not milestones.

## Model (what's live today)

QuibbleStone uses a three-level **sub-issue hierarchy**, not milestones:

- **Epic** = an Issue labeled `epic`, one per build phase (e.g. "Epic: Phase 0 - Foundation",
  "Epic: Phase 1 - Playable MVP"). Its body lists the features; the Feature issues are its **sub-issues**.
- **Feature** = an Issue labeled `feature` + `feature:{slug}` (e.g. `feature:session-engine`), a **sub-issue of an
  Epic**. Its body lists the stories and links the `feature.md` source of truth.
- **Story** = an Issue labeled `story` + `feature:{slug}`, a **sub-issue of a Feature**. Its body carries the ACs
  and links the `NN-<slug>.md` source of truth.

Issue bodies always link back to the canonical markdown, e.g.
`**Source of truth:** docs/features/session-engine/01-create-room.md`.

## Labels

### In use (do not recreate)

```
epic
feature
story
feature:child-safety   feature:design-system   feature:game-modes   feature:group-play
feature:platform-devops   feature:session-engine   feature:single-player   feature:template-model
feature:the-reveal
```

A new feature adds one `feature:{slug}` label.

### Status labels (adopted for orchestration - create once)

Status was originally tracked only as the body-text `**Status:**` line. The orchestration loop also swaps a
`status:*` label so the board is filterable. **These four are net-new; create them once** (this repo had no
`status:*` labels before):

```bash
gh label create "status:todo"        -c "#cccccc" -d "Story not started"
gh label create "status:in-progress" -c "#fbca04" -d "Story actively being built"
gh label create "status:in-review"   -c "#0e8a16" -d "Story built, in code review / PR"
gh label create "status:blocked"     -c "#b60205" -d "Story blocked (see issue comment)"
```

> Backfill note: the 32 existing issues carry body-text status but no `status:*` label yet. Either backfill
> `status:todo` onto the open stories in one pass, or let the orchestrator add labels lazily as it touches each
> story. Either is fine - the body-text `**Status:**` line remains canonical.

There is **no** `type:*`, `feat:*`, `i18n-pending`, or `claude-managed` label - those are not part of this repo's
scheme. (No i18n exists in the stack, so `i18n-pending` has no meaning here.)

## Status mapping

The markdown story header (`**Status:** ...`) is canonical. The GitHub state + label mirror it.

| Story markdown `**Status:**` | GitHub state | Label |
|---|---|---|
| Not Started | open | `status:todo` |
| In Progress | open | `status:in-progress` |
| In Review | open | `status:in-review` |
| Blocked | open (+ comment with reason) | `status:blocked` |
| Complete | **closed** (completed) | (remove `status:*`) |
| Dropped | **closed** (not planned) | (remove `status:*`) |

## Commands

These use the `gh` CLI. The `story-agent` may auto-run the create/update commands (it prints each first and shows
the resulting issue number/URL); it will not auto-close an Epic, bulk-edit many issues, or remove a `feature:*`
label without prompting.

### Create a Feature issue (sub-issue of its Epic)

```bash
gh issue create \
  --title "Feature: Session & Room Engine" \
  --body-file /tmp/feature-body.md \
  --label "feature,feature:session-engine"
# then link it under its Epic as a sub-issue (GitHub UI or the sub-issues API)
```

### Create a Story issue (body references the markdown)

```bash
gh issue create \
  --title "Story: Create a room and get a join code" \
  --body-file /tmp/story-body.md \
  --label "story,feature:session-engine,status:todo"
# -> capture the printed issue number; record it in the story file / feature.md Stories table
# then link it under its Feature as a sub-issue
```

Story bodies follow the live convention: a one-line header
(`**Story** · Feature: ... · Epic: ... · **Status:** Not Started`), a short summary, a
`**Source of truth:**` link to the `NN-<slug>.md` file, then the ACs as checkboxes.

### Move status (label swap + body line)

```bash
gh issue edit <n> --remove-label "status:todo" --add-label "status:in-progress"
# and flip the **Status:** line in the markdown story file (canonical)
```

### Update the body after editing the markdown

```bash
gh issue edit <n> --body-file /tmp/story-body.md
```

### Block / unblock

```bash
gh issue comment <n> --body "Blocked: waiting on the hub method from #<n2> (session-engine)."
gh issue edit <n> --remove-label "status:in-progress" --add-label "status:blocked"
```

### Complete

```bash
gh issue close <n> --reason completed --comment "All ACs met, tests linked, merged in #<pr>."
gh issue edit <n> --remove-label "status:in-review"
```

## Linking PRs to stories (orchestrator, Phase 5)

Single repo, so a draft PR auto-closes its stories on merge. Put one `Closes #<n>` per story in the umbrella PR
body:

```bash
gh pr create --draft --base main --head feat/session-engine \
  --title "feat(session-engine): create/join/roster" \
  --body "$(printf 'Summary...\n\nCloses #20\nCloses #21\nCloses #22\n')"
```

### Gate 3: wait for Actions, then promote

```bash
gh pr checks --watch
gh pr view --json statusCheckRollup -q '[.statusCheckRollup[].conclusion] | unique'
gh pr ready   # only after green + verification signed off
```

## Optional: GitHub Project board

If you later want a board for non-engineer visibility:

```bash
gh project item-add <project-number> --owner treetopvt --url <issue-url>
```

## Auto-execution policy

The `story-agent` may run the create/update commands above without per-command confirmation, but **prints each
command first** and shows the resulting issue number/URL. It will **not** auto-close an Epic, bulk-edit many
issues, or remove a `feature:*` label without prompting.
