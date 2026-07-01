// ----------------------------------------------------------------------------
//  WordBankAnswer.test.ts - Vitest spec for the pure category-filter helper
//  (game-modes/04, AC-02). No render harness exists in this repo, so this
//  file exercises ONLY the exported pure helper (`wordsForCategory`), not the
//  React component itself - see WordBankAnswer.tsx's header.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import type { WordBankEntry } from '../../engine/template';
import { wordsForCategory } from './WordBankAnswer';

const wordBank: readonly WordBankEntry[] = [
  { category: 'plural-noun', word: 'pretzels' },
  { category: 'plural-noun', word: 'rubber ducks' },
  { category: 'verb', word: 'yodel' },
  { category: 'verb', word: 'wiggle' },
  { category: 'place', word: 'Pancake City' },
];

describe('wordsForCategory', () => {
  it('returns only entries matching the given category (AC-02)', () => {
    expect(wordsForCategory(wordBank, 'verb')).toEqual([
      { category: 'verb', word: 'yodel' },
      { category: 'verb', word: 'wiggle' },
    ]);
  });

  it('returns an empty array when no entries match the category', () => {
    expect(wordsForCategory(wordBank, 'number')).toEqual([]);
  });

  it('returns an empty array for an empty word bank', () => {
    expect(wordsForCategory([], 'verb')).toEqual([]);
  });
});
