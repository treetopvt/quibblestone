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
import { classifyLength } from './length';
import { seedLibrary } from './seedLibrary';

describe('seedLibrary', () => {
  it('has between 10 and 120 templates', () => {
    expect(seedLibrary.length).toBeGreaterThanOrEqual(10);
    expect(seedLibrary.length).toBeLessThanOrEqual(120);
  });

  it('carries at least 4 quick templates (4-6 blanks) for story-selection', () => {
    const quick = seedLibrary.filter((t) => classifyLength(t) === 'quick');
    expect(quick.length).toBeGreaterThanOrEqual(4);
    for (const template of quick) {
      const count = getBlanks(template).length;
      expect(count, `${template.id} quick blank count`).toBeGreaterThanOrEqual(4);
      expect(count, `${template.id} quick blank count`).toBeLessThanOrEqual(6);
    }
  });

  it('has unique template ids', () => {
    const ids = seedLibrary.map((t) => t.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('pairs each template family-safe flag with a consistent age rating', () => {
    // Two content tiers now ship: the family-safe set (surfaced whenever the
    // family-safe toggle is on) and a non-family-safe "toggle off" set. The gate
    // (familySafe.ts / server FamilySafeContentSelector) acts ONLY on
    // tags.familySafe, but we still keep ageRating consistent with it so the
    // metadata never lies: family-safe => all-ages, non-family-safe => teen-plus.
    for (const template of seedLibrary) {
      if (template.tags.familySafe) {
        expect(template.tags.ageRating, `${template.id} (family-safe) must be all-ages`).toBe('all-ages');
      } else {
        expect(template.tags.ageRating, `${template.id} (not family-safe) must be teen-plus`).toBe('teen-plus');
      }
    }
  });

  it('ships both a family-safe set and a non-family-safe (toggle-off) set', () => {
    // The family-safe toggle only does something visible when the library
    // actually carries content on BOTH sides of the gate: a generous family-safe
    // set for the default-on posture, and a non-family-safe set that appears only
    // when a host turns the toggle off (selectTemplates(..., false)).
    const safe = seedLibrary.filter((t) => t.tags.familySafe);
    const adult = seedLibrary.filter((t) => !t.tags.familySafe);
    expect(safe.length, 'family-safe templates').toBeGreaterThanOrEqual(20);
    expect(adult.length, 'non-family-safe (toggle-off) templates').toBeGreaterThanOrEqual(8);
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
