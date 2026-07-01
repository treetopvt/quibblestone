// ----------------------------------------------------------------------------
//  Solo.test.ts - Vitest coverage for Solo.tsx's one pure helper,
//  pickRandomTemplate (single-player/01, see ./Solo.tsx).
//
//  Solo.tsx itself is a screen with no render harness (per the story's
//  self-check note - no RTL tests here); this file only exercises the pure,
//  exported random-pick helper so the "no templates -> undefined" guard and
//  the "always returns one of the given templates" behavior are covered
//  without needing to mount React.
// ----------------------------------------------------------------------------

import { describe, expect, it, vi } from 'vitest';
import { blank, text, type Template } from '../engine/template';
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
