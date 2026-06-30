// ----------------------------------------------------------------------------
//  template.test.ts - Vitest spec for the template schema helpers
//  (getBlanks, the text/blank segment constructors). Co-located with
//  assemble.test.ts as part of the minimal seed harness (see vitest.config.ts
//  header for why this seed exists vs. the canonical platform-devops/01 one).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { blank, getBlanks, text, type Template } from './template';

describe('getBlanks', () => {
  it('returns only the blank segments, in body order, skipping literal text', () => {
    const template: Template = {
      id: 'sample',
      title: 'Sample',
      tags: { familySafe: true, ageRating: 'all-ages', themes: [] },
      body: [
        text('The '),
        blank({
          id: 'b1',
          category: 'adjective',
          categoryLabel: 'ADJECTIVE',
          prompt: 'Give me a silly describing word',
          subHint: 'Anything goes!',
          sparkWords: ['squishy', 'gigantic', 'sparkly'],
        }),
        text(' cat sat on the '),
        blank({
          id: 'b2',
          category: 'noun',
          categoryLabel: 'NOUN',
          prompt: 'Give me a thing',
          subHint: 'Any object.',
          sparkWords: ['mat', 'rug', 'log'],
        }),
        text('.'),
      ],
    };

    const blanks = getBlanks(template);

    expect(blanks.map((b) => b.id)).toEqual(['b1', 'b2']);
    expect(blanks[0].sparkWords).toHaveLength(3);
  });

  it('returns an empty array for a template with no blanks', () => {
    const template: Template = {
      id: 'no-blanks',
      title: 'No Blanks',
      tags: { familySafe: true, ageRating: 'all-ages', themes: [] },
      body: [text('Just literal text, no blanks here.')],
    };

    expect(getBlanks(template)).toEqual([]);
  });

  it('supports an optional wordBank and templates without one are still valid (AC-03)', () => {
    const withBank: Template = {
      id: 'with-bank',
      title: 'With Bank',
      tags: { familySafe: true, ageRating: 'all-ages', themes: [] },
      body: [text('hello')],
      wordBank: [{ category: 'adjective', word: 'squishy' }],
    };
    const withoutBank: Template = {
      id: 'without-bank',
      title: 'Without Bank',
      tags: { familySafe: true, ageRating: 'all-ages', themes: [] },
      body: [text('hello')],
    };

    expect(withBank.wordBank).toHaveLength(1);
    expect(withoutBank.wordBank).toBeUndefined();
  });
});
