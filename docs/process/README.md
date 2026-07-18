<!--
  Index for docs/process/ - the development process documentation set. Points a reader at the right
  artifact for their need. The process itself is DEVELOPMENT_PROCESS.md.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# docs/process - the development process, documented

This folder documents **how work moves from an idea to shipped software** on QuibbleStone: a
spec-driven, adversarially-reviewed, agent-orchestrated process, proven on a solo nights-and-weekends
build that nonetheless shipped a full alpha.

These are the **meta-level narrative** documents. They tie together and abstract the operating manuals
that already live in the tree (the orchestration playbook, the tracker cheat sheet, the feature
templates) so the process can be explained, evolved, and carried to other projects.

## Start here

| If you want... | Read | Time |
|---|---|---|
| The pitch: what it is, why, and the benefits | [`EXECUTIVE_SUMMARY.md`](EXECUTIVE_SUMMARY.md) | 3 min |
| The full method, stage by stage | [`DEVELOPMENT_PROCESS.md`](DEVELOPMENT_PROCESS.md) | 15 min |
| How it grew into its current shape | [`PROCESS_EVOLUTION.md`](PROCESS_EVOLUTION.md) | 10 min |
| To adopt it on another project + a portable alignment checklist | [`ADOPTION_GUIDE.md`](ADOPTION_GUIDE.md) | 10 min |

There is also a **shareable one-page visual overview** (the executive summary rendered for engineers
outside the repo to read and comment on):
<https://claude.ai/code/artifact/c2a61d78-5199-4e92-b9e5-a0a10c4d43fd> (private until shared from the
page's share menu). It is a snapshot of `EXECUTIVE_SUMMARY.md` + the five-stage pipeline; the markdown
here is canonical.

## The process in one line

> Challenge the idea before you build it, make the spec the contract, let parallel agents build to it
> under three gates, and verify with a human at every wave - all as versioned text in the repo.

## The five stages

```
  IDEA -> [1 Charter] -> [2 Challenge] -> [3 Decompose] -> [4 Orchestrate] -> [5 Ship] -> SHIPPED
             README        ADR + adversarial   docs/features/    feat/{slug} +      qa on merge,
             CLAUDE.md     review              stories +         worktree builders  beta on tag
                                               implementation.md + 3 gates
```

Each arrow crosses a session boundary and a human checkpoint. The artifact is the interface between
stages.

## Relationship to the rest of the tree

The **operating detail** these documents point at (and do not restate):

- [`../FEATURE_ORCHESTRATION_PLAYBOOK.md`](../FEATURE_ORCHESTRATION_PLAYBOOK.md) - the build-phase
  manual the `orchestrate-feature` skill executes (phases, the three-gate model in full, builder
  guardrails, the per-wave Workflow skeleton).
- [`../GITHUB_TRACKER.md`](../GITHUB_TRACKER.md) - how the docs-as-code backlog mirrors to GitHub
  Epic/Feature/Story issues.
- [`../ADOPTION_NOTES.md`](../ADOPTION_NOTES.md) - how the orchestration pattern was first adapted into
  this repo (kept vs dropped from a generic template).
- [`../adr/`](../adr/) - the decision records, including the spike convention (0001) and the mature
  five-lens adversarial review (0003).
- [`../features/`](../features/) - the backlog as code, with the `_template/` kit.
- The repo [`../../README.md`](../../README.md) is the charter and remains the source of truth above
  all of these. If anything here conflicts with it, the README wins - flag the discrepancy.
