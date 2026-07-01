# Story: Reaction row

**Feature:** Reveal Delight  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
The design pack's Reveal screen spec (`docs/design/README.md` Screens screen 6)
includes a reaction row above the pinned action bar: four pill buttons - Laugh
(gold), Heart (coral), Wow/sparkle (teal), Star (purple) - each showing an icon
and a live count. Tapping increments the count and spawns a floating icon that
rises and fades. `the-reveal/feature.md` parked this explicitly for Phase 4;
this story pulls it into its own decomposed slot in `reveal-delight` now that
the look-ahead pass has a home for it. It is the lightest-weight, highest-warmth
addition to the payoff moment (README section 10) - a way for the room to say
"that was funny" without typing anything. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given the Reveal screen, then a reaction row renders above the
      pinned bottom action bar: four equal pill buttons - Laugh (gold icon +
      count), Heart (coral), Wow (teal, sparkle icon), Star (purple) - each
      showing its icon and current count in Fredoka 600 16px, matching
      `docs/design/README.md` Screens screen 6.
- [ ] AC-02: Given I tap a reaction pill, when the tap registers, then that
      reaction's count increments by exactly one and a floating copy of its
      icon rises approximately 62px and fades over ~1.1s (`@keyframes`, ease-
      out), then is removed from the DOM - matching the design pack's
      documented Reaction float timing (rise 62px + fade + scale 1.25, 1.1s
      ease-out).
- [ ] AC-03: Given the floating-icon entrance/pop animation for a reaction tap,
      then it drives ONLY `transform` (translateY + scale) - it never animates
      `opacity` as part of a keyframe on a reused/re-rendered element (the
      design pack's own documented gotcha: an opacity-based keyframe with
      `fill-mode: both` can leave a re-rendered list item stuck invisible).
      Opacity fade-out, if used at all, is applied via a plain CSS transition
      on an element that is removed afterward, not a `@keyframes` step that
      could re-fire on re-render.
- [ ] AC-04: Given I am in a group room, when any player (including me) taps a
      reaction, then every player in the room sees that reaction's count
      update in near-real-time, over the SAME one SignalR connection the
      roster and reveal already use (`web/src/signalr/useGameHub.ts`) - no
      second connection, no polling.
- [ ] AC-05: Given I am playing solo (no room), then the reaction row still
      renders and taps still increment counts and animate locally - reactions
      are not gated behind group play; a solo player can still tap "Laugh" at
      their own tale.
- [ ] AC-06: Given the reaction row, then it introduces NO new free-text entry
      point and collects no PII - a reaction tap carries no identity beyond
      "someone in this room reacted"; there is nothing here for the safety
      filter to check because no text is submitted.

## Out of Scope
- A per-player "already reacted" de-dupe guard (a player can tap the same
  reaction repeatedly; the design pack's own State Management notes flag this
  as an open design question, parked to Phase 4 in `feature.md`).
- Reaction types beyond the four named (Laugh, Heart, Wow, Star).
- Reacting to an individual filled-in word rather than the whole tale (that
  concept belongs to `reveal-delight/03`'s Golden Guardian award, which is a
  DIFFERENT mechanic - a single-winner vote, not a multi-option tally; do not
  merge the two).
- Persisting reaction counts across a "Play another round" replay (counts
  reset with the new round's fresh Reveal screen, matching that screen's
  overall ephemeral, mutable state - README section 4, this is a toy, not a
  system of record).

## Technical Notes
- Lives inside `web/src/pages/Reveal.tsx` (or a small extracted
  `ReactionRow.tsx` component it composes) - positioned above the existing
  `BottomActionBar` per the design spec's layout, reusing the same
  `BottomActionBarSpacer` reservation pattern already established in that file
  so the reaction row never hides behind the pinned actions.
- Local component state shape mirrors the design pack's documented State
  Management note: `counts: { laugh, heart, wow, star }` plus a `floaters[]`
  array (each entry removed after its ~1100ms animation completes via a
  `setTimeout`/animation-end handler) - see `docs/design/README.md` State
  Management (Reveal) and Interactions & Behavior ("optimistic increment +
  ephemeral floating icon").
- Real-time (AC-04): this is the one genuinely NEW hub surface this story
  needs - a lightweight `ReactAsync(roomCode, reactionType)` invoke and a
  `ReactionCountsChanged`-shaped broadcast, added to `api/src/Hubs/GameHub.cs`
  and `web/src/signalr/useGameHub.ts` alongside the existing `RosterChanged`
  pattern (see that file's header for the established shape: one handler
  registered once, guarded against post-leave/teardown races the same way
  `RosterChanged` already is). Server-side state for reaction counts can live
  on the same in-memory `Room` the `RoomRegistry` already tracks (no DB - this
  is ephemeral session state, README section 4) - increment counters, broadcast
  the updated tally to the room group.
- Solo play (AC-05) has no room/hub - increment local component state only,
  same optimistic-update code path minus the broadcast call. Structure the
  increment logic so it is one function called either way (locally, or as the
  optimistic update alongside a fire-and-forget hub invoke in group play) -
  avoid two parallel reaction-counting implementations.
- Icon colors/pills pull from theme tokens (`gold.main`, `coral.main`,
  `teal.main`, `primary.main` for purple) exactly as `Reveal.tsx`'s existing
  `Confetti`/`NarrationBar` sections already do (`theme.palette[color].main`
  lookup pattern) - no hardcoded hex. Icons are FontAwesome only, registered in
  `web/src/fontawesome.ts` (a laugh/heart/sparkle/star-family icon per pill;
  reuse the existing `star` icon already imported for the celebration header
  where applicable).
- Respect reduced-motion: gate the floating-icon animation (not the tap-to-
  increment itself, which must always work) behind a
  `prefers-reduced-motion` check, consistent with the reduced-motion
  discipline called out in `reveal-delight/02`'s AC for the carving animation
  - keep the two stories' reduced-motion handling consistent if built close
  together.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: Reveal screen shows 4 pills with icon + count, matching the design spec's layout |
| AC-02 | manual: tapping a pill increments its count by one and shows a rising/fading floating icon |
| AC-03 | code review + manual: floating-icon CSS uses `transform` keyframes only; no `opacity` `@keyframes` step on a reused element |
| AC-04 | manual (two browser contexts, mirrors group-play's verification approach): a tap in one context updates the count in the other without a refresh |
| AC-05 | manual: solo Reveal (no room) still shows working, animating reaction pills |
| AC-06 | code review: no new text input, no player-identifying field added to the reaction payload |

## Dependencies
- the-reveal/01-text-reveal
- session-engine/03-player-roster (the room this story broadcasts within)
- design-system/01-mui-theme-and-app-shell
