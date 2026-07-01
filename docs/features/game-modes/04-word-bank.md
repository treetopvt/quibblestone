# Story: Word Bank mode

**Feature:** Game Modes Engine  ·  **Status:** Not Started  ·  **Issue:** #53

## Context
README section 5's "Blind + word bank" variation, reframed here as the ANSWER
axis's first real mode: same `see`/`reveal` shape as Classic blind (nothing/
subject-only, at-end), but the `answer` axis flips from `free-text` to
`word-bank` - instead of typing, the player taps a word from a short curated
list attached to the template (`Template.wordBank` / `WordBankEntry`, already
defined in `web/src/engine/template.ts`). `mode.ts` and `engine.ts` were
already written to allow `answer: 'word-bank'`; this is the first mode to
actually turn it on. It plugs into the `answerSurface` slot that
`game-modes/03` adds to `FillBlank` - it does NOT touch `FillBlank.tsx` itself.
See [feature.md](./feature.md) and README section 4.

## Acceptance Criteria
- [ ] AC-01: Given Word Bank mode, when I am prompted for a blank, then I see
      the same subject-only stone-tablet prompt card as Classic blind (category
      chip, prompt, sub-hint), but the input area shows a **tappable list of
      words** drawn from the template's word bank for that blank's category,
      instead of a free-text input - rendered by plugging a new component into
      `FillBlank`'s `answerSurface` slot (`game-modes/03`), never by editing
      `FillBlank.tsx`.
- [ ] AC-02: Given the word-bank list for the current blank, then it shows only
      entries whose `category` matches the blank's `category` (per
      `WordBankEntry.category` in `template.ts`); tapping a word selects it as
      my answer for that blank.
- [ ] AC-03: Given I have selected a word, when I submit it, then it is
      recorded exactly like a free-text submission - via `engine.ts`'s
      `collectWord`, the SAME collection path every mode uses, proving this
      mode is a configuration, not a parallel engine.
- [ ] AC-04: Given `mode.answer === 'word-bank'`, then the submission is
      recorded WITHOUT going through the free-text safety filter, matching
      `engine.ts`'s already-documented behavior ("word-bank words come from
      curated, pre-vetted lists ... not free text, so there is nothing to
      filter") - this AC proves that documented seam end to end, it does not
      change it.
- [ ] AC-05: Given the family-safe toggle is ON for the session, then only word
      banks belonging to family-safe-tagged templates (`TemplateTags.familySafe`)
      are ever offered as the source list - word-bank content is still gated by
      the toggle even though individual bank entries skip the free-text filter
      (README section 6). This gating happens at content-selection time (which
      templates' banks are offered), not a per-tap check. No PII is collected
      by this mode (a tapped word carries no personal data).
- [ ] AC-06: Given a template has no word bank at all, then Word Bank mode is
      simply not offered as a playable mode for that template - the pure
      offering helper (Technical Notes) filters by `template.wordBank`
      presence, and this mode never crashes or renders an empty list for a
      bank-less template.
- [ ] AC-07: Given Word Bank mode, then it is expressed as a `ModeConfig`
      literal (`see: 'subject-only'`, `answer: 'word-bank'`, `reveal:
      'at-end'`) plus one `ModeSurfaces` value (`game-modes/03`) supplying
      `answerSurface` - no bespoke "word bank mode" code path exists anywhere
      in the engine or in `FillBlank.tsx`/`Reveal.tsx`.

## Out of Scope
- Owner-curated word banks (the host supplying the bank live instead of the
  template) - parked, see feature.md "Parked - Phase 2+/3".
- Progressive story or progressive reveal combined with a word bank - each
  axis is proven independently in this slice (04/05/06 each touch exactly one
  axis); combining axes is a follow-up once all three are proven.
- Authoring UI for word banks (templates stay hand-authored TS literals per
  `template-model/02`).
- A "shuffle" or randomized-order presentation of the bank list (a later
  delight-tier nicety).
- The mode picker / mode-selection UI that chooses this mode for a round and
  wires its surfaces into `FillBlank` at the `Solo.tsx`/`GroupRound.tsx` call
  site - a single-player/group-play concern, noted in feature.md Design notes.

## Technical Notes
- New `ModeConfig` value in `web/src/engine/modes/wordBank.ts`, mirroring
  `classicBlind.ts`'s shape (`see: 'subject-only'`, `answer: 'word-bank'`,
  `reveal: 'at-end'`), plus this mode's `ModeSurfaces` value (`game-modes/03`'s
  contract) supplying `answerSurface`. Do not touch `mode.ts` or `engine.ts` -
  `ModeAnswerAxis` already includes `'word-bank'` and `collectWord` already
  branches on `mode.answer` to skip the safety check; this story is proof of
  that seam, not new engine work.
- Answer surface component: `web/src/pages/fillblank/WordBankAnswer.tsx` - a
  tappable chip/tile list sourced from
  `template.wordBank.filter(entry => entry.category === blank.category)`,
  reusing the same teal MUI Chip tap language already established by
  FillBlank's "Need a spark?" spark-word row (same visual affordance family
  recognizes it), with big tap targets. This component is standalone: it does
  not import or edit `FillBlank.tsx`, it is PASSED into `FillBlank`'s
  `answerSurface` prop by whichever parent wires the mode (out of scope here,
  per game-modes/03's contract).
- Content-selection helper: `web/src/content/wordBankOffering.ts` - a pure
  function "which templates' banks are offered given the family-safe toggle
  state" (filters by `template.wordBank` presence AND, when family-safe is on,
  `template.tags.familySafe`), reusing the family-safe rule already established
  by `child-safety/02`'s `selectTemplates`/`familySafe.ts` rather than
  reinventing a second gating rule.
- Family-safe gating (AC-05) is a content-selection concern, not a per-request
  check - it happens when the mode/template combination is offered (session
  creation / round start), consistent with `child-safety/02`'s existing scope
  and README section 3's session-creation-time entitlement pattern (word-bank
  offering is content gating, not billing, but the same "decide once, up
  front" shape applies).
- Every color/spacing token from `web/src/theme.ts`; icons from FontAwesome
  only (`web/src/fontawesome.ts`). TS strict, no `any`.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: FillBlank rendered with `WordBankAnswer` as its `answerSurface` shows a tappable word list instead of the text input |
| AC-02 | `web/src/pages/fillblank/WordBankAnswer.test.ts` (or a content-selection unit test) - filters `wordBank` entries by the current blank's category |
| AC-03 | `web/src/engine/engine.test.ts` - `collectWord` accepts a word-bank submission via the standard path |
| AC-04 | `web/src/engine/engine.test.ts` (existing coverage) - asserts no safety-check hook invocation when `mode.answer === 'word-bank'` |
| AC-05 | `web/src/content/wordBankOffering.test.ts` - only family-safe-tagged templates' banks are offered when the toggle is on |
| AC-06 | `web/src/content/wordBankOffering.test.ts` - a template with no `wordBank` is never offered |
| AC-07 | `web/src/engine/modes/wordBank.test.ts` - asserts the mode is a plain `ModeConfig` literal with a paired `ModeSurfaces` value |

## Dependencies
- game-modes/03-mode-aware-surfaces
- template-model/01-template-schema (`WordBankEntry` / `Template.wordBank`)
- child-safety/01-profanity-filter
- child-safety/02-family-safe-toggle
