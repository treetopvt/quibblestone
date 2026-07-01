// ----------------------------------------------------------------------------
//  modeSurfaces.ts - the ModeSurfaces contract (game-modes/03, AC-05).
//
//  Why this lives in web/src/pages/ and NOT web/src/engine/modes/: a "surface"
//  is a React node - a piece of UI a mode plugs into FillBlank or Reveal (a
//  tappable word-bank list, a story-so-far view, a paced reveal presentation).
//  That is presentation, not engine data. web/src/engine/mode.ts's own header
//  is explicit that the engine stays "pure config, safe to import from
//  anywhere" and never imports React, SignalR, or api/ code - a mode's AXES
//  (see/answer/reveal, ModeConfig) belong there because they are plain,
//  serializable data the engine reads to decide behavior (collectWord's
//  safety-check branch, etc). Surfaces have no such reuse-from-anywhere need -
//  they are consumed by exactly two page components (FillBlank, Reveal) - so
//  colocating this contract with those pages keeps web/src/engine/ free of
//  React imports, per mode.ts's own "no React, no SignalR" contract, rather
//  than smuggling a UI concern into the pure-TS engine layer.
//
//  What this is: three OPTIONAL slots a mode may supply, resolved and passed
//  down by whatever PARENT screen owns the active mode (Solo.tsx / GroupRound
//  today only ever supply none of them, i.e. Classic blind - see
//  classicBlindSurfaces below). ModeConfig (engine/mode.ts) is NOT extended
//  with a surfaces field - each mode (04 word-bank, 05 progressive-story, 06
//  progressive-reveal) will export its ModeConfig and its ModeSurfaces as two
//  separate, paired values, and a future mode picker (out of this story's
//  scope) is what actually resolves "the active mode's surfaces" and passes
//  them into FillBlank/Reveal. This file only defines the shared shape so
//  every mode colocates its plug-in surfaces the same way, instead of each
//  mode inventing its own ad hoc prop bag.
//
//  Contract, not behavior: this module exports no components, no logic - just
//  a type and one documented default. FillBlank.tsx and Reveal.tsx interpret
//  each field (render seeContext above the prompt card; replace the free-text
//  input + spark chips with answerSurface; replace the coral-highlight body
//  with revealPresentation) - see their own header comments for the render
//  contract on each slot.
// ----------------------------------------------------------------------------

import type { ReactNode } from 'react';

/**
 * The optional UI slots a game mode MAY supply to the two shared screens.
 * Every field is optional: a mode that only changes ONE axis (e.g. Word Bank,
 * which only changes `answer`) supplies only the ONE surface it needs and
 * omits the rest, so the two unaffected screens/slots keep rendering their
 * Classic-blind default with zero extra wiring.
 */
export interface ModeSurfaces {
  /**
   * Replaces FillBlank's free-text input + spark-chip row when supplied (e.g.
   * a tappable word-bank list, game-modes/04). Owns its own submit affordance
   * and must still call the SAME `onSubmitWord` FillBlank was given - there is
   * never a second path into word collection (AC-06).
   */
  answerSurface?: ReactNode;
  /**
   * Rendered above FillBlank's prompt card (below the subject label) when
   * supplied - e.g. a "story so far" view for the progressive-story mode
   * (game-modes/05). A pure addition: never replaces the existing `subject`
   * label.
   */
  seeContext?: ReactNode;
  /**
   * Replaces Reveal's default coral-highlight body when supplied - e.g. a
   * paced, word-by-word reveal for the progressively-reveal mode
   * (game-modes/06).
   */
  revealPresentation?: ReactNode;
}

/**
 * Classic blind's explicit "no surfaces" default: every slot omitted, so
 * FillBlank and Reveal fall back to their original, hardcoded Classic-blind
 * rendering (subject-only label, free-text input + spark chips, the
 * coral-highlight body via buildRevealParts). Spelled out as `{}` rather than
 * left for a caller to infer, so a future reader immediately sees this is
 * deliberate: Classic blind (game-modes/02) needs no plug-in surface for any
 * of its three axes. Solo.tsx and GroupRound.tsx do not even import this
 * constant today (AC-04: this story ships the contract unused by the two
 * existing callers) - it exists as the reference "no surfaces" shape every
 * future mode's own ModeSurfaces value is diffed against.
 */
export const classicBlindSurfaces: ModeSurfaces = {};
