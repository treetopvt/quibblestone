<!--
  TEMPLATE: definition-of-done.md - a STACK-AGNOSTIC definition of done. Says "the affected stack's gate
  passes", never a hardcoded npm/dotnet line. Every stack in the repo has a builder and a gate; none
  orphaned (the pulse backend was ungated because DoD implicitly meant "the frontend gate"). Every item
  tagged [machine-enforced] or [reviewer-checked] so nothing is silently honor-system.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Definition of Done (stack-agnostic)

A change is **Done** when all of the following hold. Copy this per repo and, in the header, list the
repo's stacks and the exact gate command for each (this is the only place stack-specific commands live).

> **Stacks in this repo:** `<stack-a>` gate = `<command>`, `<stack-b>` gate = `<command>`, ...
> Every stack listed here has a builder role and a gate. If a stack is not listed, it is orphaned - add
> it before shipping.

## Per-change checklist

- [ ] **The affected stack's gate passes** for **every** stack the diff touches - build + lint +
      typecheck + test. Not "the frontend gate"; the gate for each touched stack. `[machine-enforced]`
- [ ] **PR-triggered CI is green** across all gated stacks (gates on the PR, not inside deploy).
      `[machine-enforced]`
- [ ] **Structural review clean (Tier A):** automated PR review + the review-agent pass show no Critical
      findings; the story's ACs are met; the reuse map was honored; no shared abstraction was forked.
      `[machine-enforced]` (bot) + `[reviewer-checked]` (agent)
- [ ] **Each AC is linked to a check** - an automated test where possible, a documented manual check
      otherwise (and the gap is noted). `[reviewer-checked]`
- [ ] **Human sign-off obtained IF Critical class** - the change touches isolation / security /
      contract / migration / money / safety / the integration seam. Otherwise not required.
      `[reviewer-checked, conditional]`
- [ ] **Integration reconciled:** the orchestrator merged serially, the composition-root / seam owner
      reconciled it, and the merged tree is green (Gate 2). `[machine-enforced]` + `[reviewer-checked]`
- [ ] **Rollback path exists** for anything deployed (redeploy last-good). `[reviewer-checked]`

## The rule behind the checklist

Nothing here is an honor-system claim. Each box is either enforced by a machine (a required check) or is
an explicit, named human responsibility. A DoD that says "tests pass" without saying *whose machine
runs them on the PR* is how a whole stack (155 tests, in pulse's case) goes ungated.
