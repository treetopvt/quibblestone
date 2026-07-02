# Story: Word-by-word "carving" reveal animation

**Feature:** Reveal Delight  ·  **Status:** Complete  ·  **Issue:** #57

## Context
`the-reveal/feature.md` parked this explicitly for Phase 3: "each word fades/
scales in sequentially as the stone 'carves'." Slice 1's Reveal screen shows
the complete assembled story instantly; this story adds the theatrical beat
where the coral filled-in words visibly "carve" into the tablet one after
another, matching the storybook-fantasy motif (README section 10's "the payoff
moment ... deserves the most love"). It is purely an entrance-animation layer
on top of the existing, already-assembled story - it changes nothing about
WHAT is rendered (`buildRevealParts` / `assemble()` stay untouched), only the
ORDER and TIMING in which the already-known result becomes visible. See
[feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given the Reveal screen loads with a complete assembled story,
      then the literal template text appears immediately (it needs no
      "carving" - it was never hidden), while each coral filled-in word pops in
      SEQUENTIALLY, in body order, with a short stagger between words (e.g.
      ~120-200ms per word), rather than all filled words appearing at once.
- [ ] AC-02: Given a filled word's entrance, then it is driven by `transform:
      scale` (a quick scale-up from a smaller starting scale to 1, ease-out)
      ONLY - it never animates `opacity` as part of a `@keyframes` step, per
      the design pack's documented gotcha (an opacity keyframe with
      `fill-mode: both` can leave a re-rendered list item stuck invisible,
      which would be especially damaging here since the WHOLE story would look
      broken/half-missing).
- [ ] AC-03: Given `prefers-reduced-motion: reduce` is set, then the carving
      animation is skipped entirely - the complete story (literal text +
      every filled word) renders immediately at full scale, matching Slice
      1's current instant-reveal behavior, so a reduced-motion player never
      waits on an animation to read their story.
- [ ] AC-04: Given the carving animation is playing, then it never blocks or
      delays the rest of the screen - the celebratory header, confetti, the
      pinned action bar ("Play another round" / "Share the tale"), and (once
      `reveal-delight/01` lands) the reaction row are all interactive
      immediately; a player can tap "Play another round" mid-carve without
      waiting for the last word to pop in.
- [ ] AC-05: Given I am in a group room and the reveal is shared (the room
      transitions to Reveal together, per `group-play/03`'s broadcast), then
      every player's client independently plays the SAME carving sequence
      against the SAME assembled story they all received - the animation is
      purely a local, client-side presentation layer over data every client
      already has; it introduces NO new hub message (the story text/attribution
      itself is unchanged, only its entrance timing).
- [ ] AC-06: Given the carving animation completes (or is skipped per AC-03),
      then the final rendered story is pixel-identical to today's instant
      reveal - same coral highlight styling (`color: theme.palette.coral.main`,
      weight 800, coral underline), same text, same layout. This story adds
      motion, not a new visual treatment.

## Out of Scope
- Any sound effect accompanying each word "carving" in (audio is Phase 3
  territory per `the-reveal/feature.md`'s own TTS parking note - unrelated to
  this story, which stays silent).
- Animating the LITERAL (non-blank) template text - only player-filled coral
  words carve in sequentially; surrounding story text is always instantly
  visible (AC-01).
- A skip/replay control for the carving animation beyond the reduced-motion
  bypass (AC-03) - a player who wants to skip ahead can already scroll/tap
  the pinned actions per AC-04; no dedicated "skip animation" button is added.
- Changing `assemble()`'s or `buildRevealParts()`'s output shape - this story
  is presentation-only over their existing, unmodified output.

## Technical Notes
- Lives entirely inside `web/src/pages/Reveal.tsx`'s existing story-scroll
  rendering (the `parts.map(...)` block that already walks `buildRevealParts`
  output) - add a per-word entrance animation keyed by each word's index in
  `parts`, staggered via a CSS `animation-delay` computed from that index (no
  JS-driven interval/timeout needed for the stagger itself - pure CSS keeps it
  simple and avoids re-render churn).
- A `keyframes` block alongside the file's existing `tabletGlow` / `twinkle` /
  `confettiFall` definitions (same MUI `keyframes` import already in use):
  something like `scale(.4) -> scale(1)` over ~0.35-0.45s ease-out, with each
  word's `animation-delay` = `index * <stagger>ms` (AC-01, AC-02). Reuse the
  EXISTING coral `sx` styling block for the word `<Box>` unchanged (AC-06) -
  only add the `animation` property to it.
- Reduced motion (AC-03): use CSS `@media (prefers-reduced-motion: reduce)`
  to strip the `animation` property (set it to `none`) rather than a JS
  media-query listener - keeps it declarative and matches how the rest of the
  theme has no existing JS-driven reduced-motion branching to be consistent
  with (if a shared reduced-motion utility/hook exists by build time, prefer
  it; otherwise a plain CSS media query in the `sx`/keyframes is sufficient
  for this story).
- The animation is **purely client-local** (AC-05) - it needs no new SignalR
  message and does not touch `GameHub.cs`. Every client already receives the
  identical assembled story via the existing reveal broadcast
  (`group-play/03`); this story only changes how each client's OWN render of
  that already-identical data enters the screen.
- Coordinate with `reveal-delight/01` (Reaction row) if built in the same
  wave: both stories touch `Reveal.tsx`'s render body, though in different
  regions (this story: the story-scroll `parts.map`; 01: a new region above
  `BottomActionBar`) - verify no overlapping edit before running them
  concurrently, per this feature's own Wave Plan sizing rule.
- Every color/spacing token from `web/src/theme.ts` (no new hardcoded hex);
  no new icons needed for this story.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: filled words visibly pop in one after another, staggered, in body order; literal text is present from the start |
| AC-02 | code review + manual: entrance keyframes animate only `transform`; no `opacity` keyframe step |
| AC-03 | manual (OS/browser reduced-motion setting on): full story renders immediately, no staggered entrance |
| AC-04 | manual: tapping "Play another round" or a reaction pill mid-animation works immediately, no blocking |
| AC-05 | manual (two browser contexts): both clients play the carving sequence independently against the same shared story |
| AC-06 | manual/visual diff: post-animation (or reduced-motion) rendered story matches today's instant-reveal styling exactly |

## Dependencies
- the-reveal/01-text-reveal
- design-system/01-mui-theme-and-app-shell
