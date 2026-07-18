<!--
  A ready-to-paste intro prompt for sharing PROCESS_PACKAGE.md with another AI session (or a human
  reviewer) to adversarially critique, extend, or modify the development process. Copy the block below
  the line and paste it into a fresh session together with PROCESS_PACKAGE.md.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Intro prompt: review the development process

Paste everything below the line into a fresh session, and attach (or paste)
[`PROCESS_PACKAGE.md`](PROCESS_PACKAGE.md) with it. It casts the receiving session as an adversarial
reviewer and asks for concrete add/refute/modify feedback in a structured format you can act on.

If you are running this against a specific project (cadence, pulse, a cobra prototype, a new repo),
add one sentence naming that project and its stack so the reviewer can judge portability against a real
target.

---

You are a senior engineer and process critic. Attached is `PROCESS_PACKAGE.md`: a **proposed software
development process built around AI coding agents**, proven on one real project (a solo, ~10 hrs/week
build called QuibbleStone that shipped a full alpha - roughly 126 merged pull requests - in about 8 to
11 calendar days). I am pressure-testing this process before adopting it more widely across other
projects. **Your job is to make it better by trying to break it**, not to praise it.

Read the whole package first. Then critique it hard. I want you to **add, refute, and modify** - treat
every claim as a hypothesis, not a given.

Apply these lenses, and be specific to the text (cite the stage, section, or claim you are reacting to):

1. **Completeness.** What stage, artifact, role, or checkpoint is missing? Where does an idea fall
   through a crack between stages? What happens to bug fixes, incidents, hotfixes, tech-debt, and
   spikes that are not "large planned features"?
2. **Failure modes.** Where does this break when the assumptions change: a **team of 5+** instead of a
   solo dev; a **large legacy codebase** instead of a greenfield repo; a **slow or flaky CI**; a stack
   with **no clean file-disjointness** (a monolith where everything touches everything); agents that
   **hallucinate or silently drift** from the spec?
3. **Cost and overhead.** Where is the process too heavy for the value? Which artifacts would a real
   team stop maintaining within a month? Is the "separate planning session + merged planning PR"
   checkpoint worth its latency on smaller work?
4. **Evidence.** Which claims are asserted but unsupported? The headline compression number is
   calendar time, not logged effort-hours - does that undercut any argument? What would you want
   measured to actually trust this process over a conventional one?
5. **Portability.** What is genuinely general versus what is an artifact of this specific project
   (single repo, hand-kept API contract, in-memory state, one solo owner who is also the reviewer)?
   What breaks when the "owner" and the "reviewer" are different people?
6. **Alternatives and prior art.** Where does established practice (trunk-based development, RFCs/design
   docs, spec-driven development, stacked diffs, feature flags, the Toyota "andon cord", pre-mortems)
   already do part of this better, and what should be borrowed or swapped in?

Rules of engagement:
- **Distinguish a real gap from a deliberate divergence.** If the process consciously skips something
  (it says so), argue whether that choice is right - do not just flag the absence.
- **Propose concrete changes**, not vague concerns. "Add an X artifact at stage N that captures Y"
  beats "needs more rigor."
- **Rank by impact.** I care most about the few changes that would most improve outcomes.
- Assume I will push back on weak points, so only raise findings you would defend.

Return your review in exactly this structure:

- **Verdict (one line):** adopt as-is / adopt with changes / needs rework - and the single biggest
  reason.
- **What is genuinely strong (max 5 bullets):** only the parts you would keep unchanged.
- **Findings (numbered, ranked most-impactful first).** For each: `Target` (the stage/section/claim),
  `Critique` (what is wrong, missing, or overclaimed), `Severity` (blocker / major / minor),
  `Proposed change` (a concrete edit or addition).
- **What is missing entirely:** stages, artifacts, roles, or failure-handling the process does not
  address at all.
- **If adopting on a real project:** the 3 highest-value changes to make first, and anything that must
  be adapted for that project's team size, stack, or risk profile.

Be direct and concrete. I would rather hear the process is half-baked with three specific fixes than
hear it is great.
