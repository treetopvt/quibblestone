# Story: Progressive Reveal mode

**Feature:** Game Modes Engine  ·  **Status:** In Progress  ·  **Issue:** #52

## Context
The engine's `reveal` axis (`web/src/engine/mode.ts`) declares `'progressively'`
alongside `'at-end'`, but no mode has ever turned it on. Progressive Reveal is
that mode: players fill every blank BLIND (subject-only, exactly like Classic
blind - no peeking while writing), but at the reveal, the assembled story
unveils one filled word at a time (paced/stepped) instead of showing the whole
tale at once. Where `game-modes/05` (Progressive Story) proves the SEE axis by
showing the story-so-far WHILE filling, this mode proves the REVEAL axis by
pacing HOW the finished story is shown - the two are deliberately kept
distinct so each axis is proven cleanly. It plugs into the presentation slot
that `game-modes/03` adds to `Reveal` - it does NOT touch `Reveal.tsx` itself.
See [feature.md](./feature.md) and README section 4.

## Acceptance Criteria
- [ ] AC-01: Given Progressive Reveal mode, when I am filling blanks, then I
      see the same subject-only stone-tablet prompt card as Classic blind
      (category chip, prompt, sub-hint, blind reassurance panel) - `see:
      'subject-only'` is unchanged from Classic blind, so this mode supplies no
      `seeContext`/`answerSurface` overrides, only a `revealPresentation`.
- [ ] AC-02: Given all blanks are filled and I reach the Reveal screen, then
      the assembled story unveils one filled word at a time in body order
      (paced/stepped reveal), rather than the full coral-highlighted body
      appearing all at once (Classic blind's default) - each word pop uses the
      same coral highlight treatment as the default reveal, just staged over
      time instead of shown immediately.
- [ ] AC-03: Given the progressive reveal is mid-sequence, then the literal
      story text around not-yet-revealed words is still visible (readers see
      the sentence shape forming), but the not-yet-revealed WORDS themselves
      are not shown until their step arrives - once every word has been
      revealed, the screen matches the default reveal's final state exactly.
- [ ] AC-04: Given Progressive Reveal mode, then it is expressed purely as a
      `ModeConfig` (`see: 'subject-only'`, `answer: 'free-text'`, `reveal:
      'progressively'`) plus one `ModeSurfaces` value (`game-modes/03`)
      supplying `revealPresentation` - no new branch is added to `engine.ts`
      or `assemble.ts`; the presentation reuses `buildRevealParts` READ-ONLY
      against the already-complete `AssembledStory` (pacing is a rendering
      concern, not a collection or assembly concern).
- [ ] AC-05: Given free-text Progressive Reveal answers, then every submitted
      word passes the safety filter before it is recorded (the same
      collection-path seam every free-text mode inherits) - the reveal only
      ever paces out words that are already vetted, so no unfiltered word is
      ever shown, staged or otherwise. No PII is collected by this mode.

## Out of Scope
- Any change to the SEE axis while filling (this mode stays subject-only/
  blind, identical to Classic blind, while filling - only the reveal differs).
- Combining this mode with word-bank answering or the progressive-story see
  axis (each axis proven independently in this slice; combining is a
  follow-up).
- Player control over the pacing (skip-ahead, pause, replay a single word) -
  a fixed, non-interactive pace is enough to prove the axis; interactive
  pacing controls are a later delight-tier nicety.
- A room-wide vote on any revealed word, multiple simultaneous "duel" answers
  per blank, or any other Versus-shaped mechanic - parked, see feature.md
  "Parked - Phase 2+/3" (Versus/Duel is a genuine engine stretch, not this
  story's concern).
- The mode picker / mode-selection UI that chooses this mode for a round and
  wires its `revealPresentation` into `Reveal` at the `Solo.tsx`/
  `GroupRound.tsx` call site - a single-player/group-play concern.
- Real-time synchronization of the paced reveal across multiple players in a
  group round (so everyone watches the same word land at the same moment) -
  a group-play hub concern, out of this story's footprint.

## Technical Notes
- New `ModeConfig` value in `web/src/engine/modes/progressiveReveal.ts`
  (`see: 'subject-only'`, `answer: 'free-text'`, `reveal: 'progressively'`),
  plus this mode's `ModeSurfaces` value (`game-modes/03`'s contract) supplying
  `revealPresentation`. No change to `engine.ts`/`assemble.ts` is expected
  (AC-04) - the pacing is purely a presentation concern layered on top of an
  already-complete `AssembledStory`.
- Component: `web/src/pages/reveal/ProgressiveRevealPresentation.tsx` - reuses
  `buildRevealParts(template, assembled)` (`web/src/pages/revealParts.ts`)
  READ-ONLY to get the same ordered text/word parts `Reveal.tsx`'s default
  path renders, then paces the WORD parts in over time (e.g. a local
  step/interval state revealing one additional `RevealWordPart` at a time,
  while all `RevealTextPart`s render immediately so the sentence shape is
  visible throughout - AC-03). This component is standalone: it does not
  import or edit `Reveal.tsx`; it is PASSED into `Reveal`'s
  `revealPresentation` prop by whichever parent wires the mode (out of scope
  here, per `game-modes/03`'s contract).
- Child safety: same seam as every free-text mode - the safety check lives on
  `collectWord`'s collection path during FILLING (AC-05); the reveal
  presentation never introduces a second, unfiltered path (it only paces
  already-collected, already-vetted words).
- Every color/spacing token from `web/src/theme.ts`; icons from FontAwesome
  only. Any pacing animation uses a `keyframes`/interval approach consistent
  with the existing `segmentPulse`/`tabletGlow`/`twinkle` patterns already in
  `FillBlank.tsx`/`Reveal.tsx` - do not introduce an animation library. TS
  strict, no `any`.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: Progressive Reveal mode's FillBlank rendering (no `seeContext`/`answerSurface` supplied) matches Classic blind's subject-only screen exactly |
| AC-02 | manual: Reveal rendered with `ProgressiveRevealPresentation` as its `revealPresentation` shows words landing one at a time in body order |
| AC-03 | manual: literal text renders immediately; not-yet-revealed words are absent until their step, and the final state matches the default reveal body |
| AC-04 | `web/src/engine/modes/progressiveReveal.test.ts` - asserts the mode is a plain `ModeConfig` literal with a paired `ModeSurfaces` value, and that `buildRevealParts` is called unmodified against a complete `AssembledStory` |
| AC-05 | `web/src/engine/engine.test.ts` (existing `collectWord` safety-hook coverage) exercised with this mode's `answer` axis |

## Dependencies
- game-modes/03-mode-aware-surfaces
- the-reveal/01-text-reveal (reuses `buildRevealParts` read-only)
