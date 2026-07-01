# Story: Profanity / safety filter on free text

**Feature:** Child Safety & Moderation  ·  **Status:** Complete

## Context
This is the safety primitive the rest of the game leans on: any free text a
player types must be checked before anyone sees it, so the game is safe to hand
to a kid. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given a player submits free text (a blank answer or a nickname), then
      it is checked by the safety filter before it is stored or shown to anyone.
      `GameHub` calls `_safety.CheckAsync` before storing a nickname (join /
      host-setup paths, `GameHub.cs` lines ~316, ~393) and before recording a
      submitted word (`SubmitWord`, line ~694).
- [x] AC-02: Given a submission fails the filter, then it is rejected with a
      friendly, non-shaming message and the player can try again; the failing text
      is never displayed to others.
      `ContentSafetyFilter.BlockedMessage` is a friendly, non-shaming, kid-
      readable retry prompt; rejected text is never echoed back or stored -
      confirmed by `ContentSafetyFilterTests`.
- [x] AC-03: Given a submission passes, then it proceeds into the game normally.
      Confirmed by `ContentSafetyFilterTests.Allows_clean_and_innocent_substring_words`
      and the hub call sites proceeding past the `IsAllowed` check.
- [x] AC-04: Given the check, then it runs server-side (authoritative) so a client
      cannot bypass it.
      `ContentSafetyFilter` runs in `api/src/Safety/`, invoked from `GameHub`
      (server) and `ModerationController` (server); no client-only path exists.
- [x] AC-05: Given multiple free-text entry points exist, then they all call the
      same single filter (it is not reimplemented per surface).
      `IContentSafetyFilter` is registered once as a DI singleton
      (`Program.cs`) and is the only call site referenced by `GameHub`
      (nickname + word submission) and `ModerationController`.

## Out of Scope
- Moderation of live AI-generated content (Phase 3 - the heaviest burden, last).
- Perfect or fully localized profanity coverage (a solid baseline is enough now).
- Human moderation queues / reporting flows.

## Technical Notes
- Implement as a single service in `api/` that the hub and any REST entry points
  call; expose it so nicknames (session-engine) and blank answers (game-modes,
  group-play, single-player) all route through it.
- Keep the word logic pure where possible so it is unit-testable (Vitest/xUnit).

## Dependencies
None (foundational).

## Tests
- AC-01, AC-02, AC-03: `tests/QuibbleStone.Api.Tests/ContentSafetyFilterTests.cs`
  (clean words, innocent-substring regression cases i.e. the Scunthorpe
  problem, and blocked profanity with the friendly rejection message).
- AC-04, AC-05: `tests/QuibbleStone.Api.Tests/GameHubJoinTests.cs` wires a
  `GameHub` with the real `ContentSafetyFilter` (no mocking) and asserts a
  filter-blocked nickname is rejected end-to-end and never reaches the
  roster. Gap: no equivalent end-to-end test for `SubmitWord` (word-answer
  path) or `ModerationController`, only the filter unit itself is tested
  there.
