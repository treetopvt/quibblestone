// ----------------------------------------------------------------------------
//  assemble.test.ts - Vitest spec for the pure assemble() function (AC-06).
//
//  This is the minimal seed harness's primary payload: it verifies
//  determinism, in-order replacement, per-word attribution, and the
//  documented word-count-mismatch rule (see the header comment in
//  assemble.ts for the rule itself). platform-devops/01 owns the canonical
//  test harness later; until then, run via `npm run test:unit`.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { assemble, type SubmittedWord } from './assemble';
import { blank, text, type Template } from './template';

/** A small hand-authored template with 3 ordered blanks, used across the spec. */
function makeTemplate(): Template {
  return {
    id: 'wobbly-wizard',
    title: 'The Wobbly Wizard & the Golden Sock',
    tags: { familySafe: true, ageRating: 'all-ages', themes: ['fantasy'] },
    body: [
      text('Once upon a time, a '),
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
        prompt: 'Give me a thing',
        subHint: 'Any object will do.',
        sparkWords: ['sock', 'hat', 'spoon'],
      }),
      text(' and decided to '),
      blank({
        id: 'blank-3',
        category: 'verb',
        categoryLabel: 'VERB',
        prompt: 'Give me an action word',
        subHint: 'Something you can do.',
        sparkWords: ['dance', 'jump', 'sing'],
      }),
      text('.'),
    ],
  };
}

describe('assemble', () => {
  it('replaces blanks in order to produce the final story text', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
      { playerSessionId: 'p1', word: 'dance' },
    ];

    const result = assemble(template, words);

    expect(result.storyText).toBe(
      'Once upon a time, a gigantic wizard found a sock and decided to dance.',
    );
  });

  it('is deterministic: same template + same words -> same output', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'squishy' },
      { playerSessionId: 'p2', word: 'hat' },
      { playerSessionId: 'p3', word: 'jump' },
    ];

    const first = assemble(template, words);
    const second = assemble(template, words);

    expect(second.storyText).toBe(first.storyText);
    expect(second.filledWords).toEqual(first.filledWords);
  });

  it('preserves per-word attribution (playerSessionId + word) for each filled blank', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'session-a', word: 'sparkly' },
      { playerSessionId: 'session-b', word: 'spoon' },
      { playerSessionId: 'session-a', word: 'sing' },
    ];

    const result = assemble(template, words);

    expect(result.filledWords).toEqual([
      { blankId: 'blank-1', word: 'sparkly', playerSessionId: 'session-a' },
      { blankId: 'blank-2', word: 'spoon', playerSessionId: 'session-b' },
      { blankId: 'blank-3', word: 'sing', playerSessionId: 'session-a' },
    ]);
  });

  it('fills trailing unfilled blanks with empty string and no attribution when fewer words than blanks are supplied', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [{ playerSessionId: 'p1', word: 'gigantic' }];

    const result = assemble(template, words);

    expect(result.storyText).toBe('Once upon a time, a gigantic wizard found a  and decided to .');
    expect(result.filledWords).toEqual([
      { blankId: 'blank-1', word: 'gigantic', playerSessionId: 'p1' },
      { blankId: 'blank-2', word: '', playerSessionId: undefined },
      { blankId: 'blank-3', word: '', playerSessionId: undefined },
    ]);
  });

  it('ignores extra words beyond the blank count when more words than blanks are supplied', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
      { playerSessionId: 'p3', word: 'dance' },
      { playerSessionId: 'p4', word: 'extra-ignored-word' },
    ];

    const result = assemble(template, words);

    expect(result.storyText).toBe(
      'Once upon a time, a gigantic wizard found a sock and decided to dance.',
    );
    expect(result.filledWords).toHaveLength(3);
  });

  it('carries the template id and title through to the assembled result', () => {
    const template = makeTemplate();

    const result = assemble(template, []);

    expect(result.templateId).toBe('wobbly-wizard');
    expect(result.title).toBe('The Wobbly Wizard & the Golden Sock');
  });
});
