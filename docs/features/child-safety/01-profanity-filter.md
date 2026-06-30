# Story: Profanity / safety filter on free text

**Feature:** Child Safety & Moderation  ·  **Status:** Not Started

## Context
This is the safety primitive the rest of the game leans on: any free text a
player types must be checked before anyone sees it, so the game is safe to hand
to a kid. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given a player submits free text (a blank answer or a nickname), then
      it is checked by the safety filter before it is stored or shown to anyone.
- [ ] AC-02: Given a submission fails the filter, then it is rejected with a
      friendly, non-shaming message and the player can try again; the failing text
      is never displayed to others.
- [ ] AC-03: Given a submission passes, then it proceeds into the game normally.
- [ ] AC-04: Given the check, then it runs server-side (authoritative) so a client
      cannot bypass it.
- [ ] AC-05: Given multiple free-text entry points exist, then they all call the
      same single filter (it is not reimplemented per surface).

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
