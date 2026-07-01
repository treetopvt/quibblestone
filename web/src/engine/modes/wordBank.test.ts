// ----------------------------------------------------------------------------
//  wordBank.test.ts - Vitest spec for the Word Bank ModeConfig
//  (game-modes/04, AC-03, AC-04, AC-07).
//
//  Asserts two things:
//    1. The mode is a plain ModeConfig literal over the documented axes
//       (see: 'subject-only', answer: 'word-bank', reveal: 'at-end'), with a
//       stable id/label - AC-07.
//    2. Submitting a word-bank answer through engine.ts's `collectWord` (the
//       SAME collection path every mode uses) records it, and does so WITHOUT
//       ever invoking a supplied `SafetyCheck` hook - proving the documented
//       seam end to end (AC-03, AC-04) rather than merely asserting it by
//       inspection.
// ----------------------------------------------------------------------------

import { describe, expect, it, vi } from 'vitest';
import { collectWord, createCollection, type SafetyCheck } from '../engine';
import { blank, text, type Template } from '../template';
import { wordBank } from './wordBank';

function makeTemplate(): Template {
  return {
    id: 'road-trip-disaster',
    title: 'The Great Family Road Trip Disaster',
    tags: { familySafe: true, ageRating: 'all-ages', themes: ['road-trip'] },
    body: [
      text('We packed '),
      blank({
        id: 'b1',
        category: 'plural-noun',
        categoryLabel: 'PLURAL NOUN',
        prompt: 'Give me more than one of a thing',
        subHint: 'Plural objects, e.g. "snacks".',
        sparkWords: ['pretzels', 'rubber ducks', 'socks'],
      }),
      text(' and drove.'),
    ],
    wordBank: [
      { category: 'plural-noun', word: 'pretzels' },
      { category: 'plural-noun', word: 'rubber ducks' },
    ],
  };
}

describe('wordBank ModeConfig', () => {
  it('is expressed as a plain ModeConfig literal over the documented axes (AC-07)', () => {
    expect(wordBank).toEqual({
      id: 'word-bank',
      label: 'Word Bank',
      see: 'subject-only',
      answer: 'word-bank',
      reveal: 'at-end',
    });
  });
});

describe('collectWord with the word-bank mode', () => {
  it('records a tapped word via the standard collection path (AC-03)', async () => {
    const template = makeTemplate();
    const collected = createCollection();

    const result = await collectWord(collected, template, wordBank, 'b1', {
      playerSessionId: 'p1',
      word: 'pretzels',
    });

    expect(result).toEqual({ accepted: true });
    expect(collected.get('b1')).toEqual({ playerSessionId: 'p1', word: 'pretzels' });
  });

  it('never invokes a supplied SafetyCheck hook for a word-bank submission (AC-04)', async () => {
    const template = makeTemplate();
    const collected = createCollection();
    const safetyCheck: SafetyCheck = vi.fn(async () => ({ ok: true }));

    const result = await collectWord(
      collected,
      template,
      wordBank,
      'b1',
      { playerSessionId: 'p1', word: 'pretzels' },
      safetyCheck,
    );

    expect(result).toEqual({ accepted: true });
    expect(safetyCheck).not.toHaveBeenCalled();
  });
});
