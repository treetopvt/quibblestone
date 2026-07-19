<!--
  ADOPTION_CHECKLIST.md - tells a new project which pieces of the methodology are load-bearing vs
  optional GIVEN ITS OWN Step-0 confounds. The whole point of the pulse review is that the process's
  behavior is a function of those confounds, so adoption is conditional, not one-size-fits-all.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Adoption checklist (by your confounds)

Run [`METHODOLOGY.md`](METHODOLOGY.md) Step 0 first and write down your answers. Then use this page to
decide what to adopt. **Divergence is fine when it is deliberate** - the goal is a process matched to
your conditions, not conformance.

## Load-bearing for EVERY project (adopt regardless of confounds)

These held or were confirmed across every project in the set. Do not skip them:

- [ ] A **charter** (README + agent/contributor guide) - the source of truth and the architectural bet(s).
- [ ] **ADRs** for hard decisions, each with an **adversarial review** whose findings become binding
      requirements (run by a separate context - see below).
- [ ] **Docs-as-code stories** with Given/When/Then ACs and out-of-scope guards.
- [ ] **Structural independence (Tier A)** - every diff/plan reviewed by a separate agent/bot context.
      Near-zero cost; not optional.
- [ ] A **machine-enforced PR gate** that exists and gates **every stack** (generalization 3).
- [ ] A **stack-agnostic Definition of Done** with `[machine-enforced]` / `[reviewer-checked]` labels.
- [ ] **Seams-first** with a **named integration-seam owner** (the composition root, orchestrator-only)
      and a **scheduled seam-hardening pass**.
- [ ] A **proportional rollback** (redeploy last-good) + a plaintext incident log.
- [ ] `implementation.md` treated as **disposable after merge** (never backfilled).

## Conditional - decide from your Step-0 answers

| Your confound | If ... | Then ... |
|---|---|---|
| **Team shape** | solo or solo + agent-fleet | Skip the Tier-B human sign-off tier EXCEPT for Critical classes; Tier A (agent/bot) is your independence. Do not add human-review latency the process has not proven you need. |
| | multi-human | Require the Tier-B human sign-off tier; require the PR gate **before the first shared branch**. Note: multi-human review latency is **untested** by any project so far - measure it, do not assume the process absorbs it. |
| **Coupling** | greenfield / low coupling | Waves fan wide; a light single seam-hardening pass is enough. |
| | coupled / legacy | The integration-seam owner is **mandatory**; expect **near-serial early waves** whose real job is to **create seams before fan-out pays off**; expect the composition root to churn most - schedule for it. |
| **Architectural bets** | one clean bet | Use the "one bet" review lens; measure every design against it. |
| | many concerns | Name each; expect more ADRs; do not force a single-sentence bet that is not there. |
| **CI maturity** | no gating CI | **STOP.** Stand up the minimal PR gate (`templates/ci-minimal.yml`) and gate every stack **before** adding a second contributor (human or unattended agent). This is the pulse failure mode. |
| | slow CI (exists) | Only now: Gate 0 = affected-tests subset; full suite at Gate 2 (integration) and Gate 4 (release). |
| | fast local-mirrorable CI | Run it full at every gate. |
| **Stacks** | single stack | One builder role + one gate. |
| | multi-stack | A builder role + a gate **per stack**; none orphaned (the pulse backend was ungated). |
| **Production** | no prod yet | Ship only the proportional Operate stub (`templates/operate-stub.md`). Do not add flags/canary/on-call. |
| | real prod + users | Scale Operate up: feature flags, health checks, staged rollout, on-call - each still unproven, so add deliberately. |

## The one-line adoption rule

Adopt the "load-bearing for everyone" list on faith (three projects back it). Adopt everything in the
conditional table **from your own Step-0 evidence**, and where your conditions differ from the proof
conditions, **say so and adapt** - do not inherit a choice that was right for a solo greenfield build
and wrong for you.
