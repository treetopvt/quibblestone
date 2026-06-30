// ----------------------------------------------------------------------------
//  mode.ts - the three-axes mode config type (game-modes/01, AC-01).
//
//  This is the entire "one engine, many thin modes" bet (README section 4 /
//  CLAUDE.md section 2) made concrete as data. A game mode is NOT a fork of
//  the engine or a bespoke code path - it is a small, plain config object
//  over exactly three axes:
//
//    1. `see`    - what the player sees while answering:
//                    'nothing'              - no story context at all (blind)
//                    'subject-only'         - just the template's title/subject
//                    'progressive-story'    - the story so far, filled in live
//    2. `answer` - how they answer:
//                    'free-text'  - type any word (passes the safety filter)
//                    'word-bank'  - pick from a curated list (template.wordBank)
//    3. `reveal` - when the assembled story is shown:
//                    'at-end'        - only after every blank is collected
//                    'progressively' - as each blank is filled
//
//  Each axis is a small string-literal union so a new mode is just a new
//  `ModeConfig` value - no new types, no new branches in the engine. Adding a
//  mode is therefore configuration (AC-03), never a change to collection
//  (engine.ts's `collectWord`) or assembly (assemble.ts's `assemble`).
//
//  Slice 1 ships exactly one mode (Classic blind: 'nothing' / 'free-text' /
//  'at-end', see game-modes/02-classic-blind.md). The other axis values
//  (subject-only, progressive-story, word-bank, progressively) are
//  intentionally UNIMPLEMENTED here - this file only has to let them be
//  EXPRESSED as config today (AC-01, AC-04). Building progressive reveal or
//  word-bank mechanics is explicitly out of scope for this story.
//
//  This module does not import anything from template-model (template.ts /
//  assemble.ts) - the axes are independent of the Template/Blank shape,
//  which is exactly why the abstraction holds (a mode never needs to know
//  about blank internals to declare its axes). It also does not import
//  api/ code, React, or SignalR: it is pure config, safe to import from
//  anywhere (game-modes, single-player, group-play, the-reveal).
//
//  Who imports this: engine.ts (reads `mode.answer` to decide whether a
//  collected word needs the safety check - see engine.ts header), and later
//  mode definitions (game-modes/02's classicBlind.ts) and the FillBlank
//  screen (game-modes/02), which read `see` / `reveal` to decide what to
//  render.
// ----------------------------------------------------------------------------

/** What the player sees while answering blanks. */
export type ModeSeeAxis = 'nothing' | 'subject-only' | 'progressive-story';

/** How the player answers a blank. */
export type ModeAnswerAxis = 'free-text' | 'word-bank';

/** When the assembled story is revealed to players. */
export type ModeRevealAxis = 'at-end' | 'progressively';

/**
 * A game mode, expressed purely as a configuration of the three axes
 * (AC-01). `id` and `label` are authoring/display metadata only - they carry
 * no behavior; all behavior comes from the three axis fields below, which the
 * engine (engine.ts) and any mode-aware UI (FillBlank, the-reveal) read.
 *
 * Deliberately flat and serializable (no functions, no class instances) so a
 * mode config can be hand-authored as a plain object literal, matching the
 * authoring style template.ts already established for templates.
 */
export interface ModeConfig {
  /** Stable id for this mode, e.g. "classic-blind". */
  id: string;
  /** Human-facing name, e.g. "Classic (Blind)". */
  label: string;
  /** What the player sees while answering (axis 1). */
  see: ModeSeeAxis;
  /** How the player answers (axis 2). */
  answer: ModeAnswerAxis;
  /** When the reveal happens (axis 3). */
  reveal: ModeRevealAxis;
}
