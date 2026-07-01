// ----------------------------------------------------------------------------
//  progressiveStory.test.ts - Vitest spec for the Progressive Story ModeConfig
//  and its story-so-far mechanism (game-modes/05, AC-01/AC-03/AC-04).
//
//  What this proves:
//    1. The config itself is exactly the three axes the story specifies
//       (see='progressive-story', answer='free-text', reveal='at-end'), plus
//       stable id/label metadata - mirroring classicBlind.test.ts's shape
//       (AC-03).
//    2. `assembleStory` (engine.ts), called UNMODIFIED against a PARTIAL
//       `CollectedWords` map, yields the expected story-so-far text - proving
//       AC-03's "no engine/assemble change" claim directly, not just by
//       inspection.
//    3. `sliceStorySoFarParts` (StorySoFarContext.tsx's pure, exported helper)
//       stops at the current blank - nothing at or after it survives the
//       slice (AC-01).
//    4. Free-text submissions for this mode still route through the injected
//       SafetyCheck on collectWord's collection path (AC-04) - mirroring
//       classicBlind.test.ts's safety-seam proof for this mode's own answer
//       axis, in this mode's own test file (per the story's footprint rule:
//       do not edit the shared engine.test.ts).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { progressiveStory } from './progressiveStory';
import { assembleStory, collectWord, createCollection, type SafetyCheck } from '../engine';
import { blank, text, type Template } from '../template';
import { buildRevealParts } from '../../pages/revealParts';
import { sliceStorySoFarParts } from '../../pages/fillblank/StorySoFarContext';

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
      prompt: 'Give me a thing',
      subHint: 'Any old object will do!',
      sparkWords: ['sock', 'hat', 'spoon'],
    }),
    text('.'),
  ],
};

describe('progressiveStory ModeConfig', () => {
  it('is expressed as exactly the three Progressive Story axis values', () => {
    expect(progressiveStory.see).toBe('progressive-story');
    expect(progressiveStory.answer).toBe('free-text');
    expect(progressiveStory.reveal).toBe('at-end');
  });

  it('carries a stable id and human-facing label', () => {
    expect(progressiveStory.id).toBe('progressive-story');
    expect(progressiveStory.label).toBe('Progressive Story');
  });
});

describe('progressiveStory story-so-far mechanism (AC-03)', () => {
  it('assembleStory, called against a PARTIAL collection, yields the expected story-so-far text', () => {
    const collected = createCollection();
    collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });
    // blank-2 intentionally left uncollected - a partial collection.

    const assembledSoFar = assembleStory(template, collected);

    // assemble()'s documented non-throwing mismatch rule fills the
    // not-yet-collected trailing blank with an empty string.
    expect(assembledSoFar.storyText).toBe('A gigantic wizard found a .');
    expect(assembledSoFar.filledWords).toEqual([
      { blankId: 'blank-1', word: 'gigantic', playerSessionId: 'p1' },
      { blankId: 'blank-2', word: '', playerSessionId: undefined },
    ]);
  });

  it('sliceStorySoFarParts stops at the current blank, dropping it and everything after (AC-01)', () => {
    const collected = createCollection();
    collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });

    const assembledSoFar = assembleStory(template, collected);
    const allParts = buildRevealParts(template, assembledSoFar);

    // Currently prompting for blank-2: the story-so-far should show "A " +
    // the highlighted "gigantic" + " wizard found a " but STOP before
    // blank-2's word-part (empty though it is) and everything after it.
    const visible = sliceStorySoFarParts(allParts, 'blank-2');

    expect(visible).toEqual([
      { kind: 'text', text: 'A ' },
      { kind: 'word', word: 'gigantic', blankId: 'blank-1', playerSessionId: 'p1' },
      { kind: 'text', text: ' wizard found a ' },
    ]);
    expect(visible.some((part) => part.kind === 'word' && part.blankId === 'blank-2')).toBe(false);
  });

  it('sliceStorySoFarParts stops before the FIRST blank when nothing has been collected yet', () => {
    const collected = createCollection();
    const assembledSoFar = assembleStory(template, collected);
    const allParts = buildRevealParts(template, assembledSoFar);

    const visible = sliceStorySoFarParts(allParts, 'blank-1');

    expect(visible).toEqual([{ kind: 'text', text: 'A ' }]);
  });

  it('returns the full parts list unchanged when the current blank id is not found (round already complete)', () => {
    const collected = createCollection();
    collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });
    collected.set('blank-2', { playerSessionId: 'p2', word: 'sock' });

    const assembledSoFar = assembleStory(template, collected);
    const allParts = buildRevealParts(template, assembledSoFar);

    const visible = sliceStorySoFarParts(allParts, 'blank-does-not-exist');

    expect(visible).toEqual(allParts);
  });
});

describe('progressiveStory child safety seam (AC-04)', () => {
  it('routes free-text submissions through the injected safety check on the collection path', async () => {
    const safetyCheck: SafetyCheck = async (word) =>
      word === 'naughty' ? { ok: false, message: 'Try a kinder word!' } : { ok: true };

    const collected = createCollection();

    const rejected = await collectWord(
      collected,
      template,
      progressiveStory,
      'blank-1',
      { playerSessionId: 'p1', word: 'naughty' },
      safetyCheck,
    );
    expect(rejected).toEqual({ accepted: false, message: 'Try a kinder word!' });
    expect(collected.has('blank-1')).toBe(false);

    const accepted = await collectWord(
      collected,
      template,
      progressiveStory,
      'blank-1',
      { playerSessionId: 'p1', word: 'gigantic' },
      safetyCheck,
    );
    expect(accepted).toEqual({ accepted: true });
    expect(collected.get('blank-1')).toEqual({ playerSessionId: 'p1', word: 'gigantic' });
  });
});
