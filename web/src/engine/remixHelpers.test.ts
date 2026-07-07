// ----------------------------------------------------------------------------
//  remixHelpers.test.ts - Vitest spec for replay-remix/02's pure blank-picker
//  helper (AC-02) and the engine's overwrite-only-one-blank guarantee
//  (AC-04), which this story leans on but does not reimplement.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { assembleStory, collectWord, createCollection } from './engine';
import type { ModeConfig } from './mode';
import { blank, text, type Template } from './template';
import { listRemixableBlanks } from './remixHelpers';

/** A small hand-authored template with 2 ordered blanks, mirroring engine.test.ts's fixture. */
function makeTemplate(): Template {
  return {
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
        prompt: 'Give me a thing',
        subHint: 'Any object will do.',
        sparkWords: ['sock', 'hat', 'spoon'],
      }),
      text('.'),
    ],
  };
}

const classicBlind: ModeConfig = {
  id: 'classic-blind',
  label: 'Classic (Blind)',
  see: 'nothing',
  answer: 'free-text',
  reveal: 'at-end',
};

describe('listRemixableBlanks (AC-02)', () => {
  it('returns the correct category/word pairs from an assembled story + its template', async () => {
    const template = makeTemplate();
    const collected = createCollection();
    await collectWord(collected, template, classicBlind, 'blank-1', { playerSessionId: 'p1', word: 'squishy' });
    await collectWord(collected, template, classicBlind, 'blank-2', { playerSessionId: 'p2', word: 'sock' });
    const assembled = assembleStory(template, collected);

    expect(listRemixableBlanks(assembled, template)).toEqual([
      { blankId: 'blank-1', categoryLabel: 'ADJECTIVE', word: 'squishy' },
      { blankId: 'blank-2', categoryLabel: 'NOUN', word: 'sock' },
    ]);
  });

  it('leaves an uncollected blank in the list with an empty word (assemble()s own fewer-words rule)', async () => {
    const template = makeTemplate();
    const collected = createCollection();
    // Only blank-1 collected; assemble() still fills blank-2 with an empty-string
    // word rather than omitting it (see assemble.ts's word-count-mismatch rule) -
    // the picker reflects that same, unmodified assembled shape.
    await collectWord(collected, template, classicBlind, 'blank-1', { playerSessionId: 'p1', word: 'squishy' });
    const assembled = assembleStory(template, collected);

    expect(listRemixableBlanks(assembled, template)).toEqual([
      { blankId: 'blank-1', categoryLabel: 'ADJECTIVE', word: 'squishy' },
      { blankId: 'blank-2', categoryLabel: 'NOUN', word: '' },
    ]);
  });

  it('skips a filled word whose blank id has no match in the template (defensive, e.g. a catalog drift)', () => {
    const template = makeTemplate();
    // Hand-construct an AssembledStory with a phantom blank id that is not part
    // of the template - a defensive case this module guards, never reachable
    // from a real round on the SAME template + engine.
    const assembled = {
      templateId: template.id,
      title: template.title,
      storyText: 'A squishy wizard found a sock.',
      filledWords: [
        { blankId: 'blank-1', word: 'squishy', playerSessionId: 'p1' },
        { blankId: 'phantom-blank', word: 'sock', playerSessionId: 'p2' },
      ],
    };

    expect(listRemixableBlanks(assembled, template)).toEqual([
      { blankId: 'blank-1', categoryLabel: 'ADJECTIVE', word: 'squishy' },
    ]);
  });
});

describe('a second collectWord for the same blankId overwrites only that entry (AC-04)', () => {
  it('re-assembles with every OTHER word unchanged and only the remixed word swapped', async () => {
    const template = makeTemplate();
    const collected = createCollection();
    await collectWord(collected, template, classicBlind, 'blank-1', { playerSessionId: 'p1', word: 'squishy' });
    await collectWord(collected, template, classicBlind, 'blank-2', { playerSessionId: 'p2', word: 'sock' });
    const firstAssembly = assembleStory(template, collected);
    expect(firstAssembly.storyText).toBe('A squishy wizard found a sock.');

    // The remix: a SECOND collectWord call for the SAME blankId ('blank-1'),
    // using the SAME engine functions - never a parallel re-implementation.
    const remixResult = await collectWord(collected, template, classicBlind, 'blank-1', {
      playerSessionId: 'p1',
      word: 'gigantic',
    });
    expect(remixResult).toEqual({ accepted: true });

    const remixedAssembly = assembleStory(template, collected);
    expect(remixedAssembly.storyText).toBe('A gigantic wizard found a sock.');

    // Every OTHER blank's word is unchanged; only the remixed blank swapped.
    expect(remixedAssembly.filledWords).toEqual([
      { blankId: 'blank-1', word: 'gigantic', playerSessionId: 'p1' },
      { blankId: 'blank-2', word: 'sock', playerSessionId: 'p2' },
    ]);
    expect(remixedAssembly.filledWords[1]).toEqual(firstAssembly.filledWords[1]);

    // The picker helper reflects the remix too (composition, not a fork).
    expect(listRemixableBlanks(remixedAssembly, template)).toEqual([
      { blankId: 'blank-1', categoryLabel: 'ADJECTIVE', word: 'gigantic' },
      { blankId: 'blank-2', categoryLabel: 'NOUN', word: 'sock' },
    ]);
  });
});
