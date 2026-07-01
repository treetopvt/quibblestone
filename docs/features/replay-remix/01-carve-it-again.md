# Story: Carve it again: same-crew, same-template replay

**Feature:** Replay & Remix  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
The Round Complete screen already offers "Play another round" (`group-play/04`),
which starts a fresh round with a new template pick. Sometimes the crew just
loved *that* tale and wants to hear a sillier version of the exact same one
right now - no re-join, no new template hunt. This story adds a second, faster
replay path from Round Complete: same crew, same template, brand-new blanks.
It is the "again!" reflex a kid has half a second after the laugh lands. See
[feature.md](./feature.md) and `docs/features/group-play/04-round-complete.md`.

## Acceptance Criteria
- [ ] AC-01: Given the Round Complete screen, when I am the host, then I see a
      second, lower-emphasis action alongside the existing gold "Play another
      round" and outlined-purple "Back to lobby": an option to replay the exact
      same template just played (e.g. "Carve it again" with the same story
      title shown). It does not outrank the primary "Play another round" CTA -
      it reads as a secondary/tertiary affordance per the existing button
      hierarchy (gold primary, outlined-purple secondary).
- [ ] AC-02: Given I tap "Carve it again", then a new round starts in the same
      room with the same players and the same template id as the round that
      just finished - no re-join, no code re-entry, and no template picker is
      shown.
- [ ] AC-03: Given the same template is replayed, then every blank is
      re-collected fresh (new prompts, new submissions) - the previous round's
      words are not pre-filled or reused; the new round is entirely new
      content on the same skeleton.
- [ ] AC-04: Given "Carve it again" starts a new round, then the round number
      increments (same as any other new round) and all players transition
      together into word collection in near-real-time over the one SignalR
      connection - I do not need to refresh.
- [ ] AC-05: Given I am not the host, then I cannot trigger "Carve it again";
      only the host sees and can use this action, consistent with how
      `group-play/01`'s "Start game" and `group-play/04`'s "Play another round"
      are host-only and server-enforced.
- [ ] AC-06: Given every word submitted in the replayed round, then it passes
      the same server-side safety filter as any other round; no unfiltered
      free text is ever recorded or shown.

## Out of Scope
- A template picker or "choose which past template to replay" (this story
  replays only the template that JUST finished; browsing older tales is
  `keepsake-gallery/03`).
- Persisting the choice of "always replay same template" as a room setting.
- Solo play (single-player's existing "Play again" already does an equivalent
  same-tab replay per `web/src/pages/Solo.tsx`'s `handlePlayAgain`; this story
  is the group-play, multi-device version).
- Any change to the engine's collection or assembly logic - this story is
  purely "call the existing round-start path with a pinned template id."

## Technical Notes
- Extends the **same** `startRound` hub method `group-play/01` and `group-play/04`
  already define, adding an optional pinned-template-id parameter instead of
  the normal random/host-chosen selection. Server-side host check is identical
  to the existing `startRound` authorization (do not add a second
  authorization path).
- Reuses `web/src/pages/RoundComplete.tsx` (owned by `group-play/04`) - this
  story adds the second action and its handler, it does not rebuild the screen.
  Button hierarchy per `docs/design/README.md` Buttons: the existing gold CTA
  stays "Play another round"; "Carve it again" is the secondary/tertiary
  action (outlined-purple or a lower-weight text action, matching the existing
  Round Complete button stack rather than inventing a fourth button style).
- Reuses `web/src/signalr/useGameHub.ts` - add the pinned-template-id argument
  to the existing round-start invoke rather than a new hub method name, so
  the wire contract for "start a round" stays one shape.
- The template id to pin is whatever `RoundComplete` already displays (the
  just-finished round's template, per `group-play/04`'s keepsake panel title).
- No engine change: this is entirely "which template gets passed to the
  existing round-start flow," never a change to `collectWord`, `assembleStory`,
  or the mode config.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: two browser contexts on Round Complete, host sees both actions with correct visual hierarchy |
| AC-02 | manual: host taps "Carve it again", both browser contexts land in word collection with no re-join prompt |
| AC-03 | manual: compare submitted words round 1 vs round 2 - all fresh, none pre-filled |
| AC-04 | manual: two browser contexts confirm simultaneous transition and an incremented round-number badge with no refresh |
| AC-05 | manual: non-host browser context does not see the "Carve it again" action; a direct hub invoke attempt from a non-host connection is rejected server-side |
| AC-06 | manual: submit a filtered word during the replayed round, confirm rejection with the same friendly message as a normal round |

## Dependencies
- group-play/01-start-round
- group-play/04-round-complete
- session-engine/03-player-roster
