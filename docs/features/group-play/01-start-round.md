# Story: Host starts a round

**Feature:** Group Play Experience  ·  **Status:** In Review

## Context
With players gathered in the room, the host kicks off a round - picks what to play
and moves everyone into it together. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given I am the host in a room with at least one other player, when I
      tap the gold "Start game" button on the Lobby, then I choose (or the system
      auto-selects) a template, mode is Classic blind for Slice 1, and the round
      begins for everyone in the room. See `docs/design/README.md` Screens -
      screen 3 (Lobby) and `docs/design/screens/03-lobby.png`.
- [x] AC-02: Given a round starts, then all players transition into the word-
      collection (FillBlank) state together in near-real-time.
- [x] AC-03: Given I am not the host, then I cannot start the round; the "Start
      game" button and host note are not visible to non-host players.
- [x] AC-04: Given the family-safe toggle is on, then the template choices
      offered to the host respect it (only family-safe tagged templates).

## Out of Scope
- Mode selection beyond Classic blind (later modes).
- Round timers, settings dialogs, multiple concurrent rounds.

## Technical Notes
- Hub method (host-only, server-enforced) that sets the round's template + mode
  and broadcasts the transition to the room group. Server checks that the
  requesting connection is the host before executing.
- The "Start game" CTA on the Lobby is the gold primary button; visible only to
  the host. Non-host players see the lobby in a passive "waiting" state.
- Template list respects the family-safe toggle (child-safety/02).

## Dependencies
- session-engine/03-player-roster
- game-modes/02-classic-blind
- template-model/01-template-schema
- child-safety/02-family-safe-toggle
