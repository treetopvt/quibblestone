# Story: Blind + word bank

**Feature:** Game Modes Engine  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
README section 5: "Blind + word bank - same [as Classic blind], but answer from
a provided list of words." This is Classic blind's exact `see`/`reveal` axes
(nothing/subject-only, at-end) with the `answer` axis flipped from `free-text`
to `word-bank`: instead of typing, the player taps a word from a short curated
list attached to the template (`Template.wordBank` / `WordBankEntry`, already
defined in `web/src/engine/template.ts`). It is the mode that first exercises
the `word-bank` half of the answer axis, which `mode.ts` and `engine.ts` were
already written to allow but Slice 1 never implements. See
[feature.md](./feature.md) and README section 4.

## Acceptance Criteria
- [ ] AC-01: Given Blind + word bank, when I am prompted for a blank, then I see
      the same subject-only stone-tablet prompt card as Classic blind (category
      chip, prompt, sub-hint - `game-modes/02` AC-01/AC-02), but the input area
      is replaced by a **tappable list of words** drawn from the template's word
      bank for that blank's category, instead of a free-text input.
- [ ] AC-02: Given the word-bank list for the current blank, then it shows only
      entries whose `category` matches the blank's `category` (per
      `WordBankEntry.category` in `template.ts`); tapping a word selects it as
      my answer for that blank.
- [ ] AC-03: Given I have selected a word, when I tap the gold "Next word ->"
      CTA, then my selection is submitted and recorded exactly like a free-text
      submission (via `engine.ts`'s `collectWord`) - the mode is a configuration
      of the SAME collection path, not a parallel one.
- [ ] AC-04: Given `mode.answer === 'word-bank'`, then the submission is
      recorded WITHOUT going through the free-text safety filter, matching
      `engine.ts`'s already-documented behavior ("word-bank words come from
      curated, pre-vetted lists ... not free text, so there is nothing to
      filter") - this AC exists to prove that documented behavior end to end,
      not to change it.
- [ ] AC-05: Given the family-safe toggle is ON for the session, then only word
      banks belonging to family-safe-tagged templates (`TemplateTags.familySafe`)
      are ever offered as the source list - word-bank content is still gated by
      the toggle even though individual bank entries skip the free-text filter
      (README section 6; `child-safety/02` AC-01/AC-03). No PII is collected by
      this mode (word selection carries no personal data).
- [ ] AC-06: Given Blind + word bank, then it is expressed as a `ModeConfig`
      literal (`see: 'subject-only'` (or `'nothing'`), `answer: 'word-bank'`,
      `reveal: 'at-end'`) - the same FillBlank screen and the same engine
      functions render/collect it, with no bespoke "word bank mode" code path.
- [ ] AC-07: Given a template has no word bank at all, then Blind + word bank is
      simply not offered as a playable mode for that template (the mode picker,
      wherever it lives, filters by `template.wordBank` presence) - this mode
      never crashes or renders an empty list for a bank-less template.

## Out of Scope
- Owner-curated word banks (the host supplying the bank live) - that is
  `game-modes/05`, which builds on this story's rendering.
- Progressive reveal combined with a word bank - covered by `game-modes/03`
  AC-05, not re-tested here.
- Authoring UI for word banks (templates are still hand-authored TS literals
  per `template-model/02`).
- A "shuffle" or randomized-order presentation of the bank list (a later
  delight-tier nicety).

## Technical Notes
- Add a new `ModeConfig` value in `web/src/engine/modes/` (e.g.
  `blindWordBank.ts`), mirroring `classicBlind.ts`'s shape but with `answer:
  'word-bank'`. Do not touch `mode.ts` or `engine.ts` - `ModeAnswerAxis` already
  includes `'word-bank'` and `collectWord` already branches on `mode.answer` to
  skip the safety check (see `engine.ts`'s file header and AC-04 above); this
  story is proof of that seam, not new engine work.
- UI: `web/src/pages/FillBlank.tsx` currently always renders a free-text
  `TextField` + spark chips. This story needs an alternate answer surface for
  `answer === 'word-bank'` - a tappable chip/tile list sourced from
  `template.wordBank.filter(entry => entry.category === blank.category)`.
  Because FillBlank is transport-agnostic and purely controlled (see its file
  header), the cleanest approach is likely a sibling presentational variant (or
  a mode-aware prop) rather than an `if` sprinkled through the existing
  free-text-shaped component - decide at build time whether that is a new
  `WordBankFillBlank.tsx` composed the same way `FillBlank.tsx` is, or a prop on
  the existing component; either way it must stay a config decision the PARENT
  makes (which screen to render / which prop to pass), never a fork of the
  engine underneath it.
- Selecting a word bank tile reuses the same visual chip language as the
  existing "Need a spark?" row (teal MUI Chips) so the family already
  recognizes the tap affordance from Classic blind.
- Family-safe gating (AC-05) is a content-selection concern, not a per-request
  check - it happens when the mode/template combination is offered (session
  creation / round start), consistent with `child-safety/02`'s existing scope
  ("gates content and word banks" per its Context) and README section 3's
  session-creation-time entitlement pattern (word-bank offering is content
  gating, not billing, but the same "decide once, up front" shape applies).
- Every color/spacing token from `web/src/theme.ts`; icons from FontAwesome
  only (`web/src/fontawesome.ts`).

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: FillBlank in this mode renders a tappable word list instead of the text input |
| AC-02 | `web/src/engine/modes/blindWordBank.test.ts` or a content-selection unit test - filters `wordBank` entries by the current blank's category |
| AC-03 | `web/src/engine/engine.test.ts` - `collectWord` accepts a word-bank submission via the standard path |
| AC-04 | `web/src/engine/engine.test.ts` (existing coverage) - asserts no safety-check hook invocation when `mode.answer === 'word-bank'` |
| AC-05 | manual + `web/src/content/familySafe.test.ts`-style unit test - only family-safe-tagged templates' banks are offered when the toggle is on |
| AC-06 | `web/src/engine/modes/blindWordBank.test.ts` - asserts the mode is a plain `ModeConfig` literal |
| AC-07 | manual: a template with no `wordBank` never appears in the word-bank mode picker |

## Dependencies
- game-modes/01-mode-interface
- game-modes/02-classic-blind (FillBlank screen to extend/compose)
- template-model/01-template-schema (`WordBankEntry` / `Template.wordBank`)
- child-safety/01-profanity-filter
- child-safety/02-family-safe-toggle
