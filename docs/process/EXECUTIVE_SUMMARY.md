<!--
  Executive summary of the development process - the short "what / why / benefits" pitch for a reader
  who has 3 minutes: a peer engineer deciding whether to adopt it, or a stakeholder who wants to
  understand how the work gets done. The full process is DEVELOPMENT_PROCESS.md.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

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
- **It is portable.** Nothing in the method is QuibbleStone-specific; the artifacts and checkpoints
  drop into any repository (see the adoption guide).

## The proof

QuibbleStone is a **solo, roughly ten-hours-a-week build**. Using this process it shipped a full
alpha: real-time rooms with roster and host migration, four game modes on a single shared engine,
a metered AI cost gate, anonymous-player accounts with billing, an operator console, and a
two-lane (qa/beta) deploy pipeline - hundreds of passing tests, a green trunk, and a child-safety and
anonymity posture that held across every feature. It did that without losing momentum, because the
process front-loads the thinking and automates the building.

## The one-line version

> **Challenge the idea before you build it, make the spec the contract, let parallel agents build to
> it under three gates, and verify with a human at every wave - all as versioned text in the repo.**

For the full method see [`DEVELOPMENT_PROCESS.md`](DEVELOPMENT_PROCESS.md); for how it grew see
[`PROCESS_EVOLUTION.md`](PROCESS_EVOLUTION.md); to adopt it see [`ADOPTION_GUIDE.md`](ADOPTION_GUIDE.md).
