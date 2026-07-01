// ----------------------------------------------------------------------------
//  familySafe.test.ts - Vitest coverage for the family-safe content gate
//  (child-safety/02, see ./familySafe.ts).
//
//  Builds a small in-test mix of family-safe and non-family-safe templates
//  (deliberately NOT importing seedLibrary, since every seed template is
//  currently familySafe:true and would not exercise the false branch) and
//  asserts the pure predicate/selection behavior: isFamilySafe reads the tag
//  correctly, and selectTemplates honors the toggle position without
//  mutating its input.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { blank, text, type Template } from '../engine/template';
import { isFamilySafe, selectTemplates } from './familySafe';

function makeTemplate(id: string, familySafe: boolean): Template {
  return {
    id,
    title: `Test template ${id}`,
    tags: { familySafe, ageRating: familySafe ? 'all-ages' : 'teen-plus', themes: ['test'] },
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

describe('isFamilySafe', () => {
  it('returns true for a template tagged familySafe: true', () => {
    expect(isFamilySafe(makeTemplate('safe-1', true))).toBe(true);
  });

  it('returns false for a template tagged familySafe: false', () => {
    expect(isFamilySafe(makeTemplate('unsafe-1', false))).toBe(false);
  });
});

describe('selectTemplates', () => {
  const mixed: readonly Template[] = [
    makeTemplate('safe-1', true),
    makeTemplate('unsafe-1', false),
    makeTemplate('safe-2', true),
    makeTemplate('unsafe-2', false),
  ];

  it('with familySafeOn=true, returns only the family-safe templates', () => {
    const result = selectTemplates(mixed, true);
    expect(result.map((t) => t.id)).toEqual(['safe-1', 'safe-2']);
    expect(result.every(isFamilySafe)).toBe(true);
  });

  it('with familySafeOn=false, returns all templates unfiltered', () => {
    const result = selectTemplates(mixed, false);
    expect(result.map((t) => t.id)).toEqual(['safe-1', 'unsafe-1', 'safe-2', 'unsafe-2']);
  });

  it('returns a shallow copy, not the same array reference, when off', () => {
    const result = selectTemplates(mixed, false);
    expect(result).not.toBe(mixed);
    expect(result).toEqual(mixed);
  });

  it('returns an empty array for empty input, regardless of toggle position', () => {
    expect(selectTemplates([], true)).toEqual([]);
    expect(selectTemplates([], false)).toEqual([]);
  });
});
