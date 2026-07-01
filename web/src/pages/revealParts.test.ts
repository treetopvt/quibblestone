// ----------------------------------------------------------------------------
//  revealParts.test.ts - Vitest spec for buildRevealParts() (the-reveal/01,
//  AC-01/AC-02 highlight-correctness core).
//
//  This is the prime unit-test target for the Reveal screen: it verifies that
//  literal text and filled words interleave in the exact template body order,
//  that attribution survives the reshape, and that the helper never throws on
//  a partial assembly (mirrors assemble()'s own non-throwing mismatch rule).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { assemble, type SubmittedWord } from '../engine/assemble';
import { blank, text, type Template } from '../engine/template';
import { buildRevealParts } from './revealParts';

/** A small hand-authored template with 2 ordered blanks, used across the spec. */
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
      text('.'),
    ],
  };
}

describe('buildRevealParts', () => {
  it('interleaves literal text and filled words in body order', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
    ];
    const assembled = assemble(template, words);

    const parts = buildRevealParts(template, assembled);

    expect(parts).toEqual([
      { kind: 'text', text: 'Once upon a time, a ' },
      { kind: 'word', word: 'gigantic', blankId: 'blank-1', playerSessionId: 'p1' },
      { kind: 'text', text: ' wizard found a ' },
      { kind: 'word', word: 'sock', blankId: 'blank-2', playerSessionId: 'p2' },
      { kind: 'text', text: '.' },
    ]);
  });

  it('preserves per-word attribution (playerSessionId) for each highlighted word', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'session-a', word: 'sparkly' },
      { playerSessionId: 'session-b', word: 'hat' },
    ];
    const assembled = assemble(template, words);

    const parts = buildRevealParts(template, assembled);
    const wordParts = parts.filter((p): p is Extract<typeof p, { kind: 'word' }> => p.kind === 'word');

    expect(wordParts).toEqual([
      { kind: 'word', word: 'sparkly', blankId: 'blank-1', playerSessionId: 'session-a' },
      { kind: 'word', word: 'hat', blankId: 'blank-2', playerSessionId: 'session-b' },
    ]);
  });

  it('renders an empty-string word (no attribution) when fewer words than blanks were supplied', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [{ playerSessionId: 'p1', word: 'gigantic' }];
    const assembled = assemble(template, words);

    const parts = buildRevealParts(template, assembled);

    expect(parts).toEqual([
      { kind: 'text', text: 'Once upon a time, a ' },
      { kind: 'word', word: 'gigantic', blankId: 'blank-1', playerSessionId: 'p1' },
      { kind: 'text', text: ' wizard found a ' },
      { kind: 'word', word: '', blankId: 'blank-2', playerSessionId: undefined },
      { kind: 'text', text: '.' },
    ]);
  });

  it('is deterministic: same template + same assembled story -> same parts', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'squishy' },
      { playerSessionId: 'p2', word: 'spoon' },
    ];
    const assembled = assemble(template, words);

    const first = buildRevealParts(template, assembled);
    const second = buildRevealParts(template, assembled);

    expect(second).toEqual(first);
  });

  it('never throws given a template with no blanks (pure text body)', () => {
    const template: Template = {
      id: 'plain',
      title: 'Plain Tale',
      tags: { familySafe: true, ageRating: 'all-ages', themes: [] },
      body: [text('Nothing to fill in here.')],
    };
    const assembled = assemble(template, []);

    const parts = buildRevealParts(template, assembled);

    expect(parts).toEqual([{ kind: 'text', text: 'Nothing to fill in here.' }]);
  });
});
