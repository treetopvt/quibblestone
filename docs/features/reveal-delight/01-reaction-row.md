# Story: Reaction row

**Feature:** Reveal Delight  ·  **Status:** Complete  ·  **Issue:** #56

## Context
The design pack's Reveal screen spec (`docs/design/README.md` Screens screen 6)
includes a reaction row above the pinned action bar: pill buttons, each showing
an icon and a live count, that let the room say "that was funny" without typing
anything. `the-reveal/feature.md` parked this explicitly for Phase 4; this story
pulled it into its own decomposed slot in `reveal-delight` now that the look-ahead
pass has a home for it. It is the lightest-weight, highest-warmth addition to the
payoff moment (README section 10). See [feature.md](./feature.md).

**Revised 2026-07-03 (screen de-clutter):** a product-owner decision made during
the fit-to-viewport de-clutter pass (`design-system/05`) narrowed and reworked
this row. What ships now:
- **Three pills, not four:** Love (teal, thumbs-up), Wow (gold, face-surprise),
  Didn't like (coral, thumbs-down) - replacing the old four (Laugh, Heart, Wow,
  Star). Internal type ids: `love` / `wow` / `nope`.
- **One reaction per user, switchable, not a free-for-all tap counter.** This
  REVERSES the story's original Out of Scope line ("a per-player already-reacted
  de-dupe guard is out of scope"). Tapping a pill you hold none of SELECTS it
  (+1); tapping a different pill MOVES your reaction (-old, +new); tapping the
  pill you currently hold TOGGLES it off (-1). A count can never go negative.
- **Server-authoritative dedupe for group play**: `Room.SetReaction(connectionId,
  reactionType)` holds at most one reaction per connection, guarded by the room
  lock. Reset per round in `StartRound`; cleared when a player leaves
  (`ClearReactionLocked`, wired into `RemovePlayer` and `TryReleaseSeat`).
- The wire DTO is `ReactionCountsDto(int Love, int Wow, int Nope)`; the hub's
  `KnownReactions` allow-list is `{ love, wow, nope }`.
- Solo play mirrors the same select/move/toggle arithmetic locally (no hub round
  trip) - one function, not two parallel implementations.
- The connection id remains a server-side handle only, never broadcast - a
  reaction stays a TYPE ENUM with no PII either way (child-safety unchanged).
- The floating-icon animation discipline (transform-only, reduced-motion-gated
  float, the count/selection change always firing) is unchanged.
- Golden Guardian funniest-word voting (`reveal-delight/03`) is UNCHANGED by
  this revision - still a separate, single-winner mechanic.

The ACs, Out of Scope, and Technical Notes below have been updated in place to
match what shipped; this file remains the one canonical record for this story
(no superseding story was created - the change is a revision to the same
mechanic, not a new one).

## Acceptance Criteria
- [x] AC-01: Given the Reveal screen, then a reaction row renders above the
      pinned bottom action bar: three equal pill buttons - Love (teal,
      thumbs-up icon + count), Wow (gold, face-surprise icon + count), Didn't
      like (coral, thumbs-down icon + count) - each showing its icon and
      current count in Fredoka 600 16px.
- [x] AC-02: Given I tap a reaction pill, when the tap registers, then the
      selection/count change always applies (see AC-04a for the exact
      select/move/toggle rule) and a floating copy of its icon rises
      approximately 62px and fades over ~1.1s (`@keyframes`, ease-out), then is
      removed from the DOM - matching the design pack's documented Reaction
      float timing (rise 62px + fade + scale 1.25, 1.1s ease-out).
- [x] AC-03: Given the floating-icon entrance/pop animation for a reaction tap,
      then it drives ONLY `transform` (translateY + scale) - it never animates
      `opacity` as part of a keyframe on a reused/re-rendered element (the
      design pack's own documented gotcha: an opacity-based keyframe with
      `fill-mode: both` can leave a re-rendered list item stuck invisible).
      Opacity fade-out, if used at all, is applied via a plain CSS transition
      on an element that is removed afterward, not a `@keyframes` step that
      could re-fire on re-render.
- [x] AC-04: Given I am in a group room, when any player (including me) taps a
      reaction, then every player in the room sees the updated tally in
      near-real-time, over the SAME one SignalR connection the roster and
      reveal already use (`web/src/signalr/useGameHub.ts`) - no second
      connection, no polling.
- [x] AC-04a (one reaction per user, switchable - revised 2026-07-03): Given I
      hold no reaction, when I tap a pill, then that pill's count increments by
      one and I now hold it (SELECT). Given I hold a reaction, when I tap a
      DIFFERENT pill, then my old pill's count decrements by one, the new
      pill's count increments by one, and I now hold the new one (MOVE). Given
      I hold a reaction, when I tap the SAME pill again, then its count
      decrements by one and I hold no reaction (TOGGLE OFF). In every case a
      count never goes negative. In group play this is enforced
      SERVER-AUTHORITATIVELY (`Room.SetReaction`, keyed by connection id, reset
      on `StartRound`, cleared on leave); solo mirrors the identical arithmetic
      locally.
- [x] AC-05: Given I am playing solo (no room), then the reaction row still
      renders and taps still apply the same select/move/toggle rule and animate
      locally - reactions are not gated behind group play.
- [x] AC-06: Given the reaction row, then it introduces NO new free-text entry
      point and collects no PII - a reaction is a TYPE ENUM keyed by a
      server-side connection handle only; there is nothing here for the safety
      filter to check because no text is submitted and no identity is
      broadcast.

## Out of Scope
- Reaction types beyond the three named (Love, Wow, Didn't like). The original
  four-reaction set (Laugh, Heart, Wow, Star) is retired, not parked - see the
  Revised note above.
- Reacting to an individual filled-in word rather than the whole tale (that
  concept belongs to `reveal-delight/03`'s Golden Guardian award, which is a
  DIFFERENT mechanic - a single-winner vote, not a multi-option tally; do not
  merge the two).
- Persisting reaction counts across a "Play another round" replay (counts
  reset with the new round's fresh Reveal screen, matching that screen's
  overall ephemeral, mutable state - README section 4, this is a toy, not a
  system of record).
- A history of who reacted with what, or any cross-round reaction tally - the
  per-connection hold exists only to enforce one-active-reaction-per-user for
  the CURRENT reveal; it is discarded, not archived.

## Technical Notes
- Lives inside `web/src/components/ReactionRow.tsx`, composed into
  `web/src/pages/Reveal.tsx` above the pinned bottom action cluster. The
  component is a CONTROLLED single-select: the caller (solo state, or the
  group hub mirror) owns `counts` and `selected` and passes them in; the row
  itself only renders them and calls `onReact(type)` on a tap - it never
  tracks who holds what. This keeps `Reveal.tsx` room-agnostic, matching its
  other slot-based contracts (`attribution`, `taleFeedback`, `goldenGuardian`).
- The three pills, in order: Love - teal, `thumbs-up`; Wow - gold,
  `face-surprise`; Didn't like - coral, `thumbs-down`. A selected pill fills
  solid with its theme color (`theme.palette[color].main`) and shows white
  text/icon; an unselected pill stays `background.paper` with a colored
  outline. All three icons are FontAwesome, already registered in
  `web/src/fontawesome.ts` (Love/Didn't-like reuse the icons registered for
  `TaleFeedback`'s thumbs; Wow's `face-surprise` was added in the same
  de-clutter pass).
- Real-time (AC-04/AC-04a): `GameHub.React(code, reactionType)` validates
  `reactionType` against the server-side `KnownReactions` allow-list
  (`{ love, wow, nope }`, case-insensitive) before calling
  `Room.SetReaction(connectionId, reactionType)`, which holds the
  select/move/toggle state in `_reactionByConnection` (connection id -> held
  type) alongside the tally in `_reactionCounts`, both guarded by the room
  lock. The updated tally broadcasts to the room group as
  `ReactionCountsChanged` with `ReactionCountsDto(Love, Wow, Nope)`. Both
  dictionaries reset together in `StartRound` (a fresh round starts every
  count at zero and forgets every hold) and a leaving connection's hold is
  cleared via `ClearReactionLocked`, composed into both `RemovePlayer` and
  `TryReleaseSeat` so a departed player's reaction never lingers in the tally.
- Solo play (AC-05) applies the identical select/move/toggle arithmetic to
  local component state (no hub invoke) - the SAME logic shape as the server's,
  so there are not two parallel reaction-counting implementations, just two
  callers of the same rule.
- Animation discipline carries over unchanged from the original build: the
  floating-icon rise+scale uses a `transform`-ONLY `@keyframes`
  (`translateY(-62px) scale(1.25)`), and the fade-out is a plain CSS
  `transition` on the soon-removed `FloatingIcon` element, never an opacity
  `@keyframes` step (AC-03's documented footgun). Only the float is gated
  behind `prefers-reduced-motion`; the tap's select/move/toggle effect always
  applies regardless of motion preference.
- Colors/pills pull from theme tokens only (`teal.main`, `gold.main`,
  `coral.main`, `background.paper`) - no hardcoded hex, matching the rest of
  `Reveal.tsx`.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: Reveal screen shows 3 pills (Love/Wow/Didn't like) with icon + count |
| AC-02 | manual: tapping a pill applies the select/move/toggle rule and shows a rising/fading floating icon |
| AC-03 | code review + manual: floating-icon CSS uses `transform` keyframes only; no `opacity` `@keyframes` step on a reused element |
| AC-04 | manual (two browser contexts): a tap in one context updates the tally in the other without a refresh |
| AC-04a | `tests/QuibbleStone.Api.Tests/RoomReactionTests.cs` (server select/move/toggle/never-negative + reset-on-round + clear-on-leave) + manual two-context check that only one pill stays highlighted per player |
| AC-05 | manual: solo Reveal (no room) still shows working, animating reaction pills with the same select/move/toggle rule |
| AC-06 | code review: no new text input, no player-identifying field on the reaction payload (`ReactionCountsDto` carries only three counts) |

## Dependencies
- the-reveal/01-text-reveal
- session-engine/03-player-roster (the room this story broadcasts within)
- design-system/01-mui-theme-and-app-shell
- design-system/05-fit-to-viewport-declutter (the 2026-07-03 revision that
  narrowed the set to three and added the one-reaction-per-user rule)
