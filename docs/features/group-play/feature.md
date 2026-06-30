# Feature: Group Play Experience

## Summary
The differentiator: multiple people, multiple devices, one shared story. A host
starts a round, blanks are distributed among players, words are collected, and
the room sees the reveal together. Slice 1 targets a 2-player group to prove
real-time sync early.

## README reference
README section 1 (group play is "the differentiator") and section 7 (Epic Map -
Phase 1, Group Play Experience). Slice 1: section 8 ("a 2-player group - proving
real-time sync early de-risks the scariest part").

## Stories
- [ ] 01 - Host starts a round
- [ ] 02 - Distribute blanks among players
- [ ] 03 - Collect words and ready the reveal
- [ ] 04 - Round complete and replay loop

## Dependencies
- session-engine (room + roster)
- game-modes (Classic blind)
- template-model
- the-reveal
- child-safety
- design-system (Guardian component, theme)

## Design notes
- Everything runs over the one SignalR connection: round start, blank
  assignments, submissions, and the reveal broadcast are hub messages; clients
  update room state from them.
- Slice 1 explicitly targets 2 players. The scariest risk is real-time sync
  across devices, so build the smallest real version of it early.
- Reuses the engine (game-modes) - group play is the engine plus distribution
  and a shared, broadcast reveal. Reconnect tolerance for the "car dead zone"
  is a later hardening pass, not Slice 1.
- The Round Complete screen (story 04) closes the replay loop: "Play another
  round" re-uses the same room (no re-join), "Back to lobby" restores the
  lobby state. The server tracks the round number and per-player word-count
  attribution for the crew recap.
- The Waiting interstitial (story 03) is intentionally calm and has no
  countdown - "no rush, the stone waits for everyone." This is a product
  stance, not a technical gap.

## Parked - Phase 2+
- Host controls to kick a player, skip a slow writer, or override a player who
  never submits (design pack Expansion 6 and 8).
- Capacity beyond 6 players.
