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
- [x] 01 - Host starts a round
- [x] 02 - Distribute blanks among players
- [x] 03 - Collect words and ready the reveal
- [x] 04 - Round complete and replay loop
- [x] 05 - Group mode selection (the host picks the mode for the room)

## Dependencies
- session-engine (room + roster)
- game-modes (Classic blind for Slice 1; story 05 wires the other built modes -
  Word Bank, Progressive Reveal - into group play via the shared mode registry)
- single-player/02 (the solo mode picker + registry that story 05 generalizes)
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

## Design notes (mode selection, story 05)
- Slice 1 hardcoded Classic Blind (`GameHub.StartRound` pins `Mode = "classic-blind"`;
  `GroupRound` renders `FillBlank` with no mode surfaces). Solo shipped the picker +
  the `SOLO_MODES` registry first (`single-player/02`); story 05 generalizes that
  registry so the HOST picks the mode for the whole room. The seam already exists:
  `RoundStartedDto` carries a `Mode` field - it is just pinned today.
- Three of the four modes port to group as pure WIRING (no new real-time surface):
  Classic Blind, Word Bank (swap the answer surface), and Progressive Reveal (pace the
  already-broadcast reveal client-side). **Progressive Story is the exception** - its
  "story so far" must reflect other players' in-progress fills, which needs a live
  partial-fill broadcast (a new real-time surface). Story 05 deliberately does NOT offer
  Progressive Story in the group picker; it is deferred to its own story rather than
  shipped half-working. See story 05 AC-04/AC-05.

## Parked - Phase 2+
- Progressive Story in a group (needs a live cross-player "story so far" broadcast -
  see story 05's deferral). A follow-up story adds that real-time surface, then offers
  the mode in the group picker.
- Host controls to kick a player, skip a slow writer, or override a player who
  never submits (design pack Expansion 6 and 8).
- Capacity beyond 6 players.
