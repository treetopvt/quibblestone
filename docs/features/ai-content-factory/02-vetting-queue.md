# Story: Vetting / moderation queue

**Feature:** AI Content Factory (back office)  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
This is the non-negotiable gate (README section 6: "a strong moderation
pipeline before any *live* AI generation is exposed to kids" - and here,
before any AI-generated content reaches a player at all, live or not).
Candidates from story 01 must pass through the same safety filter every
player-submitted word passes through, plus a human-in-the-loop review, before
they can ever be published. See [feature.md](./feature.md) and README
section 6.

## Acceptance Criteria
- [ ] AC-01: Given a candidate template from story 01, when it enters the
      queue, then it is automatically checked against the same authoritative
      safety filter (`IContentSafetyFilter`, child-safety/01) every
      free-text player surface uses - not a second, reimplemented filter.
- [ ] AC-02: Given a candidate, then it is tagged with an age/theme
      classification and a family-safe flag before a reviewer sees it, so the
      reviewer has context, and so an approved candidate is immediately
      usable by the family-safe toggle and content-tag consumers.
- [ ] AC-03: Given a reviewer looking at a candidate, then they can reject,
      edit, or approve it; a rejected candidate is never published and is not
      re-shown to a reviewer without being re-generated.
- [ ] AC-04: Given a candidate has NOT been explicitly approved by a reviewer,
      then it cannot reach the published library (story 03) or any
      player-facing route - passing the automated filter (AC-01) alone is
      necessary but not sufficient for publish.
- [ ] AC-05: Given the review surface, then it is admin-only / internal (not
      a themed player screen) - no player-facing route exposes unreviewed
      candidates.
- [ ] AC-06: Given no PII is collected anywhere in this pipeline, then a
      candidate or its review record never includes player data - review
      metadata is limited to the candidate content, its provenance, and the
      reviewer's decision.

## Out of Scope
- Reimplementing profanity/safety matching logic (reuse
  `IContentSafetyFilter` as-is).
- Fully automating away the human approval step (the human-in-the-loop gate
  is deliberate per README section 6, not a placeholder).
- A polished admin UI (a functional internal review surface is enough; visual
  design is not the point of this story).
- Moderation of *live*, on-demand AI generation (README Phase 3 XL, parked -
  this story only ever vets offline batch candidates).
- Multi-reviewer workflows, roles, or audit trails (a toy back-office tool,
  not a system of record - CLAUDE.md preamble).

## Technical Notes
- Project: `api/`. Reuse `IContentSafetyFilter` (child-safety/01) by
  injecting it - do not write a second word-matching implementation. This
  story's job is to add classification (age/theme/family-safe tagging) and
  the human-approval workflow on top of that existing gate.
- Follow the existing `ModerationController` pattern for the REST seam
  (`api/src/Controllers/ContentReviewController.cs`): a thin controller with
  no safety logic of its own, shaping requests/responses around an injected
  `IVettingQueue` service.
- A candidate's age/theme classification can start as a simple heuristic or a
  second, narrower AI call - either is acceptable for this story; the AC only
  requires that the tag exists and is attached before a reviewer sees the
  candidate. Keep the classification step swappable, same discipline as the
  provider client in story 01.
- The reviewer identity for Slice-appropriate scope is "the solo builder" -
  no auth/roles system is required here; a future human-moderator handoff can
  layer on top without changing this story's contract.

## Tests
No test harness is wired up yet for this pipeline's back-office code; note
the intended tests here so they exist once xUnit/`dotnet test` is available.

| AC | Test |
|---|---|
| AC-01 | `api/tests/Content/VettingQueueTests.cs` (planned) - asserts a candidate containing filter-blocked text is flagged; confirms the SAME `IContentSafetyFilter` instance is invoked (mock/spy), not a duplicate implementation |
| AC-02 | manual: inspect a queued candidate for an attached classification (age/theme tags, family-safe flag) before any reviewer action |
| AC-03 | manual: exercise reject / edit / approve against a candidate via `ContentReviewController` and confirm state transitions (rejected candidates do not reappear) |
| AC-04 | manual: attempt to read an approved-pending-only candidate through `IContentLibrary` (story 03) or any content-serving route and confirm it is absent until explicitly approved |
| AC-05 | manual: confirm no route reachable from `web/` (player-facing) can list or read raw candidates |
| AC-06 | manual: inspect a candidate/review record schema for the absence of any player-identifying field |

## Dependencies
- ai-content-factory/01-batch-generation-job (candidates to vet)
- child-safety/01-profanity-filter (`IContentSafetyFilter`, reused not
  reimplemented)
