# The Development Process - review package

> **A self-contained package for critique.** This bundles four documents describing a software
> development process built around AI coding agents, as proven on **QuibbleStone** (a solo, roughly
> ten-hours-a-week build that went from an empty repository to a full shippable alpha - about **126
> merged pull requests** - in roughly **8 to 11 calendar days**, June 30 to July 10, 2026).
>
> **Read the compression honestly:** "126 PRs in 8-11 days" is calendar time on a ~10 hrs/week budget,
> roughly **15-20 labor-hours**. It is a fact about **agent parallelism, not human throughput**, and it
> is proven once, under one set of conditions (solo, greenfield, one architectural bet, fast CI). See
> Part II, "The proof (and its limits)."
>
> It is shared as a **proposal to pressure-test, not a finished standard.** You are invited to add to
> it, refute it, and modify it. A ready-to-use review prompt accompanies this package; if you were
> handed this without one, treat every claim as a hypothesis and critique it on completeness, failure
> modes, cost, evidence, portability, and prior art.
>
> This revision incorporates an adversarial review from the sibling `cadence` repo (11 findings). A
> `pulse` review is still pending. Provenance: the process lineage is COBRA prototype -> cadence /
> pulse -> QuibbleStone.
>
> The four parts below were written as separate repository documents; internal cross-links (for
> example "see Part II") refer to the corresponding Part of this package. Where a link points at an
> operating manual not included here (the orchestration playbook, the tracker cheat sheet, the feature
> templates), that is repo-specific detail deliberately left out of this portable package - the four
> parts stand on their own.

---

## Contents

- **Part I** - The formal process (the method, stage by stage)
- **Part II** - Executive summary (what / why / benefits)
- **Part III** - How it evolved (the development history of the process itself)
- **Part IV** - Adoption guide + a portable cross-project alignment checklist

---



<!-- ===== Part I - The formal process ===== -->

# Part I - The formal process

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

## 0.5. What this borrows (prior art)

Little here is invented; most is assembled. Naming the lineage is honest and lets an adopter reuse
decades of refinement instead of rediscovering it.

| This process's part | Prior art it descends from | What to steal from the original |
|---|---|---|
| ADR + Stage-2 challenge | Design docs / RFCs (Nygard ADRs; Rust/Python RFC process) | Comment window, a named decision rule (lazy consensus), a shepherd |
| The one architectural bet | Architecture principles / fitness functions (evolutionary architecture) | Automate the bet as a test where possible, not just a review checklist item |
| Thin vertical slice | Walking skeleton (Cockburn) | Ship the skeleton end-to-end on day one, then thicken |
| Adversarial lenses | Pre-mortem (Klein) | Frame as "assume it failed - why?"; rotate who runs it |
| Three gates | Staged CI pipelines | Test-impact analysis so the early gate is a subset, not the whole suite |
| Two-lane deploy | Trunk-based dev + progressive delivery | Feature flags to decouple deploy from release |

The genuinely net-new contribution is narrower and worth stating plainly: **`implementation.md` as a
disposable planning-to-fan-out bridge**, and **findings-as-binding-ACs**. Everything else is good
assembly of known parts.

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
     |
     v
  [ STAGE 5b ] Operate .................. incidents / hotfix lane / rollback + feature flags
                                          (the paths that do NOT re-enter at Stage 1)
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
   specific stories that must satisfy them. The review must be run by an actor **other than the ADR's
   author** (a second person, or - solo - a distinctly-prompted agent whose sole mandate is "attack
   this plan; default to rejecting"). A plan cannot red-team itself.
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

**Independence rule.** The lenses are only as good as the distance between reviewer and author. Run
the review as a separate session with a separate actor and an explicit adversarial mandate; where
reviewer and author are the same human, that human must at minimum switch roles deliberately and
record which lens caught what. Log the review's catch rate and periodic false-negative spot-audits -
the adversarial claim should rest on data, not on one memorable finding.

The output is a set of **numbered findings**, and this is the crucial mechanic: **findings become
binding requirements written onto the specific stories that must satisfy them.** They are not a
review comment that evaporates; they are a "Security posture" section in the ADR that every named
story is reviewed against - until a later ADR explicitly retires or supersedes the requirement, using
the same cross-link honesty ADRs already apply to amendments. "Forever" without a retirement path
turns yesterday's finding into tomorrow's cargo-cult constraint; a binding requirement can be
un-bound, on the record.

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

Each story is a single, INVEST-oriented unit (Independent, Negotiable, Valuable, Estimable, Small,
Testable) - small enough to hand a coding agent one at a time - noting that stories are ordered by the
wave DAG, so "Independent" means **independently testable**, not free of dependency. The template is
fixed (README section 11) and every story carries:

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
parallelizable without a second analysis pass.

**Lifespan: disposable after merge.** The reuse map and wave plan are valuable at planning time and
valueless once the feature lands and the code moves. Do not maintain them post-merge, and do not
backfill them onto shipped features - a rotted wave plan has no readers. Only the ADR and the stories
are durable.

Three parts:

1. **Per-story tech notes** - approach, key files, what each story exports that others import.
2. **Reuse map** - the existing components/hooks/utilities each story must reuse instead of
   reinventing. This is what keeps N parallel builders consistent with each other and faithful to the
   architectural bet.
3. **A DAG-ready Wave Plan** - a table giving, per story: `Files it owns | Depends-on | Coupling
   surface | Can-run-with | Wave | Effort`. Sized by **two axes, not one**: **file-footprint
   disjointness** (no working-tree collision) and **semantic coupling** (shared types, runtime
   protocols, migrations, config, DI registration). Disjoint files can still be coupled through a
   contract, and that coupling - not the file overlap - is what breaks at integration. On a greenfield
   repo the two axes nearly coincide and waves fan wide; on a coupled or legacy repo they diverge
   sharply, most stories share hotspots, and expected parallelism width scales inversely with codebase
   coupling. There, the first feature's real job is often to **create seams before fan-out pays off** -
   plan for near-serial early waves and say so. Foundation first; any producer-to-consumer chain (here,
   an API/hub signature and the web code that calls it) is serialized because the contract is
   hand-kept, not generated.

### 5.4 The tracker mirror (one-way, or not at all)

The markdown is canonical; the tracker is **generated from it, one-way**, for visibility - never
hand-maintained in parallel. Double-entry bookkeeping between markdown and issues dies the first time
discipline slips (the doc's own "when they disagree, the repo wins" is an admission that they *will*
disagree). If you cannot script the mirror, **drop the tracker** rather than maintain two sources by
hand. The live model is a three-level **sub-issue hierarchy**: an **Epic** (one per build phase)
contains **Feature** issues, which contain **Story** issues. Status is carried in the markdown
`**Status:**` line (canonical) and mirrored by a `status:*` label on the issue. Full mapping and
commands: [`../GITHUB_TRACKER.md`](../GITHUB_TRACKER.md).

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

**When CI is slow (> ~10 min), the gates change shape - this is required, not optional.** Gate 1 runs
a **fast subset only**: lint, type-check, and the tests affected by the builder's diff (test-impact
analysis), not the full suite. The full suite and the security scans run once, at Gate 2
(post-integration) and Gate 3 (remote). Running a 30-minute pipeline per builder at Gate 1 makes the
wave loop CI-bound and silently kills the parallelism the process exists to buy.

### 6.3 Verification checkpoint (the wave boundary)

At each wave boundary the **main session** (which holds the long-running dev servers) boots the app
against the latest umbrella code and **walks the wave's user journeys**. The scary paths - real-time,
multi-device - are driven with **two browser contexts** (one creates, one joins). Wherever a journey
can be automated, it lands as a **Playwright e2e test in the same PR** - verification then produces a
**durable regression asset** instead of an ephemeral manual ritual, consistent with the docs-as-code
principle the rest of the process obeys. Manual owner sign-off is reserved for genuinely novel UX that
is not yet worth automating. Unrelated surfaces are checked for regression. Feedback folds into fix
items or the next wave, and into the feature's Decisions log. **The wave does not proceed without the
owner's sign-off.** This is the human-in-the-loop that keeps a fast, parallel build honest.

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

## 7.5. Stage 5b - Operate (the path the happy path forgets)

Not every change is a planned feature, and not every deploy stays up. This stage names the three paths
the pipeline otherwise drops on the floor.

- **Feature flags decouple merge from release.** Incomplete work integrates to trunk **behind a flag**
  rather than hoarding on a long-lived umbrella branch. A wave can land dark; the flag flips when
  verification signs off. This is what makes continuous integration safe and keeps the umbrella
  short-lived (see the branch-age rule below).
- **Hotfix lane.** A production incident does **not** re-enter at Stage 1. Branch from the current
  production tag, make the minimal fix, run the one gate that matters (fast checks + a targeted
  review), tag, and promote. Backfill the story/ADR **after** the fire is out, never before.
- **Rollback is a first-class action.** Every `v*` promotion has a defined reverse: revert-and-re-tag,
  or flip the flag off. The runbook states the rollback step next to the promote step. "We'll figure
  it out live" is not a rollback plan.
- **Umbrella branch-age rule.** `feat/{slug}` is a convenience, not a home. It rebases onto `main` at
  every wave boundary and lives no longer than the feature; anything expected to outlive a few waves
  integrates to trunk behind a flag instead. A long-lived feature branch is the exact failure
  trunk-based development exists to prevent, and it is invisible only because a solo greenfield trunk
  barely moves.

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
- **The forgotten operate path.** Treating a production incident as a fresh Stage-1 idea, shipping
  without a defined rollback, or hoarding a feature on an umbrella branch instead of integrating behind
  a flag. The pipeline is not done at "merged" - it is done at "running, and reversible" (Stage 5b).

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


---



<!-- ===== Part II - Executive summary ===== -->

# Part II - Executive summary

# Executive Summary: the Development Process

**A spec-driven, adversarially-reviewed, agent-orchestrated way to build software - proven on
QuibbleStone.**

---

## The what (in three sentences)

Work moves through five stages - **charter, challenge, decompose, orchestrate, ship** - and every
stage produces a durable text artifact in the repository and ends at a human checkpoint before the
next stage runs. An idea is first challenged by a multi-lens **adversarial review** whose findings
become binding requirements; it is then decomposed into small stories with strong acceptance criteria
plus a machine-readable wave plan; that plan is built by **parallel AI builder subagents** on isolated
worktrees, each gated by automated code review and CI, and verified in a real browser at every wave
boundary. Because the plan, the critique of the plan, the stories, and the code all version together
as markdown and travel through the same pull requests, the process is transparent, resumable, and
portable to any project.

## The why (the problem it solves)

Agent-assisted development is fast, but speed without structure produces a pile of locally-correct
diffs that do not add up to coherent software - and its most expensive failure mode is
**parallelizing a wrong plan** (paying N builders to faithfully build the wrong thing). The
traditional fixes (heavyweight process, a senior engineer reviewing everything by hand) do not survive
a solo, nights-and-weekends budget. This process gets the speed of parallel agents **and** the
coherence of a well-run team by putting the judgment where it is cheap - a human checkpoint at each
stage seam, and an adversarial critique **before** any code is written - and automating the rest.

## How it is different

| Common practice | This process |
|---|---|
| Design lives in someone's head or a chat thread | Design is a numbered, dated **ADR** in the repo, with rationale and lineage |
| Review happens after code is written | An **adversarial, multi-lens review happens before code**, and its findings become story requirements |
| Tickets are thin titles in a tracker | **Stories are the spec**: Given/When/Then ACs, out-of-scope guards, a reuse map |
| "Parallelize it" means hope for no conflicts | A **DAG-ready wave plan** sizes work by file-disjointness so parallel builders cannot collide |
| Plan and build blur together | Planning is its **own merged PR**; building starts from a clean, signed-off base |
| CI is one gate at the end | **Three layered gates**: per-builder, post-integration, and pre-ready |
| "Done" means the code compiles | "Done" means ACs met, gates green, and **verified in a real browser with the owner** |

## The benefits

- **Quality is designed in, not bolted on.** The adversarial review catches the class of bug that
  reaches production elsewhere (a security gate a user can defeat, a plan whose "parallel" stories
  collide) while it is still a paragraph, not a deploy.
- **Speed without chaos.** Parallel builders compress a multi-story feature into a few gated waves,
  and the wave plan makes the parallelism safe rather than hopeful.
- **The most expensive mistake is made impossible.** A wrong plan is caught at a cheap review
  checkpoint, never after N builders have already built it.
- **Everything is transparent and auditable.** The decision, the critique, the spec, and the code are
  all in the repo, versioned, and linked. Anyone can trace a line of code back to the AC, the ADR, and
  the finding that shaped it.
- **It is resumable and handoff-safe.** Because the artifact is the interface between stages, any
  stage can be stopped, resumed, or handed to a different session (or person) with no context loss.
- **It scales down.** Small changes skip the heavy path and go straight to the build tools, so the
  ceremony only appears where it earns its keep.
- **It is portable in structure - with named assumptions to adapt.** The artifacts and checkpoints
  drop into any repository, but six things are load-bearing and must be adapted, not assumed:
  greenfield-vs-coupled codebase, solo-vs-team review latency, whether the design compresses to one
  architectural bet, file-disjointness of stories, fast-vs-slow CI, and an owner who can eyeball the
  running app. Part IV walks each. Portable does not mean drop-in.

## The proof (and its limits)

QuibbleStone shipped a full alpha using this process - real-time rooms with roster and host migration,
four game modes on one shared engine, a metered AI cost gate, anonymous accounts with billing, an
operator console, and a two-lane deploy - with hundreds of passing tests, a green trunk, and a
child-safety / anonymity posture that held across every feature.

State the result honestly. The often-quoted "126 PRs in 8-11 days" is **calendar time on a ~10
hrs/week budget - roughly 15-20 labor-hours**. Calendar compression is a fact about **agent
parallelism, not human throughput**, and it must not be read as "126 human-reviewed changes per day."
Treat every claim in this document as a hypothesis **proven once, under these conditions**:

| Confound | Value here | Why it matters |
|---|---|---|
| Team size | Solo | Every "human checkpoint" is the same person; no review latency, no handoff loss |
| Codebase age | Greenfield | Stories own disjoint files by construction; parallelism is free |
| Roles | Owner = author = reviewer = verifier | The adversarial review critiques its author's own plan |
| CI | Fast, local-mirrorable | The three-gate model is cheap only because CI is cheap |
| Design | One clean architectural bet | Not every domain compresses to one sentence |

Nothing here proves the process **at team scale, on a legacy codebase, or under slow CI**. The
adoption guide (Part IV) is where those adaptations live; the executive claims above inherit these
caveats.

## The one-line version

> **Challenge the idea before you build it, make the spec the contract, let parallel agents build to
> it under three gates, and verify with a human at every wave - all as versioned text in the repo.**

For the full method see [`DEVELOPMENT_PROCESS.md`](DEVELOPMENT_PROCESS.md); for how it grew see
[`PROCESS_EVOLUTION.md`](PROCESS_EVOLUTION.md); to adopt it see [`ADOPTION_GUIDE.md`](ADOPTION_GUIDE.md).


---



<!-- ===== Part III - How it evolved ===== -->

# Part III - How it evolved

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

- **Cross-project alignment** - carrying this process to sibling projects (cadence, pulse, the cobra
  prototypes) and reconciling their process artifacts against it. The portable checklist for that lives
  in [`ADOPTION_GUIDE.md`](ADOPTION_GUIDE.md). (This is already underway: a cadence-repo adversarial
  review hardened this process - see the revision note in [`README.md`](README.md); a pulse review is
  still pending.)
- **Tightening the adversarial-review lenses into a reusable checklist** so the five lenses are run the
  same way every time rather than reconstructed per ADR.

The process is, fittingly, subject to its own discipline: changes to it are proposed, challenged, and
recorded as text - the same pipeline it describes.


---



<!-- ===== Part IV - Adoption guide + alignment checklist ===== -->

# Part IV - Adoption guide + alignment checklist

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
  remote CI. If your build is slow, **Gate 1 must collapse to an affected-tests subset** (see the
  formal process, Stage 4 gate model); the full suite moves to Gates 2-3. A three-gate model over a
  30-minute pipeline is not a lighter version - it is three times the wait.
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

**What a real sibling project reveals.** When this process is run against an existing repo, the parts
that take root **without a mandate** are the generic ones - the charter (step 1) and docs-as-code
stories with Gherkin ACs (step 3), which are prior art and immediately useful. The parts that need a
**deliberate mandate, a non-author reviewer, and CI budget** - the Stage-2 adversarial ADR (step 6)
and the `implementation.md` bridge (step 4) - are the ones a busy team quietly skips or reinvents as a
lighter manual table. Adopt steps 1 and 3 expecting them to stick; adopt steps 4 and 6 expecting to
**defend** them, or they will not.

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


---
