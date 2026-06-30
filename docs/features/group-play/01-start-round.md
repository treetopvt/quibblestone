# Story: Host starts a round

**Feature:** Group Play Experience  ·  **Status:** Not Started

## Context
With players gathered in the room, the host kicks off a round - picks what to play
and moves everyone into it together. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given I am the host in a room with at least one other player, when I
      start a round, then I choose a template (mode is Classic blind for Slice 1)
      and the round begins for everyone in the room.
- [ ] AC-02: Given a round starts, then all players transition into the word-
      collection state together, in near-real-time.
- [ ] AC-03: Given I am not the host, then I cannot start the round.
- [ ] AC-04: Given the family-safe toggle, then the template choices offered to
      the host respect it.

## Out of Scope
- Mode selection beyond Classic blind (later modes).
- Round timers / settings, multiple concurrent rounds.

## Technical Notes
- Hub method (host-only, server-enforced) that sets the round's template + mode
  and broadcasts the transition to the room group.
- Template list respects family-safe (child-safety/02).

## Dependencies
- session-engine/03-player-roster
- game-modes/02-classic-blind
- template-model/01-template-schema
- child-safety/02-family-safe-toggle
