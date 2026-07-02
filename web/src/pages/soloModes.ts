// ----------------------------------------------------------------------------
//  soloModes.ts - the SOLO-named view of the shared game-mode registry.
//
//  single-player/02 first built the mode registry here (solo only). group-play/05
//  generalized it into ./modeRegistry.ts so BOTH Solo.tsx and GroupRound.tsx
//  consume ONE list. This module now simply RE-EXPORTS the shared registry under
//  the solo names Solo.tsx (and soloModes.test.ts) already import, so solo's
//  behavior is byte-for-byte unchanged - there is one registry, viewed two ways.
//
//  New code should import from ./modeRegistry directly (GameMode, GAME_MODES,
//  findMode, DEFAULT_MODE, plus GROUP_MODES for the group picker). These aliases
//  exist only so the existing solo call sites and tests keep working verbatim.
// ----------------------------------------------------------------------------

export type {
  GameMode as SoloMode,
  FillContext as SoloFillContext,
  RevealContext as SoloRevealContext,
} from './modeRegistry';
export {
  GAME_MODES as SOLO_MODES,
  findMode as findSoloMode,
  DEFAULT_MODE as DEFAULT_SOLO_MODE,
} from './modeRegistry';
