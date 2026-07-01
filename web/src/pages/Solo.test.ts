// ----------------------------------------------------------------------------
//  Solo.test.ts - Vitest coverage for Solo.tsx's one pure helper,
//  pickRandomTemplate (single-player/01, see ./Solo.tsx), plus (story-selection/02)
//  the solo pick's content-selection COMPOSITION: family-safe gate then length
//  stage, safety first.
//
//  Solo.tsx itself is a screen with no render harness (per the story's
//  self-check note - no RTL tests here); this file only exercises the pure,
//  exported random-pick helper (and, below, the same selectTemplates ->
//  selectByLengthOrFallback composition Solo.tsx's handleStart/handlePlayAgain
//  use) so the "no templates -> undefined" guard and the "always returns one
//  of the given templates" behavior are covered without needing to mount React.
// ----------------------------------------------------------------------------

import { describe, expect, it, vi } from 'vitest';
import { blank, getBlanks, text, type Template } from '../engine/template';
import { selectTemplates } from '../content/familySafe';
import { QUICK_MAX_BLANKS, selectByLengthOrFallback } from '../content/length';
import { seedLibrary } from '../content/seedLibrary';
import { pickRandomTemplate } from './Solo';

function makeTemplate(id: string): Template {
  return {
    id,
    title: `Test template ${id}`,
    tags: { familySafe: true, ageRating: 'all-ages', themes: ['test'] },
    body: [
      text('A '),
      blank({
        id: 'b1',
        category: 'adjective',
        categoryLabel: 'ADJECTIVE',
        prompt: 'Give me a word',
        subHint: 'Any word.',
        sparkWords: ['a', 'b', 'c'],
      }),
      text(' story.'),
    ],
  };
}

describe('pickRandomTemplate', () => {
  it('returns undefined for an empty list', () => {
    expect(pickRandomTemplate([])).toBeUndefined();
  });

  it('returns the only template when given exactly one', () => {
    const only = makeTemplate('only');
    expect(pickRandomTemplate([only])).toBe(only);
  });

  it('always returns one of the given templates', () => {
    const templates = [makeTemplate('a'), makeTemplate('b'), makeTemplate('c')];
    for (let i = 0; i < 20; i += 1) {
      const picked = pickRandomTemplate(templates);
      expect(templates).toContain(picked);
    }
  });

  it('uses Math.random to select an index across the full range', () => {
    const templates = [makeTemplate('a'), makeTemplate('b'), makeTemplate('c')];
    const randomSpy = vi.spyOn(Math, 'random');

    randomSpy.mockReturnValueOnce(0);
    expect(pickRandomTemplate(templates)).toBe(templates[0]);

    randomSpy.mockReturnValueOnce(0.9999);
    expect(pickRandomTemplate(templates)).toBe(templates[2]);

    randomSpy.mockRestore();
  });
});

// story-selection/02: the solo pick composes selectTemplates (family-safe
// gate) then selectByLengthOrFallback (length stage), safety FIRST (AC-05),
// exactly as handleStart/handlePlayAgain in Solo.tsx do. This exercises that
// composition directly against the real bundled seedLibrary (not a hand-built
// fixture) so a drift in the seed content's blank counts would surface here.
describe("Solo's family-safe -> length pick composition", () => {
  it("with lengthPref 'quick', every template in the pool classifies as quick", () => {
    const pool = selectByLengthOrFallback(selectTemplates(seedLibrary, true), 'quick');
    expect(pool.length).toBeGreaterThan(0);
    for (const template of pool) {
      expect(getBlanks(template).length).toBeLessThanOrEqual(QUICK_MAX_BLANKS);
    }
  });

  it("with lengthPref 'full', every template in the pool classifies as full", () => {
    const pool = selectByLengthOrFallback(selectTemplates(seedLibrary, true), 'full');
    expect(pool.length).toBeGreaterThan(0);
    for (const template of pool) {
      expect(getBlanks(template).length).toBeGreaterThan(QUICK_MAX_BLANKS);
    }
  });

  it('every template returned is still family-safe (the gate never weakens, AC-05)', () => {
    const quickPool = selectByLengthOrFallback(selectTemplates(seedLibrary, true), 'quick');
    const fullPool = selectByLengthOrFallback(selectTemplates(seedLibrary, true), 'full');
    for (const template of [...quickPool, ...fullPool]) {
      expect(template.tags.familySafe).toBe(true);
    }
  });
});
