// ----------------------------------------------------------------------------
//  soloModes.ts - the solo mode registry (single-player/02, the first consumer
//  of game-modes/03's ModeSurfaces contract).
//
//  Why this exists: game-modes/03-06 shipped four modes - Classic blind
//  (../engine/modes/classicBlind.ts), Word Bank (wordBank.ts), Progressive
//  Story (progressiveStory.ts), Progressive Reveal (progressiveReveal.ts) -
//  each a pure ModeConfig plus a paired ModeSurfaces factory colocated with its
//  surface component. But every one of those stories deliberately deferred the
//  PICKER that selects a mode and wires its surfaces into a live round. This
//  registry is that missing seam for SOLO play: it pairs each existing
//  ModeConfig with the three things Solo.tsx needs to actually play it, and
//  nothing more -
//    - display metadata (blurb + FontAwesome icon) for the picker card;
//    - `eligibleTemplates`: which templates this mode may draw, given the
//      family-safe toggle (free-text modes use selectTemplates; Word Bank uses
//      offerWordBankTemplates so a bank-less template is never offered, per
//      game-modes/04 AC-06);
//    - `fillSurfaces` / `revealSurfaces`: factories that turn the round's
//      runtime context into the ModeSurfaces (answerSurface / seeContext /
//      revealPresentation) FillBlank / Reveal render through their game-modes/03
//      optional slots.
//
//  This is CONFIGURATION, not new engine behavior (README section 4 / CLAUDE.md
//  section 2): it adds no ModeConfig, forks neither engine.ts nor FillBlank.tsx
//  / Reveal.tsx, and reuses each mode's own colocated surface factory verbatim.
//  Classic blind returns the documented `classicBlindSurfaces` no-op (`{}`) for
//  both slots, so selecting it renders FillBlank / Reveal byte-for-byte as
//  before this story (single-player/02 AC-06).
//
//  Why the pages layer (not engine/): a ModeSurfaces value carries React nodes
//  (see ./modeSurfaces.ts's header for why surfaces are a pages concern, not an
//  engine one), so this registry - which builds them - belongs beside the pages
//  that consume it, not in the pure-TS engine. The eligible-template selectors
//  and the mode lookup are pure, so they stay unit-testable without a render
//  harness (see soloModes.test.ts).
//
//  Group-play mode selection (host picks at round start, broadcast to the room)
//  is a separate, heavier story and is NOT built here - this registry is solo
//  only.
// ----------------------------------------------------------------------------

import type { IconName } from '@fortawesome/fontawesome-svg-core';
import { selectTemplates } from '../content/familySafe';
import { offerWordBankTemplates } from '../content/wordBankOffering';
import type { AssembledStory } from '../engine/assemble';
import type { CollectedWords } from '../engine/engine';
import type { ModeConfig } from '../engine/mode';
import { classicBlind } from '../engine/modes/classicBlind';
import { progressiveReveal } from '../engine/modes/progressiveReveal';
import { progressiveStory } from '../engine/modes/progressiveStory';
import { wordBank } from '../engine/modes/wordBank';
import type { Blank, Template } from '../engine/template';
import { wordBankSurfaces } from './fillblank/WordBankAnswer';
import { progressiveStorySurfaces } from './fillblank/StorySoFarContext';
import { progressiveRevealSurfaces } from './reveal/ProgressiveRevealPresentation';
import { classicBlindSurfaces, type ModeSurfaces } from './modeSurfaces';

/**
 * The runtime context a mode needs to build its FILL-time surfaces
 * (answerSurface / seeContext), resolved by Solo per blank. `onSubmit` is the
 * SAME callback FillBlank is given (Solo's handleSubmitWord) - a surface that
 * owns its own submit affordance (e.g. Word Bank) must call it, never a second
 * path into collection (game-modes/03 AC-06).
 */
export interface SoloFillContext {
  template: Template;
  /** Words collected so far - may be partial mid-round (used by Progressive Story's story-so-far). */
  collectedSoFar: CollectedWords;
  /** The blank currently being prompted for. */
  currentBlank: Blank;
  onSubmit: (word: string) => Promise<{ accepted: boolean; message?: string }>;
}

/** The runtime context a mode needs to build its REVEAL-time surface (revealPresentation). */
export interface SoloRevealContext {
  template: Template;
  /** The already-complete assembled story (Progressive Reveal paces this; never a partial). */
  assembled: AssembledStory;
}

/**
 * One selectable mode in the solo picker: the existing ModeConfig plus the
 * display metadata and the wiring Solo needs to play it. `fillSurfaces` /
 * `revealSurfaces` default to the Classic-blind no-op for modes that do not
 * override that slot, so a mode only supplies the ONE surface it changes.
 */
export interface SoloMode {
  config: ModeConfig;
  /** One-line player-facing description shown on the picker card. */
  blurb: string;
  /** FontAwesome icon name (registered in ../fontawesome.ts) for the picker card. */
  icon: IconName;
  /** Templates this mode may draw for a round, given the family-safe toggle's position. */
  eligibleTemplates: (library: readonly Template[], familySafeOn: boolean) => Template[];
  /** Builds the FillBlank surfaces (answerSurface / seeContext) for the current blank. */
  fillSurfaces: (ctx: SoloFillContext) => ModeSurfaces;
  /** Builds the Reveal surface (revealPresentation) for the finished story. */
  revealSurfaces: (ctx: SoloRevealContext) => ModeSurfaces;
}

/** Free-text modes all draw from the same family-safe template selection. */
const selectFamilySafe = (library: readonly Template[], familySafeOn: boolean): Template[] =>
  selectTemplates(library, familySafeOn);

/**
 * The four solo modes, in picker order. Classic blind is first so it is the
 * default selection (single-player/02 AC-01/AC-06): the existing zero-choice
 * solo flow keeps working with one tap on Start.
 */
export const SOLO_MODES: readonly SoloMode[] = [
  {
    config: classicBlind,
    blurb: 'Fill each blank blind, then see the whole silly tale at the end.',
    icon: 'eye-slash',
    eligibleTemplates: selectFamilySafe,
    fillSurfaces: () => classicBlindSurfaces,
    revealSurfaces: () => classicBlindSurfaces,
  },
  {
    config: wordBank,
    blurb: 'Tap a word from a curated list instead of typing - no spelling required.',
    icon: 'wand-magic-sparkles',
    eligibleTemplates: offerWordBankTemplates,
    fillSurfaces: ({ template, currentBlank, onSubmit }) =>
      wordBankSurfaces({ wordBank: template.wordBank ?? [], blank: currentBlank, onSubmit }),
    revealSurfaces: () => classicBlindSurfaces,
  },
  {
    config: progressiveStory,
    blurb: 'Watch the story build up above each blank as you fill it in.',
    icon: 'pen-nib',
    eligibleTemplates: selectFamilySafe,
    fillSurfaces: ({ template, collectedSoFar, currentBlank }) =>
      progressiveStorySurfaces({ template, collectedSoFar, currentBlankId: currentBlank.id }),
    revealSurfaces: () => classicBlindSurfaces,
  },
  {
    config: progressiveReveal,
    blurb: 'Fill blind, then the finished tale unveils one word at a time.',
    icon: 'star',
    eligibleTemplates: selectFamilySafe,
    fillSurfaces: () => classicBlindSurfaces,
    revealSurfaces: ({ template, assembled }) => progressiveRevealSurfaces({ template, assembled }),
  },
];

/**
 * Looks up a solo mode by its ModeConfig id, falling back to Classic blind (the
 * first/default entry) for an unknown id rather than returning undefined - the
 * picker can never land on "no mode," so callers never have to guard it.
 */
export function findSoloMode(id: string): SoloMode {
  return SOLO_MODES.find((mode) => mode.config.id === id) ?? SOLO_MODES[0];
}

/** The default solo mode (Classic blind) - the picker's initial selection. */
export const DEFAULT_SOLO_MODE: SoloMode = SOLO_MODES[0];
