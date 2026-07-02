# Story: Collect words and ready the reveal

**Feature:** Group Play Experience  ·  **Status:** Complete

## Context
Players submit their assigned words; the host watches progress; once everything is
in, the room is ready for the shared reveal. This closes the real-time loop that
makes group play work. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given I have been assigned a blank, when I submit a word, then it is
      accepted only after passing the safety filter and is recorded for the round.
      Submitted words are never shown to other players before the reveal.
- [x] AC-02: Given I have submitted my last assigned word and other players are
      still writing, then I see the Waiting interstitial screen: app bar "Your
      words are in!", the hero mascot juggling letter tiles "W O W", and the
      caption "Juggling letters while the others carve...". See
      `docs/design/README.md` Screens - screen 5 (Waiting) and
      `docs/design/screens/05-waiting.png`.
- [x] AC-03: Given the Waiting screen, then a status card shows "[N] of [M]
      quibblers done" (teal check-circle icon) and "X still writing". A row of
      [M] Guardian avatars (54px circles) shows: done players at full opacity
      with a teal check badge; still-writing players dimmed (`opacity:0.55`,
      muted name) with a pulsing sandstone badge. There is no countdown. See
      `docs/design/README.md` Screens screen 5.
- [x] AC-04: Given I am on the Waiting screen, then I see a secondary outlined-
      purple "Review my words" button (chisel icon); tapping it takes me back to
      a read-only view of my submitted words. There is no gold CTA on this screen
      (it is intentionally passive). See `docs/design/README.md` Screens screen 5
      and Buttons note "A purely passive screen legitimately has no gold CTA".
- [x] AC-05: Given all assigned blanks have been submitted by all players, then
      the room transitions to the reveal in near-real-time for everyone; I do not
      need to refresh.
- [x] AC-06: Given any submitted word, then it passed the safety filter before
      being recorded or appearing in the waiting status or reveal.

## Out of Scope
- Editing a submitted word after sending.
- Progressive reveal (a later mode).
- A host control to skip a player who never submits (timeout/override is Phase
  2, parked in group-play/feature.md).
- Waiting screen animations (hero mascot juggling motion arc, foot tap) - the
  static pose is in scope; the animation is a delight-tier pass.
- Reconnect handling.

## Technical Notes
- Submissions are hub messages bound to the round + the player's assigned blank;
  the server validates (safety filter) and tracks completion, broadcasting
  progress ("N of M done") to all players in the room.
- The Waiting screen's progress row uses `<Guardian variant size={54} />` for
  each player tile. Done players show a teal check badge overlay; writing players
  are rendered at `opacity:0.55`.
- "Review my words" is a client-side read-only view of the already-submitted
  answers (no server round-trip needed; the client already holds them).
- When the server determines all blanks are submitted, it broadcasts the reveal
  transition to the room group (the-reveal/01 receives it).

## Dependencies
- group-play/02-distribute-blanks
- the-reveal/01-text-reveal
- child-safety/01-profanity-filter
- design-system/02-guardian-component
