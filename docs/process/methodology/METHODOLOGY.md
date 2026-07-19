<!--
  METHODOLOGY.md - the stack-agnostic, repeatable implementation of the AI-agent development process.
  This is the generalized distillation, hardened by two adversarial reviews (cadence, then pulse) of the
  process first proven on QuibbleStone (solo, greenfield, ~15-20 labor-hours to alpha). It encodes what
  those reviews PROVED, CONTRADICTED, and newly surfaced - and generalizes to the contradictions rather
  than papering over them. The QuibbleStone-specific worked example lives one level up in
  docs/process/DEVELOPMENT_PROCESS.md; where the two differ, THIS document is the more general and
  current source, because it is the multi-project-hardened version.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# The Methodology (stack-agnostic)

**A spec-driven, adversarially-reviewed, agent-orchestrated way to build software - stated without
reference to any one stack, so any project can adopt it.**

The single most important thing to understand before adopting: **this process's behavior is a function
of your project's confounds** (team shape, coupling, how many architectural bets, CI maturity, what
artifacts already exist). It was proven once on a solo greenfield build, then two sibling projects
(`cadence`, `pulse`) reviewed it adversarially and **contradicted** parts of that proof. The
contradictions are baked in below. Do not adopt the whole thing on faith - **run Step 0 first**, then
use the adoption checklist to decide which pieces are load-bearing for *your* conditions.

> **What is proven vs unproven** is tracked honestly in [`CONDITIONS_LEDGER.md`](CONDITIONS_LEDGER.md).
> Two things are NOT yet proven by any project in the set: **multi-human review latency** and
> **production operate / rollback**. The methodology does not claim coverage it lacks.

---

## Step 0 - Characterize your confounds (mandatory, before anything else)

Every adopting project runs this first, from its own repo, with evidence - because every later choice
depends on the answers. Write the answers down; they are the inputs to the adoption checklist.

| Confound | Question | How to measure | Why it decides things |
|---|---|---|---|
| **Team shape** | Solo, solo+agent-fleet, or multi-human? | `git shortlog -sne`; distinct recent authors; who actually reviews PRs | Decides whether independence is cheap (agent/bot) or carries human latency (untested) |
| **Coupling** | Greenfield-disjoint, or coupled through shared hotspots? | Import/dependency scan; find the God-files; which file churns most | Decides wave width - wide fan-out vs near-serial early waves + a mandatory seam owner |
| **Architectural bets** | One load-bearing design idea, or many concerns? | Read the charter; is there a sentence designs are measured against? | Decides whether the "one bet" review lens applies or you need several |
| **CI maturity** | No gating CI, slow CI, or fast local-mirrorable CI? | Read the CI config; is it PR-triggered? are all stacks gated? wall-clock? | Decides the gate model - stand one up, subset it, or run it full |
| **Existing artifacts** | What is already here - charter, ADRs, docs-as-code stories, tracker, orchestration? | Inventory the repo | Decides what you add vs what takes root on its own |

**State where you differ from the proof conditions** (solo / greenfield / one bet / fast CI). Those
differences are where the methodology has to be adapted, not assumed.

---

## The pipeline (the invariant shape)

Idea -> **Charter** (bind to constraints) -> **Challenge** (an ADR + an adversarial review whose
findings become binding requirements) -> **Decompose** (small stories with strong ACs + a
coupling-aware wave plan) -> **Build** (orchestrated builders per stack, gated) -> **Ship** (machine
gate green, assume-green deploy) -> **Operate** (proportional: rollback + incident log). Each arrow
crosses a checkpoint; the artifact is the interface between stages. The worked, stack-specific example
is [`../DEVELOPMENT_PROCESS.md`](../DEVELOPMENT_PROCESS.md).

What follows is not a re-description of the pipeline - it is the **six generalizations the two reviews
forced**, which are what make the methodology repeatable rather than a solo-project habit.

---

## The six load-bearing generalizations

### 1. Independence is structural, not human

The original solo process said "a plan cannot red-team itself - use a second person, or a
distinctly-prompted agent." Pulse contradicted the framing: its "team" was one human author across 193
commits with a **fleet of AI subagents plus an automated PR reviewer (Copilot-class bot)**. The lesson:
**independence is a property of the reviewing *context*, not of a second human**, and when the reviewer
is a separate agent/bot it is **near-zero cost**. Do not design around human review latency - no
project in the set has tested it (say so).

Encode **two tiers** of independence:

- **Tier A - structural independence (always, near-zero cost).** Every builder diff and every plan is
  reviewed by a **separate context** from the one that produced it: an automated PR reviewer
  (Copilot-class) and/or a distinctly-prompted review agent whose sole mandate is "attack this." This
  is the default and it is not optional. **Automated PR review is a named gate tier** (the original
  solo process had no slot for it; pulse depends on it).
- **Tier B - human sign-off (reserved).** A second human (team) or the owner deliberately switching
  roles (solo) signs off - but **only for Critical classes**: isolation / security boundaries,
  cross-plane or contract changes, migrations, money or safety seams, and the integration seam itself.
  Everything else rides Tier A.

### 2. Seams before fan-out (confirmed - and strengthened)

Both reviews confirmed: build the foundation seams first, then fan out. Pulse strengthened it with two
things the solo proof lacked, because pulse's coupling was real and concentrated (foundation seams had
41 / 35 / 16 / 13 non-test importers, and the composition root churned most because it is disjoint from
nothing):

- **A named integration-seam owner.** The composition root - the route/provider tree, the DI container,
  the module registry, whatever wires everything together - is **never a wave story**. It is edited
  **only by the orchestrator, serially, between waves**. It is the one file disjoint from nothing, so
  it cannot be owned by a parallel builder.
- **Seam-hardening as a scheduled step, not "freeze once."** Seed the seam at v0, **reserve extension
  fields** deliberately, and **budget one hardening pass after the first consumer wave** - because the
  highest-coupling seam keeps churning after a naive freeze. Plan for it instead of being surprised.

On a greenfield/low-coupling project these cost little and waves fan wide. On a coupled/legacy project
they are mandatory and early waves run near-serial - **expected parallelism width scales inversely with
coupling.**

### 3. Gate enforcement is the floor, not an optimization

The solo proof had gating CI and treated gates as "fast local checks." Pulse revealed the real risk was
not *slow* CI but **no gating CI at all**: every gate was local / agent honor-system, the backend (155
tests) was ungated, and quality gates ran only *post-merge* inside the deploy. So the methodology makes
this a **precondition, not a tuning knob**:

- A **minimal PR-triggered CI must exist** before any second contributor - human OR unattended agent -
  touches the repo. It runs, per affected stack: **build + lint + typecheck + test**. **All stacks are
  gated** (frontend and backend and infra - none orphaned).
- **Gates live on the PR; deploy assumes-green.** Quality gates that run only inside deploy are not
  gates.
- The "affected-tests subset for slow CI" variant is **premature until the gate exists**. First make
  the gate exist and gate every stack; subset it only once it is real and demonstrably slow.

### 4. Definition of Done is stack-agnostic and label-enforced

Pulse's backend was ungated because the DoD implicitly meant "the frontend gate." Fix it:

- DoD says **"the affected stack's gate passes"** - never a hardcoded `npm ...` or `dotnet ...` line.
- **Every stack in the repo gets a builder role and a gate.** None orphaned.
- **Every gate is tagged `[machine-enforced]` or `[reviewer-checked]`** so no gate is silently an
  honor-system claim. If it is not machine-enforced, it is a human's job and must say so.

See [`templates/definition-of-done.md`](templates/definition-of-done.md).

### 5. Operate and rollback are proportional to reality

No project in the set has reached production. So the methodology **does not ship a prod-shaped runbook
as if proven.** The minimum real Operate artifact is small:

- The pre-merge gates (above) + an **assume-green deploy**.
- A **one-command rollback**: redeploy the last-good artifact / tag. Written down next to the promote
  step.
- A **plaintext incident log** (`docs/incidents/YYYY-MM-DD-slug.md`): what, when, impact, fix,
  whether rollback was used.

Scale up (feature flags, health checks, staged rollout, on-call) **only when the project actually has
production and real users.** Not before. See [`templates/operate-stub.md`](templates/operate-stub.md).

### 6. Claim the benefit you actually deliver

Do not oversell "waves = faster." Genuine wall-clock speed from parallel fan-out is **unproven without
real concurrent runners**, and on a solo/agent project the runs are effectively serial. The **proven**
payoff of wave fan-out is **conflict-free integration and clean review boundaries** - each story lands
as a small, separately-reviewable diff that does not collide with its siblings. Sell that. It is real
and it is enough.

---

## The gate model (generalized, two-tier)

```
  build a story (a separate builder context, one story to its ACs)
     |
     v
  [ GATE 0 - machine ]        PR-triggered CI: affected stack's build + lint + typecheck + test
     |                        [machine-enforced] - all stacks gated, none orphaned  (generalization 3, 4)
     v
  [ GATE 1 - structural ]     automated PR review (Copilot-class) + a distinct review-agent pass
     |                        [reviewer-checked, separate context] - Tier A, always  (generalization 1)
     v
  integrate at the seam       orchestrator merges serially; the integration-seam owner reconciles the
     |                        composition root between waves  (generalization 2)
     v
  [ GATE 2 - integration ]    re-run CI on the merged tree + review the integrated delta
     |                        [machine-enforced + reviewer-checked]
     v
  [ GATE 3 - human, IF ]      human sign-off ONLY for Critical classes (isolation/security/contract/
     |                        migration/seam/money/safety)  [reviewer-checked] - Tier B  (generalization 1)
     v
  [ GATE 4 - release ]        full suite green; assume-green deploy; rollback path exists  (generalization 5)
```

Full definitions: [`templates/review-gates.md`](templates/review-gates.md). Gate 3 is skipped for
non-Critical changes - that is the point of tiering it.

---

## Roles (each a separate context)

| Role | Does | Independence |
|---|---|---|
| **Orchestrator** | Plans the coupling-aware DAG, sizes builders, integrates serially, owns the composition root / integration seam, runs the gates | The only role that edits the seam |
| **Builder (one per stack)** | Builds one story to its ACs on an isolated worktree; runs the affected-stack gate; never holds a long-running server | A fresh context per story |
| **Structural reviewer** | Attacks each diff and each plan from a separate context; automated PR bot + a review agent | Tier A - always, cheap |
| **Human sign-off** | Approves Critical-class changes only | Tier B - reserved |

Every stack the repo contains gets its own builder role and its own gate. If a stack has no builder and
no gate, it is orphaned - that is the pulse backend failure mode.

---

## Artifacts (portable, and their lifespan)

| Artifact | Purpose | Lifespan |
|---|---|---|
| Charter (`README` + agent/contributor guide) | Vision, stack, the architectural bet(s), non-negotiables | Durable |
| ADR (`docs/adr/NNNN-*.md`) | A challenged decision + its adversarial findings-as-requirements | Durable (retire via a later ADR, on the record) |
| Story (`NN-*.md`) | The unit of work + its acceptance criteria (the spec) | Durable |
| Implementation plan ([`templates/implementation.md`](templates/implementation.md)) | Reuse map + coupling-aware wave plan (with the integration-seam row + `stack:` field) | **Disposable after merge** - do not maintain or backfill |
| Tracker mirror | Visibility only, **generated one-way** from the markdown | Regenerated, never hand-kept |
| Minimal CI ([`templates/ci-minimal.yml`](templates/ci-minimal.yml)) | The machine gate - the floor | Durable |
| Incident log (`docs/incidents/*.md`) | Proportional Operate record | Durable, append-only |

---

## Adopt it by your confounds, not by faith

Which pieces are load-bearing vs optional depends entirely on your Step-0 answers. That mapping is
[`ADOPTION_CHECKLIST.md`](ADOPTION_CHECKLIST.md). The short version: the charter, ADRs, docs-as-code
stories, structural (agent/bot) independence, the machine PR gate, the stack-agnostic DoD, seams-first
with a named seam owner, and a proportional rollback are **load-bearing for everyone**. The human
sign-off tier, wave width, and CI subsetting **flex with your team shape, coupling, and CI maturity.**
