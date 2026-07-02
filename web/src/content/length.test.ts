// ----------------------------------------------------------------------------
//  length.test.ts - Vitest coverage for the story-length content stage
//  (story-selection/01, see ./length.ts).
//
//  Builds a small in-test mix of quick and full templates (by blank count,
//  around the QUICK_MAX_BLANKS boundary) rather than importing seedLibrary, so
//  the classification/at-threshold cases are pinned by construction and do not
//  drift when seed content changes. Asserts the pure behavior: classification
//  at / below / above the threshold, filtering per preference, that the input is
//  never mutated, and the empty-pool fallback degrading to the family-safe pool
//  (AC-06).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { blank, text, type Template, type TemplateSegment } from '../engine/template';
import {
  QUICK_MAX_BLANKS,
  classifyLength,
  selectByLength,
  selectByLengthOrFallback,
} from './length';

// Build a template with EXACTLY `blankCount` blanks (and some interleaved text),
// so its derived length class is fully controlled by the count alone.
function makeTemplate(id: string, blankCount: number): Template {
  const body: TemplateSegment[] = [text('Start ')];
  for (let i = 0; i < blankCount; i += 1) {
    body.push(
      blank({
        id: `b${i + 1}`,
        category: 'noun',
        categoryLabel: 'NOUN',
        prompt: 'Give me a thing',
        subHint: 'Any object.',
        sparkWords: ['a', 'b', 'c'],
      }),
    );
    body.push(text(' end. '));
  }
  return {
    id,
    title: `Test template ${id}`,
    tags: { familySafe: true, ageRating: 'all-ages', themes: ['test'] },
    body,
  };
}

describe('classifyLength', () => {
  it('classifies a template AT the threshold as quick', () => {
    expect(classifyLength(makeTemplate('at', QUICK_MAX_BLANKS))).toBe('quick');
  });

  it('classifies a template BELOW the threshold as quick', () => {
    expect(classifyLength(makeTemplate('below', QUICK_MAX_BLANKS - 2))).toBe('quick');
  });

  it('classifies a template ABOVE the threshold as full', () => {
    expect(classifyLength(makeTemplate('above', QUICK_MAX_BLANKS + 1))).toBe('full');
  });
});

describe('selectByLength', () => {
  const quickA = makeTemplate('quick-a', 4);
  const quickB = makeTemplate('quick-b', QUICK_MAX_BLANKS);
  const fullA = makeTemplate('full-a', QUICK_MAX_BLANKS + 1);
  const fullB = makeTemplate('full-b', 10);
  const mixed: readonly Template[] = [quickA, fullA, quickB, fullB];

  it("with 'quick', returns only quick templates", () => {
    const result = selectByLength(mixed, 'quick');
    expect(result.map((t) => t.id)).toEqual(['quick-a', 'quick-b']);
    expect(result.every((t) => classifyLength(t) === 'quick')).toBe(true);
  });

  it("with 'full', returns only full templates", () => {
    const result = selectByLength(mixed, 'full');
    expect(result.map((t) => t.id)).toEqual(['full-a', 'full-b']);
    expect(result.every((t) => classifyLength(t) === 'full')).toBe(true);
  });

  it("with 'any', returns the input unfiltered", () => {
    const result = selectByLength(mixed, 'any');
    expect(result.map((t) => t.id)).toEqual(['quick-a', 'full-a', 'quick-b', 'full-b']);
  });

  it("with 'any', returns a shallow copy, not the same array reference", () => {
    const result = selectByLength(mixed, 'any');
    expect(result).not.toBe(mixed);
    expect(result).toEqual(mixed);
  });

  it('never mutates the input array, for any preference', () => {
    const input: readonly Template[] = [quickA, fullA, quickB, fullB];
    const snapshotIds = input.map((t) => t.id);
    selectByLength(input, 'quick');
    selectByLength(input, 'full');
    selectByLength(input, 'any');
    expect(input.map((t) => t.id)).toEqual(snapshotIds);
  });

  it('returns an empty array for empty input, for any preference', () => {
    expect(selectByLength([], 'quick')).toEqual([]);
    expect(selectByLength([], 'full')).toEqual([]);
    expect(selectByLength([], 'any')).toEqual([]);
  });
});

describe('selectByLengthOrFallback (AC-06)', () => {
  const fullOnly: readonly Template[] = [
    makeTemplate('full-a', QUICK_MAX_BLANKS + 1),
    makeTemplate('full-b', 10),
  ];

  it('falls back to the family-safe pool when the length filter is empty', () => {
    // 'quick' matches nothing in a full-only pool - must degrade to the pool
    // itself rather than return empty (never fail the round).
    const result = selectByLengthOrFallback(fullOnly, 'quick');
    expect(result.map((t) => t.id)).toEqual(['full-a', 'full-b']);
  });

  it('returns the filtered pool when the length filter matches at least one', () => {
    const mixed: readonly Template[] = [
      makeTemplate('quick-a', 4),
      makeTemplate('full-a', 10),
    ];
    const result = selectByLengthOrFallback(mixed, 'quick');
    expect(result.map((t) => t.id)).toEqual(['quick-a']);
  });

  it("with 'any', returns the whole family-safe pool unchanged (shallow copy)", () => {
    const result = selectByLengthOrFallback(fullOnly, 'any');
    expect(result).not.toBe(fullOnly);
    expect(result.map((t) => t.id)).toEqual(['full-a', 'full-b']);
  });

  it('never mutates the family-safe pool', () => {
    const input: readonly Template[] = [makeTemplate('full-a', 10)];
    const snapshotIds = input.map((t) => t.id);
    selectByLengthOrFallback(input, 'quick');
    expect(input.map((t) => t.id)).toEqual(snapshotIds);
  });
});
