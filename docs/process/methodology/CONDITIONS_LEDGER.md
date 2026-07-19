<!--
  CONDITIONS_LEDGER.md - what conditions the methodology has actually been tested under, so it never
  claims coverage it lacks. Every project in the set is solo-human + agent-fleet and pre-production, so
  two whole dimensions (multi-human review latency, production operate/rollback) are UNPROVEN. Say so.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Conditions tested so far

The methodology's behavior is a function of a project's confounds (see `METHODOLOGY.md` Step 0). This
ledger records the confounds each project in the set actually had, so a claim can be traced to the
conditions that proved it. **A generalization is only as strong as the number and diversity of projects
that confirm it.**

## The set

| Confound | QuibbleStone | cadence | pulse |
|---|---|---|---|
| **Team shape** | solo human + agent fleet | not characterized (contributed a review pass, not a Step-0 profile) | solo human + agent fleet (193 commits, 1 author; Copilot-class PR reviewer) |
| **Coupling** | greenfield, mostly disjoint (one serial hotspot: the DI entrypoint) | not characterized | coupled + concentrated (foundation seams 41 / 35 / 16 / 13 non-test importers; composition root the single most-churned file) |
| **Architectural bets** | one ("one engine, many thin modes") | not characterized | not characterized in the review |
| **CI maturity** | **gating** CI on the PR (build + unit + backend test; e2e not gated) | not characterized | **no gating CI** (backend 155 tests ungated; quality gates ran only post-merge inside deploy) |
| **Stacks** | dual (web + api), both gated | not characterized | dual (frontend + backend); backend orphaned (ungated) |
| **Reached production?** | no | no | no |

> cadence's own project conditions were never characterized - it contributed an adversarial *review* of
> the process, not a Step-0 profile of itself. Running Step 0 on cadence is open work.

## What the set PROVES (traceable to ≥1 project, contradictions noted)

- **Independence is structural, not human, and cheap.** pulse (solo + agent fleet + Copilot review) is
  the strong case; QuibbleStone used a review-agent gate. pulse **contradicted** the original
  "human-latency is the cost of independence" framing - generalized to the two-tier model.
- **Seams-before-fan-out.** Confirmed by QuibbleStone (foundation-first) and pulse (seams held under
  real coupling); **strengthened** by pulse into a named seam owner + a scheduled hardening pass.
- **Gate enforcement is the floor.** pulse **contradicted** QuibbleStone's "slow CI" worry: the real
  failure was **no gating CI at all** and an orphaned stack. Generalized to: a machine PR gate that
  gates every stack is a precondition.
- **Docs-as-code, findings-as-binding-ACs, the disposable implementation.md bridge.** Used across the
  set.
- **Wave fan-out's real payoff is conflict-free integration + clean review boundaries, not wall-clock
  speed.** Speed is unproven without genuine concurrent runners; every project ran effectively serial.

## What the set does NOT prove (do not claim coverage)

- **Multi-human review latency.** Every project is solo-human (+ agents). The Tier-B human sign-off
  tier and the "team" adoption path are **designed but untested**. Measure them on the first real
  multi-human project; do not assume the process absorbs handoff cost.
- **Production operate and rollback.** No project has reached production. The Operate stage is
  deliberately a **proportional stub**; flags, canary, health-check rollback, and on-call are
  **unproven** and must be added deliberately when a project actually has prod + users.

## How to extend this ledger

When a new project adopts the methodology, add a column: run Step 0, record its confounds, and after it
ships mark which generalizations it **confirmed / contradicted / found N/A**. A contradiction is not a
failure - it is the most valuable output, and it updates `METHODOLOGY.md` the same way the pulse review
did.
