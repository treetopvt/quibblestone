// ----------------------------------------------------------------------------
//  classicBlind.ts - the Classic (Blind) mode, expressed purely as ModeConfig
//  (game-modes/02, AC-08).
//
//  One engine, many thin modes (README section 4 / CLAUDE.md section 2): this
//  file is the ENTIRE Classic-blind mode definition. It is a plain data value
//  over the three axes declared in mode.ts - `see`, `answer`, `reveal` - and
//  nothing else. There is no bespoke "classic blind" code path anywhere in the
//  engine (engine.ts's collectWord/assembleStory are mode-agnostic and never
//  branch on `mode.id`); the only other piece this story adds is the FillBlank
//  filler screen (web/src/pages/FillBlank.tsx), which reads `see`/`reveal` off
//  whatever ModeConfig it is given (this one, today) rather than hardcoding
//  Classic-blind behavior.
//
//  The three axes for Classic blind:
//    - see: 'subject-only' - players see ONLY the tale's title/subject while
//                           answering (e.g. "The Wobbly Wizard & the Golden
//                           Sock"), never the surrounding story narrative. So
//                           it stays blind to the joke - the filled-in words
//                           and the assembled story are hidden until the end -
//                           while the FillBlank subject label tells the player
//                           which tale they are carving. The reassurance panel
//                           on FillBlank makes the "no peeking at the story"
//                           promise explicit.
//    - answer: 'free-text' - players type any word; engine.ts's collectWord
//                             routes it through the injected SafetyCheck
//                             before it is ever recorded (AC-05 on this
//                             story, AC-05 on engine.ts).
//    - reveal: 'at-end'  - the assembled story is only shown after every
//                          blank is collected (single-player: after the last
//                          FillBlank submission; group play: after the whole
//                          roster finishes).
//
//  If building this mode ever required touching engine.ts, mode.ts, or
//  template.ts, that would be an abstraction leak (playbook Principle 2) -
//  it did not: this file only imports the ModeConfig type and returns a
//  literal.
//
//  Who imports this: single-player (game-modes/02's thin vertical slice
//  passes this into collectWord/FillBlank), and later group-play once it
//  offers a mode picker.
// ----------------------------------------------------------------------------

import type { ModeConfig } from '../mode';

/**
 * Classic (Blind): no story context while answering, free-text answers, one
 * reveal at the very end. Slice 1's only mode (README section 7 / CLAUDE.md
 * section 7 - the thin vertical slice).
 */
export const classicBlind: ModeConfig = {
  id: 'classic-blind',
  label: 'Classic (Blind)',
  see: 'subject-only',
  answer: 'free-text',
  reveal: 'at-end',
};
