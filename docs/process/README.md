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
| **The stack-agnostic, repeatable method** (hardened by the cadence + pulse reviews) | [`methodology/METHODOLOGY.md`](methodology/METHODOLOGY.md) + [`methodology/`](methodology/) | 15 min |
| **A partner / customer pitch + resume source** (one document, two uses) | [`CAPABILITY_STATEMENT.md`](CAPABILITY_STATEMENT.md) | 6 min |

> **If you are here to adopt the process on a new project, start with
> [`methodology/`](methodology/), not this folder.** The docs here are the QuibbleStone case study; the
> `methodology/` subtree is the portable, multi-project-hardened method (Step 0, the six generalizations
> the cadence + pulse reviews forced, templates, an adoption checklist, and a proven-vs-unproven ledger).

There is also a **shareable one-page visual overview** (the executive summary rendered for engineers
outside the repo to read and comment on):
<https://claude.ai/code/artifact/c2a61d78-5199-4e92-b9e5-a0a10c4d43fd> (private until shared from the
page's share menu). It is a snapshot of `EXECUTIVE_SUMMARY.md` + the five-stage pipeline; the markdown
here is canonical.

## Sharing this for review

To get the process critiqued, extended, or adopted by other sessions or engineers:

- [`PROCESS_PACKAGE.md`](PROCESS_PACKAGE.md) - a **self-contained bundle** of the four docs (Parts I-IV,
  repo-internal headers stripped, provenance + timeline preamble added) that stands on its own outside
  this repo. This is the thing to hand to another session or project.
- [`REVIEW_PROMPT.md`](REVIEW_PROMPT.md) - a **ready-to-paste intro prompt** that casts the receiving
  session as an adversarial reviewer and asks for structured add/refute/modify feedback. Paste it with
  the package.
- [`REVIEW_PROMPT_PULSE.md`](REVIEW_PROMPT_PULSE.md) - a **pulse-specific kickoff** for the next
  cross-project pass: run it inside a pulse session so the reviewer characterizes pulse's real
  conditions (team, coupling, CI) and confirms or contradicts the cadence findings from a different
  vantage. (Done: the pulse review is folded in and generalized into `methodology/`.)
- [`PULSE_CI_KICKOFF.md`](PULSE_CI_KICKOFF.md) - a **remediation kickoff**: paste it into a pulse
  session to generate a real minimal PR-gating CI workflow from pulse's actual build files, closing
  pulse's top finding (no gating CI; an ungated backend stack). Canonical template:
  [`methodology/templates/ci-minimal.yml`](methodology/templates/ci-minimal.yml).

## The evidence (case study)

The process took QuibbleStone from an empty repo to a full shippable alpha in **~8-11 calendar days**
(2026-06-30 to 2026-07-10), **126 merged PRs**, one solo developer. Delivery burn-up chart:
<https://claude.ai/code/artifact/cf54f9a7-cde1-4da1-97b3-90b5a44299fe>. These are GitHub calendar
timestamps, not logged effort-hours (roughly **15-20 labor-hours** at the ~10 hrs/week budget) - the
calendar compression is a fact about **agent parallelism, not human throughput**. The proof holds
under one set of conditions (solo, greenfield, one architectural bet, fast CI); see
`EXECUTIVE_SUMMARY.md` "The proof (and its limits)" before quoting the number.

## Revisions

- **2026-07-18 - v1 authored.** The five stages, evolution, adoption guide, and shareable overview,
  reverse-engineered from this repo's ADRs, backlog, and orchestration artifacts.
- **2026-07-18 - cadence-review hardening.** An adversarial review from the `cadence` repo (the process
  applied to itself) produced 11 findings, all folded in: honest labor-hours + conditions-of-proof box,
  reviewer-not-author independence rule, a **coupling model** replacing pure file-disjointness, a new
  **Operate stage** (incidents / hotfix / rollback / feature flags), a **prior-art** table, one-way
  tracker mirror + disposable-artifact lifespan, verification-as-durable-e2e, a slow-CI gate variant, a
  binding-requirement retirement path, and portability-with-named-assumptions.
- **2026-07-19 - pulse-review generalization.** A second adversarial review, from the `pulse` repo,
  **contradicted** QuibbleStone on two axes (independence is cheap via a separate agent/bot context,
  not a second human; and "no gating CI" - not "slow CI" - was the real risk) and strengthened the
  seams-before-fan-out and proportional-operate stances. Rather than only patch the case-study docs,
  this pass **extracted the stack-agnostic method** into [`methodology/`](methodology/): `METHODOLOGY.md`
  (Step 0 + the six generalizations), templates (coupling-aware wave plan, two-tier gates, stack-agnostic
  DoD, minimal PR-gating CI, proportional Operate), an adoption checklist keyed to confounds, and a
  conditions ledger that marks **multi-human review latency** and **production operate/rollback** as
  still unproven. The QuibbleStone docs here now point at the methodology as the more general source.

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
