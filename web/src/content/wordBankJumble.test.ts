// ----------------------------------------------------------------------------
//  wordBankJumble.test.ts - Vitest coverage for the free, deterministic Fresh
//  Runes reshuffle (game-modes/07, AC-02). See ./wordBankJumble.ts.
//
//  Builds a small in-test category pool rather than reusing seedLibrary so the
//  "fresh subset, favor not-just-shown, cycle when exhausted" behavior is
//  exercised against a known, deterministic word list.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import type { BlankCategory, WordBankEntry } from '../engine/template';
import { DEFAULT_OFFERING_SIZE, nextOptions } from './wordBankJumble';

// A 12-word "noun" pool plus a couple of off-category entries the jumble must
// ignore (AC-02 re-samples the SAME category only).
const pool: readonly WordBankEntry[] = [
  ...['stone', 'rune', 'ember', 'frost', 'moss', 'gale', 'thorn', 'quartz', 'cinder', 'brook', 'birch', 'flint'].map(
    (word): WordBankEntry => ({ category: 'noun', word }),
  ),
  { category: 'verb', word: 'yodel' },
  { category: 'adjective', word: 'squishy' },
];

describe('nextOptions', () => {
  it('offers only in-category words (AC-02)', () => {
    const result = nextOptions(pool, 'noun', [], 4);
    expect(result).toEqual(['stone', 'rune', 'ember', 'frost']);
    expect(result).not.toContain('yodel');
    expect(result).not.toContain('squishy');
  });

  it('is deterministic - the same inputs always yield the same subset', () => {
    const a = nextOptions(pool, 'noun', ['stone', 'rune'], 4);
    const b = nextOptions(pool, 'noun', ['stone', 'rune'], 4);
    expect(a).toEqual(b);
  });

  it('returns a DIFFERENT subset that favors words not just shown (AC-02)', () => {
    const first = nextOptions(pool, 'noun', [], 4);
    const second = nextOptions(pool, 'noun', first, 4);
    // Every word in the second set is fresh (none was in the first set).
    expect(second.some((w) => first.includes(w))).toBe(false);
    expect(second).toEqual(['moss', 'gale', 'thorn', 'quartz']);
  });

  it('walks the whole pool before repeating (cumulative shown)', () => {
    const seen = new Set<string>();
    let shown: string[] = [];
    // 12 words, 4 at a time -> 3 taps cover the whole pool with no repeat.
    for (let tap = 0; tap < 3; tap += 1) {
      const set = nextOptions(pool, 'noun', shown, 4);
      set.forEach((w) => seen.add(w));
      shown = [...shown, ...set];
    }
    expect(seen.size).toBe(12);
  });

  it('cycles gracefully (never empty) once the pool is exhausted (AC-02)', () => {
    const everyWord = pool.filter((e) => e.category === 'noun').map((e) => e.word);
    const result = nextOptions(pool, 'noun', everyWord, 4);
    // All shown already -> a full, non-empty set from the pool head, not [].
    expect(result).toHaveLength(4);
    expect(result).toEqual(['stone', 'rune', 'ember', 'frost']);
  });

  it('fills a partial-fresh set from the pool head rather than dwindling', () => {
    // Show all but the last two -> only 2 fresh remain, but a size-4 set is asked.
    const nouns = pool.filter((e) => e.category === 'noun').map((e) => e.word);
    const shown = nouns.slice(0, 10); // leaves 'birch', 'flint' fresh
    const result = nextOptions(pool, 'noun', shown, 4);
    expect(result).toHaveLength(4);
    // Fresh words come first, then the cycle refills from the head.
    expect(result.slice(0, 2)).toEqual(['birch', 'flint']);
    expect(result.slice(2)).toEqual(['stone', 'rune']);
  });

  it('never offers more than the pool holds (small category, AC-02)', () => {
    const tiny: readonly WordBankEntry[] = [
      { category: 'noun', word: 'stone' },
      { category: 'noun', word: 'rune' },
    ];
    expect(nextOptions(tiny, 'noun', [], 8)).toEqual(['stone', 'rune']);
  });

  it('de-duplicates repeated words in the pool (case-insensitively)', () => {
    const dupes: readonly WordBankEntry[] = [
      { category: 'noun', word: 'stone' },
      { category: 'noun', word: 'Stone' },
      { category: 'noun', word: 'rune' },
    ];
    expect(nextOptions(dupes, 'noun', [], 8)).toEqual(['stone', 'rune']);
  });

  it('returns an empty array for an empty or mismatched-category pool (AC-02, no throw)', () => {
    expect(nextOptions([], 'noun', [])).toEqual([]);
    expect(nextOptions(pool, 'pronoun' as BlankCategory, [])).toEqual([]);
  });

  it('defaults to DEFAULT_OFFERING_SIZE when no size is given', () => {
    const result = nextOptions(pool, 'noun', []);
    expect(result).toHaveLength(Math.min(DEFAULT_OFFERING_SIZE, 12));
  });
});
