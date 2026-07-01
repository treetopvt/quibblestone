# Story: Progressive Story mode

**Feature:** Game Modes Engine  ·  **Status:** In Progress  ·  **Issue:** #84

## Context
The engine's `see` axis (`web/src/engine/mode.ts`) declares
`'progressive-story'` alongside `'nothing'` and `'subject-only'`, but no mode
has ever turned it on. Progressive Story is that mode: as I fill each blank, I
see the story-so-far - literal text plus every already-filled word - rendered
above the prompt card, updating after each submission. Unlike Classic blind's
"no peeking," the tale unfolds live in front of me while I write into the next
gap. This story proves the SEE axis in isolation - it keeps `answer: 'free-text'`
and `reveal: 'at-end'` so it does not also exercise the reveal axis (that is
`game-modes/06`). It plugs into the `seeContext` slot that `game-modes/03`
adds to `FillBlank` - it does NOT touch `FillBlank.tsx` itself. See
[feature.md](./feature.md) and README section 4.

## Acceptance Criteria
- [ ] AC-01: Given Progressive Story mode, when I am prompted for a blank, then
      I see the story-so-far rendered up to (but not including) the current
      blank - literal text plus every already-filled word, using the same
      coral word-highlight treatment as the-reveal (`buildRevealParts`) -
      directly above the FillBlank prompt card, via the `seeContext` slot; I do
      not see any text after the current blank.
- [ ] AC-02: Given I submit a word for the current blank, then the story-so-far
      view updates to include my word in place (coral, in the assembled
      position) before the next blank's prompt appears.
- [ ] AC-03: Given Progressive Story mode, then it is expressed purely as a
      `ModeConfig` (`see: 'progressive-story'`, `answer: 'free-text'`,
      `reveal: 'at-end'`) plus one `ModeSurfaces` value (`game-modes/03`)
      supplying `seeContext` - no new branch is added to `engine.ts`'s
      `collectWord`/`assembleStory` or to `assemble.ts`; the story-so-far view
      is produced by calling the existing `assembleStory` (or `assemble`)
      against the collection-so-far, same as any other mode.
- [ ] AC-04: Given Progressive Story mode, then my submitted word passes the
      safety filter before it is recorded or shown in the story-so-far view
      (the same collection-path seam every free-text mode inherits) - no
      player ever sees an unfiltered word, even transiently, because the
      story-so-far view only ever renders words already present in the
      collection (which `collectWord` only adds after the filter passes).
- [ ] AC-05: Given the last blank in the template is filled, then the
      story-so-far view already equals the full assembled story - no separate
      "final reveal" transition is required by this story, though a caller may
      still route to the Reveal screen (`the-reveal/01`) as a shared
      celebratory moment (confetti) after the last word lands; `reveal:
      'at-end'` means Reveal, when shown, renders its default coral body
      (`game-modes/03` AC-03's default path) - this mode does not touch
      Reveal's presentation slot.

## Out of Scope
- A typing/waiting indicator showing what another player is doing before their
  word lands (a delight-tier nicety).
- Word-by-word "carving" entrance animation for the progressive text (that is
  `reveal-delight/02` territory - this story ships a plain, non-animated
  append and lets a later story layer the animation on, since both consume the
  same `buildRevealParts`-shaped output).
- Letting a player skip ahead to see unfilled future blanks (defeats the
  mode).
- Combining this mode with word-bank answering or progressive reveal (each
  axis proven independently in this slice; combining is a follow-up).
- Multi-player distribution/ordering of WHO answers which progressive blank,
  and any live cross-player broadcast of the shared story-so-far (so every
  player's screen updates as ANY player's word lands) - both are group-play
  hub/distribution concerns, out of this story's footprint, noted in
  feature.md Design notes.
- The mode picker / mode-selection UI that chooses this mode for a round and
  wires its `seeContext` into `FillBlank` at the `Solo.tsx`/`GroupRound.tsx`
  call site - a single-player/group-play concern.

## Technical Notes
- New `ModeConfig` value in `web/src/engine/modes/progressiveStory.ts`
  (`see: 'progressive-story'`, `answer: 'free-text'`, `reveal: 'at-end'`),
  plus this mode's `ModeSurfaces` value (`game-modes/03`'s contract) supplying
  `seeContext`. No change to `engine.ts`/`assemble.ts` is expected (AC-03):
  the story-so-far view is just `assembleStory(template, collectedSoFar)`
  called against a **partial** `CollectedWords` map - `assemble()` is already
  documented as non-throwing on a fewer-words-than-blanks mismatch (see
  `assemble.ts` header), which is exactly the partial-collection case this
  mode relies on. If building this mode ever requires touching `collectWord`,
  `assembleStory`, or `template.ts`, stop and flag it as an abstraction leak.
- Component: `web/src/pages/fillblank/StorySoFarContext.tsx` - reuses
  `buildRevealParts(template, assembledSoFar)` (`web/src/pages/revealParts.ts`)
  READ-ONLY against the partial assembly, applying the same coral highlight
  treatment `Reveal.tsx` uses, but stops rendering at the current blank (slice
  `template.body` up to the current blank's position, or filter
  `buildRevealParts`'s output to the segments preceding it - do not reinvent
  the interleave-and-highlight logic). This component is standalone: it does
  not import or edit `FillBlank.tsx`/`Reveal.tsx`; it is PASSED into
  `FillBlank`'s `seeContext` prop by whichever parent wires the mode (out of
  scope here).
- Child safety: same seam as every free-text mode - the safety check lives on
  `collectWord`'s collection path (AC-04), nothing new to wire here.
- Every color/spacing token from `web/src/theme.ts`; icons from FontAwesome
  only. TS strict, no `any`.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: FillBlank rendered with `StorySoFarContext` as its `seeContext` shows story-so-far text up to the current blank, nothing after |
| AC-02 | manual: submitting a word updates the story-so-far view with the new word highlighted before the next prompt renders |
| AC-03 | `web/src/engine/modes/progressiveStory.test.ts` - asserts the mode is a plain `ModeConfig` literal with a paired `ModeSurfaces` value, and that `assembleStory` is called unmodified against a partial collection |
| AC-04 | `web/src/engine/engine.test.ts` (existing `collectWord` safety-hook coverage) exercised with this mode's `answer` axis |
| AC-05 | manual: after the last blank, story-so-far view equals `assembleStory` on a complete collection |

## Dependencies
- game-modes/03-mode-aware-surfaces
- the-reveal/01-text-reveal (reuses `buildRevealParts` read-only)
