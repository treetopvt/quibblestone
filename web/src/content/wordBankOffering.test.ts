// ----------------------------------------------------------------------------
//  wordBankOffering.test.ts - Vitest coverage for the Word Bank content-
//  selection gate (game-modes/04, AC-05, AC-06). See ./wordBankOffering.ts.
//
//  Builds a small in-test mix of templates (bank-less / family-safe-with-bank
//  / not-family-safe-with-bank) deliberately NOT reusing seedLibrary, since
//  every seed template is family-safe and only two carry a wordBank - that
//  would not exercise the "not family-safe" branch this gate must reject when
//  the toggle is on.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { blank, text, type Template } from '../engine/template';
import { offerWordBankTemplates } from './wordBankOffering';

function makeTemplate(id: string, familySafe: boolean, withBank: boolean): Template {
  return {
    id,
    title: `Test template ${id}`,
    tags: { familySafe, ageRating: familySafe ? 'all-ages' : 'teen-plus', themes: ['test'] },
    body: [
      text('A '),
      blank({
        id: 'b1',
        category: 'verb',
        categoryLabel: 'VERB',
        prompt: 'Give me an action word',
        subHint: 'Something you DO.',
        sparkWords: ['a', 'b', 'c'],
      }),
      text(' story.'),
    ],
    ...(withBank ? { wordBank: [{ category: 'verb' as const, word: 'yodel' }] } : {}),
  };
}

describe('offerWordBankTemplates', () => {
  const mix: readonly Template[] = [
    makeTemplate('safe-with-bank', true, true),
    makeTemplate('safe-no-bank', true, false),
    makeTemplate('unsafe-with-bank', false, true),
    makeTemplate('unsafe-no-bank', false, false),
  ];

  it('with familySafeOn=true, offers only family-safe templates that carry a word bank (AC-05)', () => {
    const result = offerWordBankTemplates(mix, true);
    expect(result.map((t) => t.id)).toEqual(['safe-with-bank']);
  });

  it('with familySafeOn=false, offers every template that carries a word bank, regardless of family-safe tag', () => {
    const result = offerWordBankTemplates(mix, false);
    expect(result.map((t) => t.id)).toEqual(['safe-with-bank', 'unsafe-with-bank']);
  });

  it('never offers a bank-less template, even when family-safe (AC-06)', () => {
    const result = offerWordBankTemplates(mix, false);
    expect(result.some((t) => t.id === 'safe-no-bank')).toBe(false);
  });

  it('never offers a template with an empty word bank array', () => {
    const emptyBank: Template = {
      ...makeTemplate('empty-bank', true, false),
      wordBank: [],
    };
    const result = offerWordBankTemplates([emptyBank], false);
    expect(result).toEqual([]);
  });

  it('returns an empty array for empty input, regardless of toggle position', () => {
    expect(offerWordBankTemplates([], true)).toEqual([]);
    expect(offerWordBankTemplates([], false)).toEqual([]);
  });
});
