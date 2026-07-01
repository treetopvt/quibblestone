// ----------------------------------------------------------------------------
//  progressiveStory.ts - the Progressive Story mode, expressed purely as
//  ModeConfig (game-modes/05, AC-03).
//
//  One engine, many thin modes (README section 4 / CLAUDE.md section 2): this
//  file is the ENTIRE Progressive Story mode definition. Like classicBlind.ts,
//  it is a plain data value over the three axes declared in mode.ts - `see`,
//  `answer`, `reveal` - and nothing else. There is no bespoke "progressive
//  story" branch anywhere in the engine (engine.ts's collectWord/assembleStory
//  never branch on `mode.id`); the only other piece this story adds is the
//  seeContext surface component (../../pages/fillblank/StorySoFarContext.tsx),
//  which FillBlank (game-modes/03's `seeContext` slot) renders for whichever
//  ModeConfig it is given.
//
//  The three axes for Progressive Story:
//    - see: 'progressive-story' - the player sees the STORY SO FAR (literal
//                                  text plus every already-filled word,
//                                  highlighted coral) rendered above the
//                                  prompt card, updating live as each blank is
//                                  filled. This is the axis this story proves
//                                  in isolation - unlike Classic blind's
//                                  subject-only "no peeking," the tale unfolds
//                                  in front of the player while they write.
//    - answer: 'free-text' - players type any word; engine.ts's collectWord
//                             still routes it through the injected
//                             SafetyCheck before it is ever recorded (AC-04) -
//                             nothing about the `see` axis changes the
//                             collection-path safety seam.
//    - reveal: 'at-end' - kept the SAME as Classic blind deliberately (AC-05):
//                          this story proves the SEE axis cleanly, distinct
//                          from the REVEAL axis (game-modes/06's
//                          progressive-reveal mode). The story-so-far view
//                          already equals the full assembled story once the
//                          last blank lands, so no separate "final reveal"
//                          transition is required - a caller may still route
//                          to the shared Reveal screen as a celebratory beat.
//
//  If building this mode ever required touching engine.ts, mode.ts, or
//  template.ts, that would be an abstraction leak (playbook Principle 2) - it
//  did not: this file only imports the ModeConfig type and returns a literal,
//  exactly like classicBlind.ts.
//
//  Who imports this: whichever future mode picker resolves the active mode for
//  a round (single-player/group-play, out of this story's scope) - paired,
//  via the colocated `progressiveStorySurfaces` factory in
//  StorySoFarContext.tsx, with this mode's `seeContext` surface.
// ----------------------------------------------------------------------------

import type { ModeConfig } from '../mode';

/**
 * Progressive Story: the story-so-far unfolds live above the prompt card as
 * each blank is filled, free-text answers, one reveal at the very end (the
 * REVEAL axis is proven separately by game-modes/06).
 */
export const progressiveStory: ModeConfig = {
  id: 'progressive-story',
  label: 'Progressive Story',
  see: 'progressive-story',
  answer: 'free-text',
  reveal: 'at-end',
};
