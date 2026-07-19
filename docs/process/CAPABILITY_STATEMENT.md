<!--
  CAPABILITY_STATEMENT.md - a single consumable document that does double duty:
    (A) a partner / customer-facing "why choose us and our approach" capability statement, and
    (B) a resume-source section (headline, quantified achievements, skills inventory) a resume builder
        or a human can lift verbatim.
  Every claim is kept honest and traceable to the evidence in this repo (the QuibbleStone build, the
  cadence + pulse adversarial reviews, and the proven-vs-unproven ledger in methodology/). Replace the
  [Studio] placeholder with your brand before sending; swap "we/our" for "I/my" in Part B if you want
  first-person resume bullets.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Capability Statement: AI-Agent-Orchestrated Software Delivery

> **How to use this one document.** Part A is written to hand to a partner or a potential customer -
> the "why choose us and our approach" pitch. Part B is a resume source: a headline, quantified
> achievement bullets, and a skills inventory a resume builder (or you) can lift directly. Replace
> **[Studio]** with your brand. The claims are deliberately honest and evidence-backed - the honesty is
> part of the sell.

---

# Part A - The approach (for partners and customers)

## One line

**[Studio] builds software the way modern AI makes possible - fast, in parallel, by fleets of coding
agents - without the incoherence that usually comes with it. We put human judgment where it is
cheapest and most valuable (before the code is written), and let automation do the rest.**

## The problem we solve

AI coding agents made building software dramatically faster. But speed without structure produces a
pile of locally-correct changes that do not add up to a coherent system - and the most expensive
failure mode is subtle: **paying a fleet of agents to faithfully build the wrong plan.** Most teams get
speed *or* coherence. Traditional fixes (heavyweight process, a senior engineer hand-reviewing
everything) do not survive the pace or the budget.

## Our approach

We run a disciplined, five-stage pipeline where **the plan, the critique of the plan, the specification,
and the code all live together as versioned text**, and every stage hands the next a durable artifact
across a deliberate checkpoint:

1. **Charter** - bind the idea to the constraints and the one architectural bet it must respect.
2. **Challenge** - write the design as a decision record, then **attack it from multiple adversarial
   angles before any code exists.** The findings are not advice - they become binding acceptance
   criteria on the exact work that must satisfy them.
3. **Decompose** - turn the accepted design into small stories with testable, Given/When/Then
   acceptance criteria plus a coupling-aware build plan.
4. **Orchestrate** - build the whole feature in parallel with agent "builders," each on isolated
   ground, each gated by automated review and continuous integration, integrated one clean diff at a
   time.
5. **Ship and operate** - land it behind machine-enforced gates, deploy on green, with a defined,
   one-command rollback.

Two ideas make it work: **judgment moves earlier** (we challenge the design, not just review the code),
and **independence is structural** (every change is reviewed by a separate agent/bot context by default;
a human signs off only where the stakes demand it - security, contracts, money, safety).

## Why it is different

| Common practice | Our approach |
|---|---|
| Design lives in someone's head or a chat thread | A numbered, dated decision record in the repo, with rationale and lineage |
| Review happens after the code is written | An adversarial, multi-lens review happens **before** code, and its findings become binding requirements |
| Tickets are thin titles | Stories are the contract: testable acceptance criteria, out-of-scope guards, a reuse map |
| "Parallelize it" means hope for no conflicts | A coupling-aware plan sizes work so parallel agents cannot collide, and a single owner reconciles the integration seam |
| CI is one gate at the end | Layered, machine-enforced gates on every change and every technology stack - none left on the honor system |
| "Done" means the code compiles | "Done" means acceptance criteria met, gates green, and the change verified working |
| Vendors oversell certainty | We tell you exactly what our method has proven and what it has not (see below) |

## What you get

- **Quality designed in, not bolted on.** The adversarial review catches the class of defect that
  reaches production elsewhere - a security control a user can defeat, a plan whose "parallel" parts
  collide - while it is still a paragraph, not a deploy.
- **A fully auditable trail.** Every line of code traces back to its acceptance criterion, its decision
  record, and the review finding that shaped it. Nothing is a black box.
- **Clean, conflict-free delivery.** Work lands as small, independently-reviewable diffs that integrate
  without stepping on each other - fewer merge fires, faster review, safer trunk.
- **Right-sized process.** Small changes skip the heavy path; the ceremony only appears where it earns
  its keep. We do not sell you bureaucracy.
- **Built-in safety and privacy where it matters.** Non-negotiable constraints (child safety, data
  minimization, security boundaries) are enforced as acceptance criteria on every relevant surface, not
  deferred to a hardening phase.

## The proof (and our honesty about its limits)

We proved this method on **QuibbleStone**, a real, dual-stack, real-time multiplayer product:

- From an **empty repository to a full shippable alpha** - real-time rooms with host migration, four
  game modes on a single shared engine, a metered AI cost control, anonymous accounts with billing, an
  operator console, and a two-lane deployment pipeline.
- Built **solo** in roughly **15-20 labor-hours** (126 merged pull requests over an 8-to-11 day calendar
  window), with **880+ passing automated tests**, a continuously green trunk, and **zero child-safety or
  anonymity regressions** across every feature.

Then we did something most shops never do: **we turned the method on itself.** Two sibling projects
(`cadence`, then `pulse`) ran adversarial reviews *of the process*. They contradicted some of our
original assumptions - and we generalized to the contradictions rather than hiding them. The result is
a **stack-agnostic methodology with a published "proven vs unproven" ledger.**

Being candid about what is *not* yet proven is a feature, not a weakness: our method is proven for
solo-plus-agent teams on modern codebases; **multi-human review latency and full production operations
are honestly marked as still maturing.** You will always know which parts of our approach rest on
evidence and which rest on judgment. That is how we would want a vendor to talk to us.

## How we adapt to your project

We do not sell a one-size-fits-all playbook. Every engagement starts by characterizing five things
about *your* codebase - team shape, coupling, architectural clarity, CI maturity, and what already
exists - because the right amount of process is a function of those. We bring the method; we tune it to
your reality, and we tell you which pieces are load-bearing for you and which are optional.

---

# Part B - Resume source (lift these directly)

> First-person plural above ("we/our"); switch to "I/my" here for a personal resume. All figures are
> real and conservative.

## Headline options

- **AI-Agent-Orchestrated Software Delivery - methodology design and full-stack execution.**
- **Spec-driven, adversarially-reviewed engineering: shipping coherent software at agent speed.**
- **Process engineer and full-stack builder - designed and proved a repeatable AI-agent development method.**

## Professional summary (2-3 sentences)

Designed, proved, and hardened a repeatable methodology for building software with fleets of AI coding
agents - spec-driven, adversarially reviewed before any code, and gated at every step. Used it to take
a dual-stack, real-time product from empty repository to a full shippable alpha solo, in roughly 15-20
labor-hours, with 880+ passing tests and zero safety regressions. Stress-tested the method through two
independent adversarial reviews and generalized it into a stack-agnostic practice with an honest,
evidence-backed maturity ledger.

## Quantified achievements (bullets)

- **Designed and proved an AI-agent-orchestrated development methodology** that took a dual-stack
  (React/TypeScript + ASP.NET Core) real-time application from **empty repo to full shippable alpha in
  ~15-20 solo labor-hours** (126 merged pull requests), maintaining a continuously green trunk.
- **Shipped, on that methodology:** real-time multiplayer with host migration, four game modes on a
  single shared engine, a metered AI cost-control gate, anonymous accounts with billing, an operator
  console, and a two-lane (staging/production) deploy pipeline - **880+ passing automated tests, zero
  child-safety or anonymity regressions** across every feature.
- **Instituted "challenge before build":** a multi-lens adversarial review of every design whose
  findings become **binding acceptance criteria**, catching security and architecture defects while
  still at the design stage (e.g. redesigned an access-control gate a client could trivially bypass -
  before a line of code shipped).
- **Established a layered quality-gate system:** per-change and post-integration continuous integration
  plus a **two-tier review model** - automated/agent review on every change by default, human sign-off
  reserved for security-, contract-, and safety-critical changes.
- **Hardened the methodology through two independent adversarial reviews** of the process itself,
  generalizing it into a **stack-agnostic, adoption-checklist-driven practice** with a published
  proven-vs-unproven evidence ledger.
- **Engineered safety and privacy by design** (COPPA-conscious): profanity/safety filtering and data
  minimization enforced as non-negotiable acceptance criteria on every user-input surface; players kept
  fully anonymous while a paid entitlement model was layered on cleanly.
- **Practiced docs-as-code end to end:** charter, decision records, backlog stories, and build plans all
  versioned in-repo and traceable from any line of code back to its requirement and its design rationale.

## Skills and competencies inventory

- **Methodology / process engineering:** spec-driven development, adversarial design review, docs-as-code,
  acceptance-criteria-driven delivery, right-sized process design.
- **AI agent orchestration:** multi-agent build pipelines, worktree-isolated parallel builders, agent role
  design, automated (agent/bot) code review, structured-output agent workflows.
- **Release engineering / DevOps:** layered CI gating, trunk-based delivery, staging/production promotion,
  rollback design, GitHub Actions, Infrastructure-as-Code (Bicep / Azure).
- **Full-stack engineering:** React 19 + TypeScript (strict), Material UI, real-time (SignalR/WebSockets),
  ASP.NET Core (.NET), REST + hub APIs, PWA.
- **Product and safety:** child-safety/privacy-by-design, entitlement/billing design (Stripe), anonymous
  identity models, observability and cost governance for AI features.
- **Leadership traits (evidenced):** intellectual honesty about what is and is not proven; adversarial
  self-review; building auditable, hand-off-safe systems.

## One-line variants (for a headline, a bio, or a LinkedIn "about")

- "I design development processes that let AI agents build coherent software at speed - proven from
  empty repo to shippable alpha in under 20 solo hours, and hardened by adversarial review."
- "I make AI-assisted software delivery fast *and* trustworthy: challenge the design before the code,
  gate every change, and stay honest about what is proven."

---

## Provenance (for your own reference - not for the customer copy)

Everything above traces to artifacts in this repository: the QuibbleStone build (git history, 126 merged
PRs, June 30 to July 10 2026), the methodology and its evidence ledger in
[`methodology/`](methodology/), and the two adversarial process reviews recorded in the
[`README.md`](README.md) revision log. Keep the claims conservative and swap in your brand; do not add
numbers this evidence does not support.
