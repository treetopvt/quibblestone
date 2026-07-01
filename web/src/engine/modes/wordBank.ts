// ----------------------------------------------------------------------------
//  wordBank.ts - the Word Bank mode, expressed purely as ModeConfig
//  (game-modes/04, AC-07).
//
//  One engine, many thin modes (README section 4 / CLAUDE.md section 2): this
//  file is the ENTIRE Word Bank mode definition, mirroring classicBlind.ts's
//  shape exactly. It is a plain data value over the three axes declared in
//  mode.ts - `see`, `answer`, `reveal` - and nothing else. There is no bespoke
//  "word bank mode" code path anywhere in the engine: `engine.ts`'s
//  `collectWord` already branches on `mode.answer === 'word-bank'` to skip the
//  free-text safety check (see engine.ts's header, "word-bank words come from
//  curated, pre-vetted lists ... not free text, so there is nothing to
//  filter") - this file only turns that already-declared axis value on for
//  the first time; it does not add new engine behavior.
//
//  The three axes for Word Bank:
//    - see: 'subject-only'  - same as Classic blind: players see ONLY the
//                             tale's title/subject while answering, never the
//                             surrounding story narrative or the filled words.
//    - answer: 'word-bank'  - players TAP a curated word from the template's
//                             `wordBank` (template.ts's `WordBankEntry`)
//                             instead of typing free text. `collectWord`
//                             records the tapped word via the SAME collection
//                             path every mode uses (AC-03), skipping the
//                             safety check because the word is pre-vetted
//                             curated content, not player-authored free text
//                             (AC-04).
//    - reveal: 'at-end'     - same as Classic blind: the assembled story is
//                             only shown after every blank is collected.
//
//  If building this mode ever required touching engine.ts, mode.ts, or
//  template.ts, that would be an abstraction leak (playbook Principle 2) -
//  it did not: this file only imports the ModeConfig type and returns a
//  literal.
//
//  This is a PURE-TS file - no React import, per mode.ts's own "no React, no
//  SignalR" contract (see mode.ts header). The React-facing answer surface
//  that plugs into FillBlank's `answerSurface` slot lives in the PAGES layer
//  instead: web/src/pages/fillblank/WordBankAnswer.tsx, paired with this
//  config by a future mode picker (out of this story's scope, see
//  docs/features/game-modes/04-word-bank.md "Out of Scope").
//
//  Who imports this: a future mode picker (single-player/group-play,
//  out of this story's scope) that resolves this ModeConfig plus
//  WordBankAnswer.tsx's paired ModeSurfaces value for a live round; this
//  story's own test file (wordBank.test.ts) that proves the axes and the
//  collectWord seam.
// ----------------------------------------------------------------------------

import type { ModeConfig } from '../mode';

/**
 * Word Bank: same subject-only view and end reveal as Classic blind, but
 * players tap a curated word instead of typing free text (game-modes/04,
 * the ANSWER axis's first real mode).
 */
export const wordBank: ModeConfig = {
  id: 'word-bank',
  label: 'Word Bank',
  see: 'subject-only',
  answer: 'word-bank',
  reveal: 'at-end',
};
