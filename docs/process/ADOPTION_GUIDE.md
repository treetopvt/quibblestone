<!--
  Adoption guide - for an engineer on ANOTHER project (cadence, pulse, the cobra prototypes, or a new
  repo) who wants to read, comment on, and potentially adopt this development process. Includes a
  portable, repo-neutral ALIGNMENT CHECKLIST the reader can run against their own project to find gaps
  and reconcile toward (or deliberately away from) this process. The full process is
  DEVELOPMENT_PROCESS.md; the pitch is EXECUTIVE_SUMMARY.md; the history is PROCESS_EVOLUTION.md.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Adoption Guide (and cross-project alignment checklist)

For an engineer on another project - **cadence, pulse, the cobra prototypes, or a fresh repo** - who
wants to evaluate this process, comment on it, and potentially adopt it. Nothing here is
QuibbleStone-specific; the examples are, the method is not.

This guide has three parts:

1. **How to read and critique it** (you are invited to push back).
2. **How to adopt it incrementally** (do not swallow it whole).
3. **The portable alignment checklist** (run it against your project to find the gaps).

---

## Part 1 - How to read and critique this

Start with [`EXECUTIVE_SUMMARY.md`](EXECUTIVE_SUMMARY.md) (3 minutes), then
[`DEVELOPMENT_PROCESS.md`](DEVELOPMENT_PROCESS.md) (the method), then
[`PROCESS_EVOLUTION.md`](PROCESS_EVOLUTION.md) (why it is shaped this way).

You are explicitly invited to disagree. The most useful critique targets:

- **Is the adversarial review worth its cost on your project?** It is the highest-value and
  highest-effort stage. On a low-risk internal tool it may collapse to a single lens; on anything
  touching money, safety, or untrusted input it earns its keep.
- **Does the three-gate model fit your CI reality?** The gates assume a fast local check that mirrors
  remote CI. If your build is slow, the local gates change shape.
- **Is docs-as-code right for your team?** The whole process assumes the spec belongs in the repo. A
  team married to an external tracker as the source of truth will feel friction here - resolve it
  deliberately (see the checklist's "tracker" row) rather than half-adopting.

To comment: open a PR against these files, or leave review comments on the PR that introduced them.
The process is subject to its own discipline - propose a change as text and let it be challenged.

---

## Part 2 - How to adopt it incrementally

Do not adopt all five stages at once. The evolution history shows the load-bearing order; follow it.
Each step is useful on its own.

| Step | Add | You get | Prerequisite |
|---|---|---|---|
| 1 | A **charter** (README) + a **working agreement** (agent/contributor rules) + one named architectural bet + slice discipline | A shared source of truth and a scope backbone | none |
| 2 | The **ADR convention** (`docs/adr/`, numbered, dated, decisions-surfaced) | Durable, auditable design decisions | step 1 |
| 3 | The **docs-as-code backlog** (`feature.md` + `NN-story.md` with strong ACs) + a story-authoring agent | The spec as versioned text, small enough to hand an agent | step 1 |
| 4 | The **`implementation.md` bridge** (reuse map + DAG-ready wave plan) | Stories become safely parallelizable | step 3 |
| 5 | **Orchestration** (umbrella branch, worktree builders, three gates, wave verification) | Fast parallel builds that stay coherent | steps 3-4 |
| 6 | The **adversarial review** (multi-lens, findings-as-requirements) folded into the ADR | The design survives its own critique before code | steps 2-3 |
| 7 | **Deploy lanes + promotion discipline** (auto to staging, deliberate tag to prod) | A closed pipeline with a final human checkpoint | a working CI/CD |

A team can stop at any step and have a better process than they started with. Steps 1-3 alone (charter,
ADRs, docs-as-code stories) are a large improvement with almost no tooling.

**When adapting a pattern from another repo (including this one), record the adaptation.** The single
best habit to copy is [`../ADOPTION_NOTES.md`](../ADOPTION_NOTES.md): a written record of what you
kept, what you dropped, and why. It makes your adaptation auditable and stops the next person from
"fixing" a deliberate deviation.

---

## Part 3 - The portable alignment checklist

Run this against any project (cadence, pulse, cobra, a new repo) to see where it aligns with the
process and where it diverges. **Divergence is not automatically wrong** - a smaller or lower-risk
project should deliberately run a lighter version. The goal is *deliberate* alignment, not conformance.

For each row: mark **Aligned** / **Partial** / **Gap** / **N/A by design**, and note the artifact (or
its absence) that justifies the mark. A project that is Partial or Gap on a row it should be Aligned on
is a reconciliation target.

### A. Charter and framing (Stage 1)

- [ ] There is a single **charter** document that is the acknowledged source of truth for vision,
      stack, and architecture.
- [ ] There is a **working agreement** for contributors (human and agent): conventions, deliberate
      exclusions, prose style.
- [ ] One **architectural bet** is named and is the thing designs are measured against.
- [ ] A short set of **non-negotiables** is named up front (safety, privacy, security - whatever the
      domain demands) and is designed in, not deferred.
- [ ] **Slice discipline** is explicit: there is a defined current slice, and out-of-slice ideas are
      parked rather than built.
- [ ] A **living roadmap / index** exists, is dated, traces every line to a spec item, and is updated
      as work lands.

### B. Challenge (Stage 2)

- [ ] Hard or fact-dependent decisions are recorded as **numbered, dated ADRs** in the repo.
- [ ] Fact-dependent decisions are preceded by a **timeboxed spike** whose deliverable is a written
      recommendation, and which is **honest about its limits**.
- [ ] Every non-trivial plan gets an **adversarial review before any code**.
- [ ] The review runs **multiple distinct lenses** (at minimum: invariant/safety, abuse/security,
      parallelizability/plan, scope, cold-builder-can-build-it).
- [ ] Review **findings become binding requirements** written onto the specific stories that must
      satisfy them - not comments that evaporate.
- [ ] ADRs **amend each other honestly**: a superseding decision says so in both records, and retained
      invariants are described precisely (not overclaimed).

### C. Decompose to spec (Stage 3)

- [ ] The **backlog lives in the repo** as markdown (docs-as-code), versioning with the code.
- [ ] Stories are **small, INVEST-quality units** with **Given/When/Then acceptance criteria** (3-7),
      an **out-of-scope guard**, and dependencies.
- [ ] Non-negotiables appear as **explicit, observable ACs** on the stories that touch them.
- [ ] Each fully-specified feature has an **`implementation.md`**: per-story tech notes, a **reuse
      map**, and a **DAG-ready wave plan** sized by file-footprint disjointness.
- [ ] The tracker (if any) **mirrors** the markdown for visibility, with the **markdown canonical** -
      one authority for content, one for status.
- [ ] **Planning is its own session** and lands as a **reviewed, merged planning PR** before any
      building starts.

### D. Orchestrated build (Stage 4)

- [ ] Large features are built via an **orchestrator** that plans a dependency DAG and fans out
      **builder subagents**, each on an **isolated worktree**, each building **one story to its ACs**.
- [ ] Builders get a **self-contained brief** (story + implementation note + reuse map + the
      non-negotiable guardrails verbatim) and return a **structured result**.
- [ ] Parallelism is bounded by **file-footprint disjointness**, not by wave labels - shared-file
      hotspots are serialized.
- [ ] There is a **layered gate model**: per-builder review + CI, a **re-run after integration** on
      the shared branch, and a **remote-CI-green** gate before a PR goes ready.
- [ ] Code review is **adversarial** and emits a **machine-readable verdict** the automation can act
      on.
- [ ] There is a **human verification checkpoint at each wave boundary** (drive the real app, real
      multi-device flows where relevant) with a required **sign-off** before proceeding.

### E. Ship (Stage 5)

- [ ] There is a **staging lane** that deploys automatically on merge and a **production lane** that
      moves only on a **deliberate, versioned action** (a tag), validated in staging first.
- [ ] Promotion is **offered proactively but never automatic** - production moves on a human's
      deliberate choice.
- [ ] A **runbook** governs deploys and is read before promoting.
- [ ] Shipping **closes the loop**: story status flips, issues close, the roadmap is updated.

### F. Roles and cross-cutting

- [ ] Work is done by **specialized agents/roles** (story author, builder, reviewer, tester), each
      scoped to one job, each brief encoding the same non-negotiables.
- [ ] The **scope rule** is respected: small/unplanned changes skip the heavy path and go straight to
      the build tools.
- [ ] The process **records what it does NOT cover** as explicitly as what it does (no overclaimed
      guarantees).
- [ ] Adaptations of the process are themselves **recorded as text** (an adoption-notes equivalent).

---

## How to use the checklist across projects

1. **Copy this file** into the project under review (or run it as a review pass).
2. Walk each row, mark Aligned / Partial / Gap / N/A-by-design, and cite the artifact.
3. Produce a **one-page alignment report** per project: the Gaps that are real (should align but do
   not) versus the deliberate divergences (lighter by design, and why).
4. For each real Gap, decide: **reconcile the project toward the process**, or **evolve the process**
   because the other project found a better way. Cross-project review is bidirectional - a sibling
   project may have solved something this one has not, and that improvement flows back as a change to
   [`DEVELOPMENT_PROCESS.md`](DEVELOPMENT_PROCESS.md).

The aim is a set of sibling projects that run **recognizably the same process**, each adapted to its
own risk profile and stack, with every deviation deliberate and written down.
