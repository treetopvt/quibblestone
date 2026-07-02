// ----------------------------------------------------------------------------
//  modeRegistry.test.ts - Vitest coverage for the GROUP subset of the shared
//  mode registry (group-play/05). See ./modeRegistry.ts.
//
//  The shared registry's per-mode mechanics (config pairing, surface slots,
//  eligibleTemplates gating, findMode fallback) are already proven by
//  soloModes.test.ts, which exercises the SAME objects through the solo-named
//  re-exports. This file only proves the NEW group-specific facts: which modes
//  the group host may pick (AC-04) and that Progressive Story is deferred (AC-05),
//  and that the group set is a genuine SUBSET of the solo set (so its card
//  visuals + surfaces + eligibility are shared, never re-specified - AC-01/AC-08).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import {
  DEFAULT_GROUP_MODE,
  GAME_MODES,
  GROUP_MODES,
  GROUP_MODE_IDS,
  findMode,
} from './modeRegistry';

describe('GROUP_MODES (the modes the group host may pick)', () => {
  it('offers exactly Classic Blind, Word Bank, and Progressive Reveal, in that order (AC-04)', () => {
    expect(GROUP_MODES.map((mode) => mode.config.id)).toEqual([
      'classic-blind',
      'word-bank',
      'progressive-reveal',
    ]);
  });

  it('does NOT offer Progressive Story - it is deferred for group play (AC-05)', () => {
    expect(GROUP_MODES.map((mode) => mode.config.id)).not.toContain('progressive-story');
    expect(GROUP_MODE_IDS).not.toContain('progressive-story');
  });

  it('is a genuine SUBSET of the solo set - the SAME objects, so surfaces + eligibility are shared (AC-01/AC-08)', () => {
    for (const mode of GROUP_MODES) {
      // Reference-equal to the entry in GAME_MODES (not a copy), so a group round
      // renders byte-for-byte the same card + surfaces as solo for that mode.
      expect(GAME_MODES).toContain(mode);
    }
    // The group set is strictly smaller than the full set (Progressive Story dropped).
    expect(GROUP_MODES.length).toBe(GAME_MODES.length - 1);
  });

  it('defaults the group picker to Classic Blind (one tap on Start still works)', () => {
    expect(DEFAULT_GROUP_MODE.config.id).toBe('classic-blind');
    expect(GROUP_MODES[0]).toBe(DEFAULT_GROUP_MODE);
  });

  it('resolves a group mode id through the shared findMode, falling back for a drifted id', () => {
    expect(findMode('word-bank').config.id).toBe('word-bank');
    // An out-of-sync id (e.g. a wire mode the client does not know) resolves to the
    // safe Classic Blind default rather than undefined, so GroupRound never crashes.
    expect(findMode('progressive-story').config.id).toBe('progressive-story'); // still a known solo mode
    expect(findMode('no-such-mode').config.id).toBe('classic-blind');
  });
});
