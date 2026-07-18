<!--
  Pulse-specific kickoff for the cross-project alignment review. A variant of REVIEW_PROMPT.md aimed at
  vetting the development process against the pulse repo - deliberately chosen because pulse's likely
  conditions (a company/team project, older and more coupled, real CI) are the ones QuibbleStone's
  solo/greenfield proof did NOT cover, so it is the natural stress test for the cadence-review findings.
  Run this INSIDE a session that has the pulse repo checked out, with PROCESS_PACKAGE.md attached.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Pulse review kickoff

The `cadence` review hardened the process (11 findings, all folded in). Pulse is the second data point,
and the more important one: it is a **dynamisinc project, not a solo personal build**, so it likely
breaks the exact conditions QuibbleStone's proof relied on (solo, greenfield, one architectural bet,
fast CI). That is what makes it worth running.

**How to run it:**

1. Open a session **inside the pulse repository** (so the reviewer can inspect pulse's real artifacts,
   not guess them).
2. Attach [`PROCESS_PACKAGE.md`](PROCESS_PACKAGE.md).
3. Paste everything below the line.
4. Feed the resulting report back here - each confirmed finding, and any new pulse-only finding, becomes
   the next revision entry in [`README.md`](README.md).

If you would rather I pre-fill pulse's actual stack instead of having the reviewer self-characterize it,
add the pulse repo to a session and say so, and I will produce a version with pulse's real facts baked in.

---

You are a senior engineer on the **pulse** project and a process critic. You have the pulse repository
in front of you. Attached is `PROCESS_PACKAGE.md`: a development process built around AI coding agents,
proven on ONE project (QuibbleStone - a solo, greenfield, ~10 hrs/week build, ~15-20 labor-hours to a
full alpha). It has already been hardened by an adversarial review from the sibling `cadence` repo. I
am now vetting it against **pulse**, whose conditions are deliberately different, before adopting it
more widely. Your job is to test whether this process survives contact with pulse - make it better by
trying to break it against a real, different target, not to praise it.

**Step 0 - Characterize pulse honestly, from the actual repo, before reading the package's claims.**
Fill in the five confounds the process itself names as load-bearing, and cite evidence from pulse:

- **Team size:** solo or a team? (`git shortlog -sne`, distinct authors in recent history, PR review
  patterns.) Therefore, is "owner = author = reviewer = verifier" true here, or are those different
  people with real handoff and review latency?
- **Codebase age / coupling:** greenfield or established? Do changes own disjoint files, or does most
  work route through shared hotspots (a central module, a DI container, a schema, a shared client)?
  Find the God-files; run a quick import/dependency scan. Would "size waves by file-disjointness" fan
  wide here, or collapse to near-serial?
- **One architectural bet:** is there a single load-bearing design idea designs can be measured
  against, or several competing concerns?
- **CI:** fast and local-mirrorable, or slow / remote-only? Read the CI config and note the wall-clock.
  Does the three-gate model stay cheap here?
- **Existing process artifacts:** what does pulse ALREADY have - a charter (README / agent guide), ADRs
  or design docs, docs-as-code stories, a tracker mirror, any orchestration - and what is absent?

State the **top 3 ways pulse differs from QuibbleStone's proof conditions.** Those differences are the
test.

**Step 1 - Run the alignment checklist** (Part IV of the package) against pulse's real artifacts. Mark
each row **Aligned / Partial / Gap / N-A-by-design**, each with the citing artifact (or its absence).

**Step 2 - Adversarially critique the process from pulse's vantage.** Apply the lenses (completeness,
failure modes, cost/overhead, evidence, portability, prior art), weighted toward where pulse breaks
QuibbleStone's assumptions. In particular, pressure-test whether the process holds:

- with **review latency** (a team, not a solo owner) - does the reviewer-not-author independence rule
  help or just add a bottleneck (finding #2)?
- on a **coupled codebase** - does the coupling model and "create seams before fan-out pays off" hold,
  or does pulse show it is worse than that (finding #3)?
- under **pulse's real CI** - does the slow-CI gate variant (Gate 1 = affected-tests subset) actually
  work here, or is even that too heavy (finding #8)?
- for **incidents and rollback** - does the new Operate stage (5b) match how pulse actually ships and
  recovers, or is it naive about production reality (finding #4)?

Then **confirm or contradict each of the 11 cadence findings** from pulse's evidence: a finding two
projects agree on is weak; one that three confirm is a pattern; one pulse contradicts needs rethinking.

Rules of engagement: distinguish a real gap from a deliberate divergence; propose concrete changes, not
vague concerns; rank by impact; only raise findings you would defend.

Return your review in exactly this structure:

- **Pulse profile:** the five confounds filled in with citations, and the top-3 differences from
  QuibbleStone.
- **Alignment report:** the Part IV checklist marked, with real Gaps (should-align-but-does-not)
  separated from deliberate divergences.
- **Verdict (one line):** ports to pulse as-is / with changes / not without rework - and the single
  biggest reason.
- **Findings (numbered, ranked most-impactful first):** each with `Target` (stage/section/claim),
  `Critique`, `Severity` (blocker / major / minor), `Proposed change`, and a `pulse-specific vs general`
  tag.
- **Cross-project signal:** for each of the 11 cadence findings - CONFIRM / CONTRADICT / N/A with the
  pulse evidence; plus any NEW finding that cadence and QuibbleStone both missed because neither had
  pulse's conditions.
- **If adopting on pulse:** the 3 highest-value changes to make first, and exactly what must be adapted
  for pulse's team size, coupling, and CI.

Be direct and concrete. I most want the findings that only a team / coupled / real-CI project like pulse
could surface - those are the ones that improve the process for every project, not just this one.
