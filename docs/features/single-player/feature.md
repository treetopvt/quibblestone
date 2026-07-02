# Feature: Single-Player Experience

## Summary
Solo play: the no-friction entry point ("I'm bored in line") and the funnel into
group play. Pick a template, fill the blanks (Classic blind), laugh at the
reveal - no room, no account.

## README reference
README section 1 ("Single-player and group play are both first-class. Solo is the
no-friction entry point ... and the funnel into group play") and section 7
(Epic Map - Phase 1, Single-Player Experience). Slice 1: section 8.

## Stories
- [x] 01 - Solo play (Classic blind end to end)
- [ ] 02 - Solo mode picker (choose a mode, play it) - wires game-modes/03-06's
  modes into the solo flow; solo only (group mode selection is separate)

## Dependencies
- template-model
- game-modes (Classic blind)
- the-reveal
- child-safety.

## Design notes
- Solo reuses the same engine as group play (game-modes), just with one filler
  and no room. Proving the engine works solo first de-risks group play.
- Zero friction is the whole point: no code to enter, no account, instant start,
  easy to replay. Keep the path to "first laugh" as short as possible.
