// ----------------------------------------------------------------------------
//  tabletLayout.test.ts - Vitest spec for the keepsake-gallery tablet image's
//  pure word-wrap layout (keepsake-gallery/01, AC-02). Canvas drawing itself
//  cannot run under Vitest (see tabletLayout.ts header), so this covers the
//  extracted PURE wrap logic with a fake character-count measurer.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { assemble, type SubmittedWord } from '../engine/assemble';
import { blank, text, type Template } from '../engine/template';
import { buildRevealParts } from '../pages/revealParts';
import {
  lineToPlainText,
  wrapPlainTextIntoLines,
  wrapRevealPartsIntoLines,
  type MeasureTextWidth,
} from './tabletLayout';

/** A fake measurer: width = character count (coral words measured 1.5x wider, mirroring the live screen's bolder coral weight). */
const measureByCharCount: MeasureTextWidth = (word, coral) => word.length * (coral ? 1.5 : 1);

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

describe('wrapRevealPartsIntoLines', () => {
  it('keeps every literal and filled word, in body order, when everything fits on one line', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
    ];
    const parts = buildRevealParts(template, assemble(template, words));

    const lines = wrapRevealPartsIntoLines(parts, measureByCharCount, 500);

    expect(lines).toHaveLength(1);
    expect(lines[0].segments.map((s) => s.text)).toEqual([
      'Once', 'upon', 'a', 'time,', 'a', 'gigantic', 'wizard', 'found', 'a', 'sock', '.',
    ]);
  });

  it('marks filled words coral and literal text non-coral', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
    ];
    const parts = buildRevealParts(template, assemble(template, words));

    const lines = wrapRevealPartsIntoLines(parts, measureByCharCount, 500);
    const flat = lines.flatMap((l) => l.segments);

    expect(flat.find((s) => s.text === 'gigantic')?.coral).toBe(true);
    expect(flat.find((s) => s.text.startsWith('Once'))?.coral).toBe(false);
  });

  it('wraps onto a new line once the running width exceeds maxWidth', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
    ];
    const parts = buildRevealParts(template, assemble(template, words));

    // A narrow width forces multiple lines; each line's rendered width (by
    // the fake measurer) must never exceed maxWidth by more than the widest
    // single token (a token never splits mid-word).
    const maxWidth = 20;
    const lines = wrapRevealPartsIntoLines(parts, measureByCharCount, maxWidth);

    expect(lines.length).toBeGreaterThan(1);
    // Every token from the source survives somewhere in the wrapped output.
    const allWords = lines.flatMap((l) => l.segments.map((s) => s.text));
    expect(allWords).toEqual([
      'Once', 'upon', 'a', 'time,', 'a', 'gigantic', 'wizard', 'found', 'a', 'sock', '.',
    ]);
  });

  it('renders an empty-word (skipped) blank as no token at all, not an empty coral segment', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [{ playerSessionId: 'p1', word: 'gigantic' }];
    const parts = buildRevealParts(template, assemble(template, words));

    const lines = wrapRevealPartsIntoLines(parts, measureByCharCount, 500);
    const flat = lines.flatMap((l) => l.segments);

    expect(flat.some((s) => s.text === '')).toBe(false);
    expect(flat.map((s) => s.text)).toEqual(['Once', 'upon', 'a', 'time,', 'a', 'gigantic', 'wizard', 'found', 'a', '.']);
  });

  it('a single token wider than maxWidth still gets its own line rather than being split', () => {
    const parts = buildRevealParts(
      { id: 'x', title: 'x', tags: { familySafe: true, ageRating: 'all-ages', themes: [] }, body: [text('supercalifragilisticexpialidocious')] },
      assemble({ id: 'x', title: 'x', tags: { familySafe: true, ageRating: 'all-ages', themes: [] }, body: [text('supercalifragilisticexpialidocious')] }, []),
    );

    const lines = wrapRevealPartsIntoLines(parts, measureByCharCount, 5);

    expect(lines).toHaveLength(1);
    expect(lines[0].segments).toEqual([{ text: 'supercalifragilisticexpialidocious', coral: false }]);
  });

  it('is deterministic: same inputs produce the same wrapped lines', () => {
    const template = makeTemplate();
    const words: SubmittedWord[] = [
      { playerSessionId: 'p1', word: 'squishy' },
      { playerSessionId: 'p2', word: 'spoon' },
    ];
    const parts = buildRevealParts(template, assemble(template, words));

    const first = wrapRevealPartsIntoLines(parts, measureByCharCount, 30);
    const second = wrapRevealPartsIntoLines(parts, measureByCharCount, 30);

    expect(second).toEqual(first);
  });
});

describe('wrapPlainTextIntoLines', () => {
  it('wraps a plain string (e.g. a title or byline) with no coral segments', () => {
    const lines = wrapPlainTextIntoLines('carved by Mia & Sam & crew', measureByCharCount, 12);

    expect(lines.length).toBeGreaterThan(1);
    expect(lines.every((line) => line.segments.every((s) => s.coral === false))).toBe(true);
  });

  it('never drops or reorders words', () => {
    const lines = wrapPlainTextIntoLines('The Wobbly Wizard & the Golden Sock', measureByCharCount, 10);
    const allWords = lines.flatMap((l) => l.segments.map((s) => s.text));

    expect(allWords).toEqual(['The', 'Wobbly', 'Wizard', '&', 'the', 'Golden', 'Sock']);
  });
});

describe('lineToPlainText', () => {
  it('joins a wrapped line back into a single space-separated string', () => {
    const lines = wrapPlainTextIntoLines('carved by Mia & crew', measureByCharCount, 500);

    expect(lines).toHaveLength(1);
    expect(lineToPlainText(lines[0])).toBe('carved by Mia & crew');
  });
});
