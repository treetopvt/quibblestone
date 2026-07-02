// ----------------------------------------------------------------------------
//  seedLibrary.test.ts - Vitest spec proving the seed library (AC-02) is
//  shaped correctly and vetted to the family-safe bar (AC-03). Co-located
//  with seedLibrary.ts per the minimal test seed harness convention
//  (template-model/01's vitest.config.ts - include glob src/**/*.test.ts
//  already picks this file up, so nothing else needs to change to run it).
//
//  These checks deliberately do NOT re-test the engine itself (getBlanks,
//  assemble) - those are covered by web/src/engine/*.test.ts. This file only
//  asserts that THIS DATA satisfies the schema's contract and the content
//  quality bar: right library size, every template family-safe, every blank
//  fully authored with exactly 3 spark words, and every template assembles
//  cleanly through the real (imported, not mocked) assembler.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { assemble, type SubmittedWord } from '../engine/assemble';
import { getBlanks } from '../engine/template';
import { seedLibrary } from './seedLibrary';

describe('seedLibrary', () => {
  it('has between 10 and 15 templates', () => {
    expect(seedLibrary.length).toBeGreaterThanOrEqual(10);
    expect(seedLibrary.length).toBeLessThanOrEqual(15);
  });

  it('has unique template ids', () => {
    const ids = seedLibrary.map((t) => t.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('tags every template family-safe and all-ages', () => {
    for (const template of seedLibrary) {
      expect(template.tags.familySafe, `${template.id} must be familySafe`).toBe(true);
      expect(template.tags.ageRating, `${template.id} must be all-ages`).toBe('all-ages');
    }
  });

  it('gives every blank exactly 3 spark words and non-empty copy', () => {
    for (const template of seedLibrary) {
      const blanks = getBlanks(template);
      expect(blanks.length, `${template.id} should have at least one blank`).toBeGreaterThan(0);

      for (const b of blanks) {
        expect(b.sparkWords, `${template.id}/${b.id} sparkWords`).toHaveLength(3);
        for (const word of b.sparkWords) {
          expect(word.trim().length, `${template.id}/${b.id} spark word must be non-empty`).toBeGreaterThan(0);
        }
        expect(b.prompt.trim().length, `${template.id}/${b.id} prompt`).toBeGreaterThan(0);
        expect(b.subHint.trim().length, `${template.id}/${b.id} subHint`).toBeGreaterThan(0);
        expect(b.categoryLabel.trim().length, `${template.id}/${b.id} categoryLabel`).toBeGreaterThan(0);
      }
    }
  });

  it('assembles every template without throwing, using one placeholder word per blank', () => {
    for (const template of seedLibrary) {
      const blanks = getBlanks(template);
      const words: SubmittedWord[] = blanks.map((b, index) => ({
        playerSessionId: 'test-session',
        word: `PLACEHOLDER_${index}_${b.id}`,
      }));

      const result = assemble(template, words);

      expect(result.templateId).toBe(template.id);
      expect(result.title).toBe(template.title);
      for (const w of words) {
        expect(result.storyText, `${template.id} story text should contain "${w.word}"`).toContain(w.word);
      }
    }
  });

  it('keeps at least one template with a wordBank and one without (exercises both AC-03 paths)', () => {
    const withBank = seedLibrary.filter((t) => t.wordBank !== undefined);
    const withoutBank = seedLibrary.filter((t) => t.wordBank === undefined);
    expect(withBank.length).toBeGreaterThan(0);
    expect(withoutBank.length).toBeGreaterThan(0);
  });

  it('gives every word-bank template an entry for EVERY blank category (Word Bank mode never shows an empty tap list)', () => {
    // Word Bank mode filters the bank to the current blank's category
    // (WordBankAnswer.wordsForCategory). If a template is offered a bank but a
    // blank's category has no entry, the player sees an empty list on that
    // blank - which is exactly the "no word bank to choose from" bug this
    // guard prevents (game-modes/04 AC-06, single-player/02 AC-03/AC-04).
    for (const template of seedLibrary) {
      if (template.wordBank === undefined) continue;
      const bankCategories = new Set(template.wordBank.map((entry) => entry.category));
      for (const b of getBlanks(template)) {
        expect(
          bankCategories.has(b.category),
          `${template.id}/${b.id}: Word Bank needs at least one '${b.category}' bank entry`,
        ).toBe(true);
      }
    }
  });
});
