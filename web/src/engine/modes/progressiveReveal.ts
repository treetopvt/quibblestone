// ----------------------------------------------------------------------------
//  progressiveReveal.ts - the Progressive Reveal mode, expressed purely as
//  ModeConfig (game-modes/06, AC-04).
//
//  One engine, many thin modes (README section 4 / CLAUDE.md section 2): this
//  file is the ENTIRE Progressive Reveal mode definition on the engine side.
//  It is a plain data value over the three axes declared in mode.ts - `see`,
//  `answer`, `reveal` - nothing else. There is no bespoke "progressive reveal"
//  branch anywhere in the engine (engine.ts's collectWord/assemble.ts's
//  assemble are mode-agnostic and never branch on `mode.id`); the only other
//  piece this story adds is a presentation component
//  (web/src/pages/reveal/ProgressiveRevealPresentation.tsx) that plugs into
//  the `revealPresentation` slot Reveal.tsx already exposes (game-modes/03) -
//  it does not live here, because a React node is a UI concern, not engine
//  config (see mode.ts's own "no React" header note, and modeSurfaces.ts's
//  header for why surfaces are colocated with the pages that consume them).
//
//  The three axes for Progressive Reveal:
//    - see: 'subject-only' - IDENTICAL to Classic blind while filling: players
//                           see only the tale's title/subject, never the
//                           surrounding narrative or one another's words.
//                           This mode supplies no `seeContext`/`answerSurface`
//                           override (AC-01) - only its `revealPresentation`
//                           differs from Classic blind.
//    - answer: 'free-text' - players type any word; engine.ts's collectWord
//                             routes it through the injected SafetyCheck
//                             before it is ever recorded (AC-05) - the same
//                             seam every free-text mode inherits, unchanged.
//    - reveal: 'progressively' - unlike Classic blind's 'at-end', the already
//                                -complete AssembledStory is unveiled one
//                                filled word at a time (paced/stepped) at the
//                                reveal step, rather than all at once. Pacing
//                                is a rendering concern layered on top of a
//                                complete assembly (AC-04) - it never changes
//                                WHEN collection happens, only how the final
//                                result is shown.
//
//  If building this mode ever required touching engine.ts, mode.ts,
//  assemble.ts, or template.ts, that would be an abstraction leak (playbook
//  Principle 2) - it did not: this file only imports the ModeConfig type and
//  returns a literal, exactly mirroring classicBlind.ts.
//
//  Who imports this: the (out-of-scope) future mode picker that pairs this
//  ModeConfig with its ModeSurfaces value (progressiveRevealSurfaces, see
//  ../../pages/reveal/ProgressiveRevealPresentation.tsx) and wires both into
//  a live round.
// ----------------------------------------------------------------------------

import type { ModeConfig } from '../mode';

/**
 * Progressive Reveal: identical blind filling to Classic blind (subject-only,
 * free-text), but the reveal paces the finished story in one word at a time
 * instead of showing it all at once (game-modes/06).
 */
export const progressiveReveal: ModeConfig = {
  id: 'progressive-reveal',
  label: 'Progressive Reveal',
  see: 'subject-only',
  answer: 'free-text',
  reveal: 'progressively',
};
