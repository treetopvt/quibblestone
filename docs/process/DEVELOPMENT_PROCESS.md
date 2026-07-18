<!--
  The Development Process (idea to ship) - the formal, end-to-end description of how work moves from
  an idea, through an adversarial challenge, into epics/features/stories with strong acceptance
  criteria, and out through agent-orchestrated implementation with code review and gated integration.
  This is the source-of-truth NARRATIVE that ties the existing operating manuals together:
    - docs/FEATURE_ORCHESTRATION_PLAYBOOK.md  (the build-phase manual the orchestrate-feature skill executes)
    - docs/GITHUB_TRACKER.md                  (how the docs-as-code backlog mirrors to GitHub)
    - docs/ADOPTION_NOTES.md                  (how the orchestration pattern was adapted into this repo)
    - docs/features/README.md + _template/    (the backlog-as-code structure and templates)
    - docs/adr/                               (the adversarial decision records)
  The repo README.md is the charter; if anything here conflicts with it, the README wins - flag it.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# The Development Process (idea to ship)

**A spec-driven, adversarially-reviewed, agent-orchestrated way to build software.** Proven on
QuibbleStone: a solo, nights-and-weekends build that nonetheless shipped a full alpha (rooms, roster,
host migration, four game modes on one engine, an AI cost gate, accounts + billing, an operator
console, and two cloud lanes) without losing momentum or coherence.

This document is the **formal, end-to-end description** of the process. It is deliberately
project-neutral in its bones so it can be lifted into other repositories, but every example is real
and lives in this tree. The companion documents are the executable detail:

| You want... | Read |
|---|---|
| The one-page pitch (what / why / benefits) | [`EXECUTIVE_SUMMARY.md`](EXECUTIVE_SUMMARY.md) |
| How the process grew into its current shape | [`PROCESS_EVOLUTION.md`](PROCESS_EVOLUTION.md) |
| How to adopt it on another project + a portable alignment checklist | [`ADOPTION_GUIDE.md`](ADOPTION_GUIDE.md) |
| The build-phase manual (orchestration mechanics) | [`../FEATURE_ORCHESTRATION_PLAYBOOK.md`](../FEATURE_ORCHESTRATION_PLAYBOOK.md) |
| How the backlog mirrors to the tracker | [`../GITHUB_TRACKER.md`](../GITHUB_TRACKER.md) |

---

## 0. What this process is (and is not)

It is a **pipeline with checkpoints**, not a ceremony. An idea enters as intent; it leaves as merged,
verified, deployed code. Between those two points sit five stages, each of which produces a **durable
artifact** and ends at a **human checkpoint** before the next stage can consume its output. The
artifacts are all **plain text in the repository** (docs-as-code), so the plan, the challenge to the
plan, the stories, and the code all version together and travel through the same pull requests.

The load-bearing idea is a **separation of concerns across time**: thinking, specifying, and building
are different jobs done in different sessions, and the seam between each is a place a human signs off.
That separation is what lets the expensive, parallel, agent-driven build phase run fast without
building the wrong thing.

It is **not**: a waterfall (stages loop and feed back), a replacement for judgment (every checkpoint
is a human decision), or a mandate for heavyweight process on trivial work (small changes skip
straight to the build tools - see the scope rule in section 2).

---

## 1. The shape in one picture

```
  IDEA / INTENT
     |
     v
  [ STAGE 1 ] Charter & framing ......... README (charter) + CLAUDE.md (working agreement)
     |                                     the durable constraints every later stage inherits
     |  -- checkpoint: is this in scope for the current slice? --
     v
  [ STAGE 2 ] Flesh out & CHALLENGE ..... an ADR (docs/adr/NNNN-*.md)
     |                                     research spike (optional) -> decisions -> a multi-lens
     |                                     ADVERSARIAL review whose findings become binding
     |  -- checkpoint: owner accepts the ADR; findings are recorded as requirements --
     v
  [ STAGE 3 ] Decompose to spec ......... docs/features/{slug}/  (feature.md + NN-story.md + implementation.md)
     |                                     epics -> features -> stories with strong ACs; a DAG-ready
     |                                     wave plan; GitHub Epic/Feature/Story issues mirror it
     |  -- checkpoint: planning-docs PR reviewed and MERGED (in its own session) --
     v
  [ STAGE 4 ] Orchestrated build ........ feat/{slug} umbrella branch + worktree builder subagents
     |                                     per wave: fan out -> Gate 1 -> integrate -> Gate 2
     |  -- checkpoint: main-session app verification at each wave boundary; user sign-off --
     v
  [ STAGE 5 ] Integrate, verify, ship ... draft PR -> Gate 3 (remote CI green) -> ready -> merge
     |                                     merge auto-deploys to qa; a v* tag promotes to beta
     |  -- checkpoint: offer to promote; the owner tags deliberately --
     v
  SHIPPED  (and the ROADMAP + story Status fields updated to say so)
```

Two rules give the picture its power:

1. **Each arrow crosses a session boundary and a human checkpoint.** The output of one stage is
   reviewed before the next stage runs. A wrong plan caught at the Stage 3 checkpoint costs a review;
   the same wrong plan caught after Stage 4 costs N parallel builders faithfully building the wrong
   thing (the single most expensive failure mode this process exists to prevent).
2. **The artifact is the interface between stages.** Stage 4 does not need the person who wrote the
   ADR in the room; it needs the merged `implementation.md`. This is what makes the build phase
   resumable, parallelizable, and handed-to-an-agent-able.

---

## 2. Principles (the invariants that make it work)

These hold across every stage. They are the reason the pipeline produces coherent software instead of
a pile of locally-correct diffs.

1. **Docs-as-code, markdown canonical.** The charter, the decisions, the backlog, and the plan all
   live in the repo as text. When the repo and any external mirror (the tracker, a board) disagree,
   the repo wins. Content lives in markdown; the tracker carries **visibility** (status, the work
   queue), not truth.
2. **Separate the sessions.** Exploration, planning, and orchestration load different context and
   want clean context. Planning is its own session that ends in a merged PR; orchestration starts
   fresh from `main`. This is context hygiene and resumability, not bureaucracy.
3. **Challenge before you build.** No non-trivial plan reaches code without an adversarial pass
   against it (Stage 2). Findings are not advice - they become **binding requirements** named on the
   specific stories that must satisfy them.
4. **The story is the spec.** Build to the acceptance criteria; ACs drive the tests. Behavior not in
   an AC is either a new story or silent scope creep - the code reviewer flags it either way.
5. **A charter-level architectural bet, stated once.** Every project has one load-bearing design idea
   (for QuibbleStone: "one engine, many thin modes" - every game variation is the same engine,
   configured, never a fork). Stages 2-4 are all measured against it; a diff that forks the engine is
   a smell caught at review.
6. **Thin vertical slice discipline.** Build the smallest end-to-end thing that delivers the core
   value, then iterate. Great ideas that are not in the current slice are **parked** in a backlog
   stub, not built. Parallelism is never an excuse to build breadth before the slice is proven.
7. **Non-negotiables are designed in, reviewed every time.** Each project names a small set of
   invariants that can never regress (for QuibbleStone: child safety and player anonymity). They ride
   in every builder brief and are a standing item on the review checklist, not a later hardening pass.
8. **Gates are cheap and layered.** Fast local checks keep junk out of the merge; a re-run after
   integration protects the shared branch; the remote CI run is the final word before a PR goes
   ready. Nothing becomes reviewable until all three pass.
9. **The scope rule (when NOT to use the heavy path).** The full pipeline is for large, planned,
   multi-story features. Small changes, bug fixes, and refactors skip Stages 2-3 and go straight to
   the build tools (the frontend/back-end agent, the code reviewer, the local CI check). Applying
   orchestration to a one-file fix is its own anti-pattern.

---

## 3. Stage 1 - Charter and framing

**Goal:** capture the idea and bind it to the constraints it must live within, before any design.

**Inputs:** an idea, a problem, a piece of user feedback, a gap the roadmap exposes.

**What happens:** the idea is framed against two standing documents:

- **The charter (`README.md`).** The vision, the stack, the architecture, the phase plan, and the
  one architectural bet. It is the source of truth; everything downstream cites it. Vision-level ideas
  that are not yet ready to design live here as backlog stubs (parked, per principle 6).
- **The working agreement (`CLAUDE.md`).** The operating rules for anyone (human or agent) touching
  the code: conventions, the stack's deliberate exclusions, the non-negotiables, the prose style. It
  points at the roadmap for "what to do next" and defers to the README on conflicts.

The **roadmap (`docs/ROADMAP.md`)** is the living index over all of this: dated, updated as work
lands, and the place a session starts to pick the next item. Every roadmap line traces to a story in
the backlog - the roadmap is a map, never new scope.

**Checkpoint:** is this idea in scope for the current slice? If yes, it proceeds to Stage 2 (if it
needs a design decision) or straight to Stage 3 (if it is a well-understood addition). If no, it is
parked in the relevant `feature.md` stub and the session moves on.

**Artifact produced:** at minimum, a roadmap line or a parked backlog stub. The charter itself is
rarely rewritten; it is the fixed frame.

---

## 4. Stage 2 - Flesh out and adversarially challenge

**Goal:** turn a fuzzy idea into a committed design decision that has already survived its own
strongest critique, so that Stage 3 decomposes something correct.

This is the stage the rest of the industry tends to skip, and it is where this process earns most of
its quality. It has three moves.

### 4.1 Research spike (optional, when the design turns on an unknown)

When a decision depends on a fact nobody has yet (a provider's real cost, an SDK's actual shape, a
latency budget), run a **timeboxed spike** whose deliverable is a written recommendation, not code.
The spike states its method and its **honest limits** (what it could and could not verify), gives
findings, and surfaces the open decisions for the owner. It commits its output as an ADR.

> Real example: [`docs/adr/0001-ai-provider.md`](../adr/0001-ai-provider.md) is a committed spike -
> seven questions on model, cost, moderation, SDK integration, quotas, and a go/no-go, with a candid
> "it was not possible to make a live call from the sandbox, so these token counts are estimates" and
> a first-build step to "measure one real call and confirm the estimate." It established the
> `docs/adr/` convention itself.

### 4.2 The decision record (ADR)

The design is written as an **Architecture Decision Record**: a numbered, dated markdown file that
records **Context** (the tension or problem, cited to the charter), **Decisions** (what the owner
chose, and why), the **invariant(s) retained**, **Consequences**, and any decisions it **supersedes
or amends**. ADRs are durable and honest about their own lineage - a later ADR that changes an
earlier stance says so in both files (for example ADR 0003 amends two stances of ADR 0002 and both
records cross-link).

The ADR is where an idea stops being a conversation and becomes a **position with a paper trail**.

### 4.3 The adversarial review (the heart of the stage)

Before any code, the plan is attacked from several fixed lenses. Each lens is a distinct failure mode;
running them as separate passes stops one reviewer's blind spot from covering another's. The lenses
proven here:

- **Invariant lens:** does anything in the plan violate a non-negotiable (for QuibbleStone: does any
  path put PII on the play plane, or let content escape the safety filter)?
- **Abuse / security lens:** how does a motivated bad actor defeat this? (This lens is what reclassified
  "a client-held token gates teen content" as broken - a lock whose holder can shed it is not
  enforcement - and inverted the default to family-safe.)
- **Wave-plan lens:** will the proposed parallelization actually work, or do two "parallel" stories
  collide on a shared file? (This lens caught a feature numbering its waves locally in a way that would
  mislead the orchestrator.)
- **Scope lens:** is this still the thin slice, or has breadth crept in?
- **Cold-builder lens:** can an agent with no prior context build this story from the brief alone, or
  does it hide a stateful gotcha (for example, a SignalR hub is rebuilt per invocation, so a resolved
  identity cannot be a hub field)?

The output is a set of **numbered findings**, and this is the crucial mechanic: **findings become
binding requirements written onto the specific stories that must satisfy them.** They are not a
review comment that evaporates; they are a "Security posture" section in the ADR that every named
story is reviewed against, forever.

> Real example: [`docs/adr/0003-admin-platform-and-family-accounts.md`](../adr/0003-admin-platform-and-family-accounts.md)
> ran "a five-lens adversarial review (invariant, abuse/security, wave-plan, scope, cold-builder)
> against the plan before any code." Its findings redesigned the teen-content gate, added an explicit
> nickname carve-out to the anonymity invariant, corrected the wave plan's file-collision hazards, and
> became a list of binding requirements ("Handles are secrets, and are treated as secrets";
> "Identity is discarded at the boundary, structurally") each tagged to the stories that must honor it.

**Checkpoint:** the owner accepts the ADR (Status: Accepted). The open decisions are resolved and
recorded; the findings are captured as requirements. Only now is the design real.

**Artifact produced:** a numbered ADR under `docs/adr/`, with resolved decisions, retained invariants,
and adversarial findings-as-requirements.

---

## 5. Stage 3 - Decompose to spec

**Goal:** turn the accepted design into an ordered backlog of small, testable, orchestration-ready
units - the backlog as code.

This is the **story-agent's** job (`.claude/agents/story-agent.md`), run as a **planning session**
(principle 2). It produces, under `docs/features/{slug}/`:

### 5.1 `feature.md` - what the feature is and why

A short record: summary, the charter section it traces to, the ordered list of stories, dependencies,
and design notes. One folder per feature.

### 5.2 `NN-<story>.md` - the stories (order-prefixed, one PR-sized unit each)

Each story is a single, INVEST-quality unit (Independent, Negotiable, Valuable, Estimable, Small,
Testable) - small enough to hand a coding agent one at a time. The template is fixed (README section
11) and every story carries:

- **Context** (why it exists), a link back to `feature.md`.
- **Acceptance Criteria** - Given / When / Then, one observable behavior each, 3-7 per story. "If you
  cannot imagine a check for an AC, it is too vague." ACs drive the tests.
- **Out of Scope** - an explicit guard against creep.
- **Technical Notes** and **Dependencies**.
- **Non-negotiable ACs where relevant.** Any story touching a sensitive surface carries the standing
  invariant as an explicit, observable AC (for QuibbleStone: free-text surfaces carry a "passes the
  safety filter / honors the family-safe toggle / collects no PII" AC; paid-tier stories state the
  free-vs-paid behavior at session-creation time). The invariant is in the spec, not assumed.

### 5.3 `implementation.md` - the planning-to-orchestration bridge (the net-new artifact)

The single most important structural invention of this process. It is what makes the build phase
parallelizable without a second analysis pass. Three parts:

1. **Per-story tech notes** - approach, key files, what each story exports that others import.
2. **Reuse map** - the existing components/hooks/utilities each story must reuse instead of
   reinventing. This is what keeps N parallel builders consistent with each other and faithful to the
   architectural bet.
3. **A DAG-ready Wave Plan** - a table giving, per story: `Files it owns | Depends-on | Can-run-with |
   Wave | Effort`. Sized by **file-footprint disjointness**, so a wave can fan out with no further
   analysis. Foundation first; any producer-to-consumer chain (here, an API/hub signature and the web
   code that calls it) is serialized because the contract is hand-kept, not generated.

### 5.4 The tracker mirror

The markdown is canonical; the tracker mirrors it for visibility. The live model is a three-level
**sub-issue hierarchy**: an **Epic** (one per build phase) contains **Feature** issues, which contain
**Story** issues. Status is carried in the markdown `**Status:**` line (canonical) and mirrored by a
`status:*` label on the issue. Full mapping and commands: [`../GITHUB_TRACKER.md`](../GITHUB_TRACKER.md).

**Checkpoint (the most important seam in the process):** the planning docs are opened as their own
**planning-docs PR, reviewed, and merged** before any building starts. Planning and building are
never the same session. A human signs off on scope and ACs here, cheaply, before any builder runs.

**Artifact produced:** a fully specified, orchestration-ready `docs/features/{slug}/` folder, merged
to `main`, with its Epic/Feature/Story issues synced.

---

## 6. Stage 4 - Orchestrated implementation

**Goal:** build the whole feature to its ACs, fast, in parallel, without breaking the shared branch or
drifting from the spec.

This is the **`orchestrate-feature` skill** executing the
[Feature Orchestration Playbook](../FEATURE_ORCHESTRATION_PLAYBOOK.md), run as a **fresh session** off
the merged plan. The roles:

- **Orchestrator (the main session, high reasoning effort).** Plans the dependency DAG, sizes builders,
  fans out each wave, integrates serially, runs the gates, boots the app for verification, and manages
  the PR and tracker. It owns the expensive decisions.
- **Builder subagents (medium effort by default).** Each takes **one story** (or a tight, file-disjoint
  cluster) on its **own git worktree**, builds to the ACs, runs local checks, and returns a structured
  result. Builders are ephemeral: they never hold a long-running server. Web work uses the
  `frontend-agent`; API work is a general builder against the API conventions.

### 6.1 The wave loop

For each wave of the DAG:

1. **Fan out (a Workflow).** Each file-disjoint story becomes a worktree-isolated builder with a
   **self-contained brief**: the story file (ACs), its `implementation.md` note, the relevant reuse-map
   rows, and the **builder guardrails** (the project conventions and non-negotiables, verbatim, in
   every brief). The builder returns a schema'd result (`summary`, `filesTouched`, `ciStatus`,
   `openQuestions`).
2. **Gate 1 (per builder, in the Workflow).** A `code-review` pass on each builder's diff plus its
   local CI result. Only review-clean, CI-green branches are eligible to integrate. The review is
   adversarial by design: a plausible-but-wrong diff that slips Gate 1 breaks the umbrella at Gate 2.
3. **Integrate serially onto the umbrella (`feat/{slug}`).** The orchestrator merges eligible branches
   one at a time, resolving conflicts where footprints overlapped (shared files like a central theme, a
   hub, or the DI registration in a program entrypoint are the systemic hotspots - they merge one PR at
   a time even when everything else is parallel).
4. **Gate 2 (post-integration).** Re-run the full local CI on the merged result and re-review the
   integrated delta. This is the run that actually protects the shared branch, because integration
   introduces interactions no isolated worktree ever saw (a renamed export a sibling consumes, a shared
   type that drifted).

### 6.2 The three-gate model

```
  fan out builders -> [GATE 1: per builder]  local CI + code-review (only clean branches integrate)
  integrate serially -> [GATE 2: post-integration]  re-run CI on the MERGED tree + review the delta
  push + draft PR    -> [GATE 3: pre-ready]  remote CI (Actions) green + final review + verification
```

The first two gates are local and fast (they mirror the CI workflow); the third is the remote CI run
on the pushed branch. Nothing becomes a ready-for-review PR until all three pass. Two CI runs are not
redundant: Gate 1 keeps junk out of the merge cheaply; Gate 2 is the one that catches integration
breakage.

### 6.3 Verification checkpoint (the wave boundary)

At each wave boundary the **main session** (which holds the long-running dev servers) boots the app
against the latest umbrella code and **walks the wave's user journeys with the owner** through a real
browser. Real-time and multi-device stories are driven with **two browser contexts** (one creates,
one joins) to prove the scary part - live sync across devices - actually works. Unrelated surfaces are
checked for regression. Feedback folds into fix items or the next wave, and into the feature's
Decisions log. **The wave does not proceed without the owner's sign-off.** This is the human-in-the-loop
that keeps a fast, parallel build honest.

**Artifacts produced:** merged, gated, verified code on the `feat/{slug}` umbrella; updated story
Status; a draft PR accumulating `Closes #<issue>` links.

---

## 7. Stage 5 - Integrate, verify, ship

**Goal:** land the feature on the trunk and get it in front of real users, deliberately.

- **Gate 3 and ready.** With the umbrella pushed and the draft PR open, wait for remote CI to go green
  (`gh pr checks --watch`), confirm the final review is clean and verification is signed off, then
  promote the PR from draft to ready. Only then does it merge.
- **Two-lane promotion.** A merge to `main` **auto-deploys to qa** (the playground lane). Production
  ("beta", the friends-and-family site) moves **only on a deliberately pushed `v*` tag** validated in
  qa first. The deploy mechanics live in a runbook that must be read before any promotion.
- **Offer to promote, never auto-tag.** When a feature lands and is validated on qa, the process
  **proactively offers** a semver bump and beta promotion but leaves the call to the owner. Production
  only ever moves on a human's deliberate tag.
- **Close the loop.** Story Status flips to Complete, the issues close on merge, and the roadmap is
  updated to say what shipped. The map stays honest.

**Artifact produced:** deployed software, an updated roadmap, and closed stories - and, when the
feature surfaces something new, a fresh idea re-entering the pipeline at Stage 1.

---

## 8. Roles: the agents and skills registry

The process is executed by a small set of specialized agents and skills, each scoped to one job. This
division of labor is itself part of the method: a general agent asked to "build a feature" will drift;
a story-agent asked to write stories, a builder asked to satisfy ACs, and a reviewer asked to attack a
diff each stay in their lane.

| Actor | Kind | Stage | Job |
|---|---|---|---|
| `story-agent` | agent | 3 | Author/maintain `feature.md` + stories + `implementation.md`; sync the tracker; enforce slice discipline. Writes the spec; never writes code. |
| `orchestrate-feature` | skill | 4 | Drive the wave loop: DAG, fan-out, gates, integration, verification, PR. User-invoked only. |
| `frontend-agent` | agent | 4 | Build web stories to ACs under the stack conventions. |
| (general builder) | agent | 4 | Build API/infra stories to ACs under the API conventions. |
| `testing-agent` | agent | 4 | Extend the test suites for new features; prefer extracting pure logic and covering it. |
| `code-review` | agent | 4 (Gates 1-2) | Adversarially review a diff against the project's values, conventions, non-negotiables, and the story's ACs. Emits a machine-readable clean/not-clean verdict the Workflow reads. |
| `ci-check` | skill | 4 (Gates 1-2) | Run the local build/validate pipeline that mirrors remote CI, plus a project-sanity grep. |
| `commit` | skill | all | Conventional commit with scope detection. |

Each agent's brief encodes the same non-negotiables and conventions, so consistency is structural, not
a matter of every author remembering.

---

## 9. Artifact index (what the process produces and where it lives)

| Stage | Artifact | Location | Canonical for |
|---|---|---|---|
| 1 | Charter | `README.md` | Vision, stack, architecture, the one bet |
| 1 | Working agreement | `CLAUDE.md` | Conventions + non-negotiables for every contributor |
| 1 | Living index | `docs/ROADMAP.md` | Where the build is; what is next (a map, not scope) |
| 2 | Decision record | `docs/adr/NNNN-*.md` | The design, its rationale, retained invariants, and the adversarial findings-as-requirements |
| 3 | Feature spec | `docs/features/{slug}/feature.md` | What the feature is and why |
| 3 | Story | `docs/features/{slug}/NN-*.md` | The unit of work + its acceptance criteria (the spec) |
| 3 | Implementation plan | `docs/features/{slug}/implementation.md` | The reuse map + DAG-ready wave plan (planning-to-orchestration bridge) |
| 3 | Tracker mirror | GitHub Epic/Feature/Story issues | Visibility: status, the work queue |
| 4 | Umbrella branch + draft PR | `feat/{slug}` | The integrated, gated, verified delta |
| 5 | Deployed build | qa (on merge) / beta (on `v*` tag) | The running software |

The templates for the Stage 3 artifacts live in `docs/features/_template/`.

---

## 10. Anti-patterns (failure modes the process is built to prevent)

- **Parallelizing a wrong plan.** The most expensive failure: N builders faithfully building the wrong
  thing. Prevented by the Stage 3 planning-docs checkpoint (a human signs off on scope + ACs before any
  builder runs).
- **Planning and building in one session.** Muddies context, defeats resumability, and skips the
  checkpoint. Planning is always its own merged PR.
- **Findings that evaporate.** An adversarial review whose findings are comments, not requirements
  written onto stories, buys nothing. Findings are captured in the ADR and reviewed against every time.
- **Silent scope creep.** Behavior not in an AC. Caught by the reviewer ("should this be a new AC or a
  new story?") and by the Out-of-Scope section on every story.
- **Forking the shared abstraction.** A mode that becomes a parallel engine, a component that re-specs
  a shared contract. Caught by the reuse map and the reviewer.
- **Two builders, one file.** Worktree isolation prevents working-tree collisions, not merge conflicts.
  Sizing by file-footprint disjointness (the wave plan) is what actually prevents the collision.
- **Skipping a gate under pressure.** `--no-verify`, marking a PR ready before CI is green. The gates
  are the cheapest place to catch a break; bypassing one is never silent (tell the owner).
- **Orchestrating a one-file fix.** The heavy path on small work is overhead. Small, unplanned changes
  go straight to the build tools (scope rule, principle 9).
- **Overclaiming a guarantee.** The process records what it does NOT cover as explicitly as what it
  does (for example, "the group-play teen gate is fixed; the solo path is a known, tracked gap"). A
  guarantee stated once and quietly broken later is worse than a scoped one.

---

## 11. The effort and cost model

Reasoning effort (and therefore token cost) is spent where the expensive, error-prone decisions are:

- **Stage 2 (challenge) and Stage 4 orchestration: high.** DAG planning, builder sizing, integration
  and conflict resolution, and adversarial review are where extra reasoning pays for itself.
- **Builder subagents: medium by default** (a precise brief against established patterns), **low** for
  mechanical stories, **high** for novel or stateful ones.
- **The spike and story authoring: moderate.** Bounded, template-driven work.

The parallelism is bounded by the dependency DAG and file disjointness, not by ambition: a wave runs
as wide as its stories are independent, and no wider.

---

## 12. Relationship to the existing manuals

This document is the **narrative spine**. It does not restate the operating detail that already lives
in the tree; it points at it:

- The **build-phase mechanics** (phases, the three-gate model in full, builder guardrails verbatim,
  the per-wave Workflow skeleton, the worktree footguns) are in
  [`../FEATURE_ORCHESTRATION_PLAYBOOK.md`](../FEATURE_ORCHESTRATION_PLAYBOOK.md).
- The **tracker mapping and commands** are in [`../GITHUB_TRACKER.md`](../GITHUB_TRACKER.md).
- The **story and implementation templates** are in `docs/features/_template/` and README section 11.
- **How this pattern was first adapted into the repo** (what was kept, what was dropped from a generic
  template) is in [`../ADOPTION_NOTES.md`](../ADOPTION_NOTES.md).

If any of those conflict with this document, they are the more specific and more current source for
their own topic - reconcile toward them and flag the drift here. The README remains the charter above
all.
