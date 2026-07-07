# Story: Distribute blanks among players

**Feature:** Group Play Experience  ·  **Status:** Complete

## Context
In a group, the blanks of the template are shared out among the players so
everyone contributes. Each player is asked for their blank(s) blind, Classic-style.
See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given a round has started with N players and a template with M blanks,
      then the blanks are distributed **round-robin** (deal blanks out in player
      order, wrapping around) so the work is shared: every blank assigned exactly
      once, everyone contributes, and per-player counts differ by at most one (e.g.
      8 blanks / 5 players -> 2/2/2/1/1). Round-robin (not chunked) spreads each
      player's words across the story, which reads funnier on reveal.
- [x] AC-02: Given distribution, then each player is told which blank(s) they owe,
      by prompt only ("give me a plural noun") - no story context (Classic blind).
- [x] AC-03: Given the Slice-1 target of 2 players, then distribution works for 2
      players and a typical template.
- [x] AC-04: Given more blanks than players (or fewer), then every blank is
      assigned exactly once and no blank is left unassigned.

## Out of Scope
- Letting players choose which blank they fill.
- Re-assignment when a player disconnects (reconnect handling deferred).
- Weighted/skill-based allocation or letting the host tune the split - round-robin
  is the fixed Slice-1 rule.

## Technical Notes
- Distribution is pure logic over (players, blanks) - unit-test it (Vitest).
- The server assigns and tells each client (via the hub) only its own prompts.

## Dependencies
- group-play/01-start-round
- game-modes/01-mode-interface
- session-engine/03-player-roster
