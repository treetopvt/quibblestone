<!--
  Index for docs/process/methodology/ - the stack-agnostic, multi-project-hardened distillation of the
  development process. This is the portable "adopt me" version; the QuibbleStone-specific narrative is
  one level up in docs/process/.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# The Methodology - stack-agnostic and repeatable

This folder is the **portable, adopt-anywhere distillation** of the development process. It is the
process stated without reference to any one stack, and **hardened by two adversarial reviews**
(`cadence`, then `pulse`) of the version first proven on QuibbleStone. Where those reviews contradicted
the original, this methodology **generalizes to the contradiction** rather than papering over it.

If `docs/process/` is the case study (how it worked on QuibbleStone), **this folder is the method** any
future project runs.

## Read in this order

1. [`METHODOLOGY.md`](METHODOLOGY.md) - the spec: **Step 0** (characterize your confounds), the pipeline,
   and the **six load-bearing generalizations** the reviews forced.
2. [`ADOPTION_CHECKLIST.md`](ADOPTION_CHECKLIST.md) - which pieces are load-bearing vs optional **given
   your own Step-0 answers**.
3. [`CONDITIONS_LEDGER.md`](CONDITIONS_LEDGER.md) - what is **proven vs unproven** across the projects
   so far (multi-human latency and production operate are **not** proven - do not claim them).
4. Templates ([`templates/`](templates/)) - copy-from artifacts:
   - [`implementation.md`](templates/implementation.md) - coupling-aware wave plan (integration-seam
     row + `stack:` field)
   - [`review-gates.md`](templates/review-gates.md) - the two-tier review-gate definition
   - [`definition-of-done.md`](templates/definition-of-done.md) - stack-agnostic DoD with
     machine/reviewer labels
   - [`ci-minimal.yml`](templates/ci-minimal.yml) - the minimal PR-gating CI (the floor)
   - [`operate-stub.md`](templates/operate-stub.md) - the proportional Operate stage

## The one thing to internalize first

**This process's behavior is a function of your project's confounds.** It was proven on a solo,
greenfield, one-bet, fast-CI build; two reviews showed it bends on other axes (independence turned out
cheap-via-agents, and "no gating CI" - not "slow CI" - was the real risk). So there is no drop-in
version. Run Step 0, then adopt by the checklist.

## Lineage

Process lineage: COBRA prototype -> cadence / pulse -> QuibbleStone (proof) -> this methodology
(generalization). The QuibbleStone narrative, its evolution, the shareable overview, and the review
prompts live one level up in [`../`](../).
