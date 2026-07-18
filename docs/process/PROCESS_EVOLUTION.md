<!--
  How the development process grew into its current shape - the evolution/development narrative. Traces
  the process from a charter-only solo build through the ADR convention, the docs-as-code backlog, the
  adopted orchestration pattern, the implementation.md bridge, the formalized adversarial review, and
  the two-lane deploy. Written from the artifacts actually in this repo (ADRs 0001-0003, ADOPTION_NOTES,
  the feature backlog, the roadmap). The full current-state process is DEVELOPMENT_PROCESS.md.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# How the Process Evolved

The process in [`DEVELOPMENT_PROCESS.md`](DEVELOPMENT_PROCESS.md) did not arrive whole. It
accreted, one artifact at a time, each addition solving a specific pain the previous shape exposed.
This is the honest development history, reconstructed from the artifacts in this tree. It is worth
reading before adopting the process, because the **order** things were added tells you which parts are
load-bearing and which are conveniences.

The throughline: **every addition moved a piece of judgment earlier and made it durable.** The
process got better by front-loading more thinking and by writing that thinking down where it could be
reviewed and reused.

---

## Stage 0 - Charter-driven solo build (the starting point)

The build began the way most do: a **charter** (`README.md`) and a **working agreement**
(`CLAUDE.md`), a clear architectural bet ("one engine, many thin modes"), and hard discipline about
building a **thin vertical slice** before anything else. There was no orchestration, no ADRs, no
formal decomposition - just the charter and the discipline to ship the smallest fun thing first.

**What it got right, and kept forever:** the charter as source of truth, the single architectural bet,
the non-negotiables (child safety, anonymity) named up front, and slice discipline. These are the
bedrock every later addition assumed.

**What it lacked:** a way to make a hard design decision durably, and a way to build something bigger
than one person can hold in their head at once.

---

## Milestone 1 - The ADR convention (durable decisions)

The first real AI feature forced the first hard, fact-dependent decision (which model, at what cost,
behind what safety gate). The answer was a **timeboxed research spike whose deliverable was a written
recommendation** - and committing that recommendation as
[`docs/adr/0001-ai-provider.md`](../adr/0001-ai-provider.md) **established the `docs/adr/`
convention itself** (its own header says so).

Three habits were set here that never left:

- A decision is a **numbered, dated, committed record**, not a conversation.
- A spike is **honest about its limits** ("it was not possible to make a live call from the sandbox,
  so these token counts are estimates") and schedules a real-world confirmation as the first build
  step.
- An ADR **surfaces the open decisions** for the owner to resolve, then records the resolution.

This is the birth of Stage 2. At this point, though, the ADR was a decision record, not yet an
adversarial instrument.

---

## Milestone 2 - The docs-as-code backlog (the spec as text)

In parallel, the backlog moved **into the repo**: `docs/features/{slug}/` with a `feature.md` and
order-prefixed `NN-story.md` files, each story a PR-sized unit with Given/When/Then acceptance
criteria, an out-of-scope guard, and (where relevant) child-safety and entitlement ACs baked in as
observable behavior. The `story-agent` was defined to author and maintain this tree and to enforce
slice discipline (park parked ideas; do not let creep in).

The insight: **the story is the spec, and the spec belongs in the repo** next to the code that
satisfies it, versioning and travelling through the same pull requests. This is Stage 3 in its first
form - complete except for the bridge to a parallel build, which did not exist yet.

---

## Milestone 3 - The ADR becomes a position record (challenge begins)

[`docs/adr/0002-accounts-subscriptions-and-admin.md`](../adr/0002-accounts-subscriptions-and-admin.md)
was a different kind of ADR: an **exploration / position record** that mapped a genuine tension (charge
money and know a purchaser, while keeping every player anonymous forever), stated the **one
load-bearing invariant** that resolved it (entitlement travels with the session, not identity; no PII
on the play plane), and **surfaced six open decisions (A-F)** for the owner to resolve.

This is where the ADR grew from "record the decision" into "**work the problem, name the invariant,
and force the hard choices into the open**." The adversarial instinct is visible but not yet
systematized: the invariant is stated as the thing every future story will be reviewed against.

---

## Milestone 4 - The orchestration pattern is adopted (parallel building)

The backlog could describe a large feature, but building one still meant doing stories one at a time by
hand. The **feature-orchestration pattern** was adopted from a generalized template bundle and
**adapted, not imposed**, to this repo. [`../ADOPTION_NOTES.md`](../ADOPTION_NOTES.md) is the
honest record of that adaptation - what landed, what was kept from the existing setup, and what was
dropped from the generic template because it did not fit (all i18n machinery removed; the cross-repo /
codegen-client machinery removed because this is a single repo with a hand-kept contract; the tracker
rewritten to match the repo's real Epic/Feature/Story hierarchy rather than the template's milestone
scheme).

What landed and stuck:

- The [`FEATURE_ORCHESTRATION_PLAYBOOK.md`](../FEATURE_ORCHESTRATION_PLAYBOOK.md) - phases, the
  three-gate model, builder guardrails, the per-wave Workflow skeleton.
- The `orchestrate-feature` skill that executes it.
- Worktree-isolated builder subagents, serial integration onto a `feat/{slug}` umbrella, and
  main-session app verification at wave boundaries.

This is Stage 4. The lesson embedded in the adoption itself: **adapt a pattern to your repo's reality;
do not adopt the parts that do not apply.** ADOPTION_NOTES exists precisely so that the adaptation is
auditable.

---

## Milestone 5 - The `implementation.md` bridge (the net-new invention)

Adopting orchestration exposed a gap: the stories said *what* to build and the playbook said *how* to
orchestrate, but nothing connected them - nothing told the orchestrator which stories could run in
parallel without colliding. So a **net-new artifact** was invented (ADOPTION_NOTES flags it as
net-new, and the template header calls it "the bridge between planning and orchestration"):
`implementation.md`, carrying per-story tech notes, a **reuse map**, and a **DAG-ready Wave Plan**
sized by file-footprint disjointness.

This is the single most important structural invention of the process. It is what turns "we have
stories" into "we can safely fan out N builders," because the wave plan is pre-computed at planning
time and the orchestrator only validates it rather than deriving it. The convention hardened to
"**required for every fully-specified feature**."

---

## Milestone 6 - The adversarial review is formalized (challenge, systematized)

[`docs/adr/0003-admin-platform-and-family-accounts.md`](../adr/0003-admin-platform-and-family-accounts.md)
is where Stage 2 reached its mature form. A **five-lens adversarial review (invariant, abuse/security,
wave-plan, scope, cold-builder) ran against the plan before any code.** It did real work:

- It found the teen-content gate defended the wrong thing (a lock riding a client-held token the
  holder could simply clear) and **inverted the default to family-safe** with an adult-signal unlock.
- It added an honest **carve-out** to the anonymity invariant (household data an adult consents to,
  distinguished structurally from play-plane identity) rather than letting the invariant be quietly
  overclaimed.
- It **corrected the wave plan** (two features had numbered their waves in a way that would mislead an
  orchestrator; two "parallel" stories actually shared a file).
- Crucially, its findings were written into the ADR as a **"Security posture" section of binding
  requirements**, each tagged to the specific stories that must satisfy it ("Handles are secrets, and
  are treated as secrets"; "Identity is discarded at the boundary, structurally"; "The support console
  cannot bridge the planes").

Two further maturations landed here:

- **ADRs learned to amend each other honestly.** ADR 0003 amends two stances of ADR 0002; both files
  cross-link, and the retained invariant is described precisely (retained on the play plane, with an
  explicit account-plane carve-out) rather than claimed "verbatim."
- **The cross-feature build order became a first-class, orchestration-ready table** in the ADR, with
  the shared-file hotspots called out (a program entrypoint that nearly every story touches serializes
  even when everything else is parallel).

This is the moment the process became what it is: **challenge before build, findings as requirements,
the plan itself reviewed for parallelizability.**

---

## Milestone 7 - The two-lane deploy and the promotion discipline (ship, deliberately)

Shipping matured from a single manual deploy into **two lanes**: a merge to `main` auto-deploys to
**qa**; production ("beta") moves **only on a deliberately pushed `v*` tag** validated in qa first,
governed by a runbook that must be read before any promotion. A companion discipline was added to the
working agreement: when a feature lands and validates on qa, the process **proactively offers** a
semver bump and promotion but never auto-tags - production only ever moves on a human's deliberate
choice.

This closed the pipeline: an idea can now travel from Stage 1 all the way to a running production
build, with a human checkpoint at the final gate exactly as at every earlier one.

---

## The pattern in the evolution

Read as a whole, the history has a clear grain:

1. **Judgment moved earlier over time.** From "review the code after it is written" to "challenge the
   design before any code exists." The adversarial review is the endpoint of that movement.
2. **Ephemeral thinking became durable artifacts.** Chat-thread decisions became ADRs; head-held plans
   became `implementation.md`; "parallelize it" became a DAG-ready wave plan.
3. **Each artifact created its next need.** The backlog needed a bridge to orchestration
   (`implementation.md`); orchestration needed a challenge to trust the plan (the five-lens review);
   the challenge needed a way to bind its findings (findings-as-requirements on stories).
4. **Adaptation was always auditable.** When a pattern was adopted from outside, the adaptation was
   recorded (ADOPTION_NOTES) so future readers see what was changed and why.

The takeaway for an adopting team: you do not have to add every piece on day one. Add the charter and
slice discipline first (they are bedrock), then ADRs (durable decisions), then the docs-as-code
backlog (the spec as text), then orchestration and its `implementation.md` bridge, then the
adversarial review, then the deploy lanes. Each step is useful on its own and sets up the next.

---

## Where it goes next

The process continues to evolve at its own seams. The natural next steps visible in the tree:

- **Backfilling `implementation.md`** onto the earliest features (they predate the convention).
- **Cross-project alignment** - carrying this process to sibling projects (cadence, pulse, the cobra
  prototypes) and reconciling their process artifacts against it. The portable checklist for that lives
  in [`ADOPTION_GUIDE.md`](ADOPTION_GUIDE.md).
- **Tightening the adversarial-review lenses into a reusable checklist** so the five lenses are run the
  same way every time rather than reconstructed per ADR.

The process is, fittingly, subject to its own discipline: changes to it are proposed, challenged, and
recorded as text - the same pipeline it describes.
