// ----------------------------------------------------------------------------
//  progressiveReveal.test.ts - Vitest spec for the Progressive Reveal
//  ModeConfig and its paired pacing helper (game-modes/06, AC-04).
//
//  Three things worth proving here, all pure (no React render harness exists
//  in this repo yet):
//    1. The config itself is exactly the three axes the story specifies
//       (see='subject-only', answer='free-text', reveal='progressively'),
//       plus stable id/label metadata - mirroring classicBlind.test.ts.
//    2. A light integration through engine.ts's collectWord: passing
//       progressiveReveal + a stub SafetyCheck rejects a failing word (never
//       recorded) and records a passing one - proving the safety seam sits on
//       the mode-agnostic collection path for THIS mode too, and that the
//       reveal's pacing never introduces a second, unfiltered path (AC-05).
//    3. `buildRevealParts` is reused unmodified against a complete
//       AssembledStory, and the pure pacing helper
//       (howManyWordsRevealedAtStep, from ProgressiveRevealPresentation.tsx)
//       reveals words in body order, never exceeds the total word count, and
//       ends at the full set once every step has elapsed (AC-02/AC-03/AC-04).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { progressiveReveal } from './progressiveReveal';
import { collectWord, createCollection, type SafetyCheck } from '../engine';
import { assemble } from '../assemble';
import { blank, text, type Template } from '../template';
import { buildRevealParts } from '../../pages/revealParts';
import { howManyWordsRevealedAtStep } from '../../pages/reveal/ProgressiveRevealPresentation';

const template: Template = {
  id: 'wobbly-wizard',
  title: 'The Wobbly Wizard & the Golden Sock',
  tags: { familySafe: true, ageRating: 'all-ages', themes: ['fantasy'] },
  body: [
    text('A '),
    blank({
      id: 'blank-1',
      category: 'adjective',
      categoryLabel: 'ADJECTIVE',
      prompt: 'Give me a silly describing word',
      subHint: 'Something that describes a thing - anything goes!',
      sparkWords: ['squishy', 'gigantic', 'sparkly'],
    }),
    text(' wizard found a '),
    blank({
      id: 'blank-2',
      category: 'noun',
      categoryLabel: 'NOUN',
      prompt: 'Give me a silly thing',
      subHint: 'Any object will do!',
      sparkWords: ['sock', 'hat', 'spoon'],
    }),
    text('.'),
  ],
};

describe('progressiveReveal', () => {
  it('is expressed as exactly the three Progressive Reveal axis values', () => {
    expect(progressiveReveal.see).toBe('subject-only');
    expect(progressiveReveal.answer).toBe('free-text');
    expect(progressiveReveal.reveal).toBe('progressively');
  });

  it('carries a stable id and human-facing label', () => {
    expect(progressiveReveal.id).toBe('progressive-reveal');
    expect(progressiveReveal.label).toBe('Progressive Reveal');
  });

  it('routes free-text submissions through the injected safety check on the collection path (AC-05)', async () => {
    const safetyCheck: SafetyCheck = async (word) =>
      word === 'naughty' ? { ok: false, message: 'Try a kinder word!' } : { ok: true };

    const collected = createCollection();

    const rejected = await collectWord(
      collected,
      template,
      progressiveReveal,
      'blank-1',
      { playerSessionId: 'p1', word: 'naughty' },
      safetyCheck,
    );
    expect(rejected).toEqual({ accepted: false, message: 'Try a kinder word!' });
    expect(collected.has('blank-1')).toBe(false);

    const accepted = await collectWord(
      collected,
      template,
      progressiveReveal,
      'blank-1',
      { playerSessionId: 'p1', word: 'gigantic' },
      safetyCheck,
    );
    expect(accepted).toEqual({ accepted: true });
    expect(collected.get('blank-1')).toEqual({ playerSessionId: 'p1', word: 'gigantic' });
  });

  it('derives its parts from buildRevealParts, unmodified, against a complete AssembledStory (AC-04)', () => {
    const assembled = assemble(template, [
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
    ]);

    const parts = buildRevealParts(template, assembled);

    expect(parts).toEqual([
      { kind: 'text', text: 'A ' },
      { kind: 'word', word: 'gigantic', blankId: 'blank-1', playerSessionId: 'p1' },
      { kind: 'text', text: ' wizard found a ' },
      { kind: 'word', word: 'sock', blankId: 'blank-2', playerSessionId: 'p2' },
      { kind: 'text', text: '.' },
    ]);
  });

  it('paces words in body order, never exceeding the total, and ends at the full set (AC-02/AC-03)', () => {
    const assembled = assemble(template, [
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
    ]);
    const parts = buildRevealParts(template, assembled);

    expect(howManyWordsRevealedAtStep(parts, -1)).toBe(0);
    expect(howManyWordsRevealedAtStep(parts, 0)).toBe(0);
    expect(howManyWordsRevealedAtStep(parts, 1)).toBe(1);
    expect(howManyWordsRevealedAtStep(parts, 2)).toBe(2);
    // Once every word part has been revealed, further steps do not exceed
    // the total (the final state matches the default reveal exactly).
    expect(howManyWordsRevealedAtStep(parts, 99)).toBe(2);
  });
});
