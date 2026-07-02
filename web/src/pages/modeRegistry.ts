// ----------------------------------------------------------------------------
//  modeRegistry.ts - the SHARED game-mode registry (group-play/05).
//
//  History: single-player/02 first built this registry as `soloModes.ts` for
//  solo play only. group-play/05 GENERALIZES it into this shared module so BOTH
//  Solo.tsx and GroupRound.tsx consume ONE list - the host picks the mode for a
//  group round exactly the way a solo player picks it for themselves. `soloModes.ts`
//  now re-exports the solo-named aliases from here, so solo's behavior is byte-for-
//  byte unchanged; this file is the single source of truth for the modes.
//
//  What it is: game-modes/03-06 shipped four modes - Classic blind
//  (../engine/modes/classicBlind.ts), Word Bank (wordBank.ts), Progressive
//  Story (progressiveStory.ts), Progressive Reveal (progressiveReveal.ts) -
//  each a pure ModeConfig plus a paired ModeSurfaces factory colocated with its
//  surface component. This registry pairs each existing ModeConfig with the three
//  things a live round needs to actually play it, and nothing more -
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
//  before this registry existed (single-player/02 AC-06).
//
//  Why the pages layer (not engine/): a ModeSurfaces value carries React nodes
//  (see ./modeSurfaces.ts's header for why surfaces are a pages concern, not an
//  engine one), so this registry - which builds them - belongs beside the pages
//  that consume it, not in the pure-TS engine. The eligible-template selectors
//  and the mode lookup are pure, so they stay unit-testable without a render
//  harness (see modeRegistry.test.ts / soloModes.test.ts).
//
//  GROUP vs SOLO (group-play/05, AC-04/AC-05): solo offers ALL FOUR modes
//  (GAME_MODES). The group picker offers only the THREE that need NO new
//  real-time surface - Classic Blind, Word Bank, Progressive Reveal (GROUP_MODES).
//  Progressive Story is deliberately EXCLUDED from the group set (AC-05): its
//  group "story so far" must reflect OTHER players' in-progress fills, which needs
//  a live partial-fill broadcast (a new hub surface) that does not exist yet, so
//  it is deferred rather than shipped half-working. The server enforces the same
//  offered set (api/src/Content/GameModeCatalog.cs) - keep the two in sync by hand.
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
 * (answerSurface / seeContext), resolved by the round screen per blank.
 * `onSubmit` is the SAME callback FillBlank is given (Solo's handleSubmitWord /
 * GroupRound's handleSubmitWord) - a surface that owns its own submit affordance
 * (e.g. Word Bank) must call it, never a second path into collection
 * (game-modes/03 AC-06).
 */
export interface FillContext {
  template: Template;
  /**
   * Words collected so far - may be partial mid-round (used by Progressive
   * Story's story-so-far). Group play never offers Progressive Story (AC-05),
   * so GroupRound passes an empty collection; the three group modes ignore it.
   */
  collectedSoFar: CollectedWords;
  /** The blank currently being prompted for. */
  currentBlank: Blank;
  onSubmit: (word: string) => Promise<{ accepted: boolean; message?: string }>;
}

/** The runtime context a mode needs to build its REVEAL-time surface (revealPresentation). */
export interface RevealContext {
  template: Template;
  /** The already-complete assembled story (Progressive Reveal paces this; never a partial). */
  assembled: AssembledStory;
}

/**
 * One selectable mode: the existing ModeConfig plus the display metadata and
 * the wiring a round needs to play it. `fillSurfaces` / `revealSurfaces`
 * default to the Classic-blind no-op for modes that do not override that slot,
 * so a mode only supplies the ONE surface it changes.
 */
export interface GameMode {
  config: ModeConfig;
  /** One-line player-facing description shown on the picker card. */
  blurb: string;
  /** FontAwesome icon name (registered in ../fontawesome.ts) for the picker card. */
  icon: IconName;
  /** Templates this mode may draw for a round, given the family-safe toggle's position. */
  eligibleTemplates: (library: readonly Template[], familySafeOn: boolean) => Template[];
  /** Builds the FillBlank surfaces (answerSurface / seeContext) for the current blank. */
  fillSurfaces: (ctx: FillContext) => ModeSurfaces;
  /** Builds the Reveal surface (revealPresentation) for the finished story. */
  revealSurfaces: (ctx: RevealContext) => ModeSurfaces;
}

/** Free-text modes all draw from the same family-safe template selection. */
const selectFamilySafe = (library: readonly Template[], familySafeOn: boolean): Template[] =>
  selectTemplates(library, familySafeOn);

/**
 * All four modes, in picker order. Classic blind is first so it is the default
 * selection (single-player/02 AC-01/AC-06): the existing zero-choice flow keeps
 * working with one tap on Start.
 */
export const GAME_MODES: readonly GameMode[] = [
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
 * Looks up a mode by its ModeConfig id, falling back to Classic blind (the
 * first/default entry) for an unknown id rather than returning undefined - a
 * picker can never land on "no mode," so callers never have to guard it. This
 * also means an out-of-sync `round.mode` from the wire resolves to a safe,
 * renderable default instead of crashing the round screen.
 */
export function findMode(id: string): GameMode {
  return GAME_MODES.find((mode) => mode.config.id === id) ?? GAME_MODES[0];
}

/** The default mode (Classic blind) - a picker's initial selection. */
export const DEFAULT_MODE: GameMode = GAME_MODES[0];

/**
 * The mode ids the GROUP picker offers (group-play/05, AC-04): the three that
 * ride the existing distribute -> collect -> broadcast-reveal loop with NO new
 * real-time surface. Progressive Story is deliberately absent (AC-05, see the
 * file header) and the SERVER enforces the same set (GameModeCatalog.Offered) -
 * keep the two lists in sync by hand.
 */
export const GROUP_MODE_IDS: readonly string[] = ['classic-blind', 'word-bank', 'progressive-reveal'];

/**
 * The modes the group host may pick, in picker order (a subset of GAME_MODES,
 * so the card visuals + eligibility + surfaces are all shared with solo, never
 * re-specified). Derived from GAME_MODES by GROUP_MODE_IDS so a new offered mode
 * is a one-line change and the order stays consistent with solo.
 */
export const GROUP_MODES: readonly GameMode[] = GAME_MODES.filter((mode) =>
  GROUP_MODE_IDS.includes(mode.config.id),
);

/** The group picker's default selection (Classic blind) - one tap on Start still works. */
export const DEFAULT_GROUP_MODE: GameMode = findMode('classic-blind');
