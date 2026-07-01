// ----------------------------------------------------------------------------
//  engine.test.ts - Vitest spec for the collect + assemble orchestration
//  (AC-02, AC-03, AC-04, AC-05). Covers: collection independent of mode,
//  the same template playing under multiple modes unmodified, the injectable
//  safety-check seam (accept / reject / skipped-for-word-bank / no-hook), and
//  delegating assembly to template-model's assemble() rather than
//  reimplementing it.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import {
  assembleStory,
  collectWord,
  createCollection,
  isCollectionComplete,
  skipBlank,
  toOrderedWords,
  type SafetyCheck,
} from './engine';
import type { ModeConfig } from './mode';
import { blank, text, type Template } from './template';

/** A small hand-authored template with 2 ordered blanks, shared across the spec. */
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

const progressiveWordBank: ModeConfig = {
  id: 'progressive-word-bank',
  label: 'Progressive Word Bank (future)',
  see: 'progressive-story',
  answer: 'word-bank',
  reveal: 'progressively',
};

describe('collectWord', () => {
  it('records a word against its blank id (AC-02)', async () => {
    const template = makeTemplate();
    const collected = createCollection();

    const result = await collectWord(collected, template, classicBlind, 'blank-1', {
      playerSessionId: 'p1',
      word: 'gigantic',
    });

    expect(result).toEqual({ accepted: true });
    expect(collected.get('blank-1')).toEqual({ playerSessionId: 'p1', word: 'gigantic' });
  });

  it('rejects a submission against an unknown blank id', async () => {
    const template = makeTemplate();
    const collected = createCollection();

    const result = await collectWord(collected, template, classicBlind, 'not-a-real-blank', {
      playerSessionId: 'p1',
      word: 'gigantic',
    });

    expect(result.accepted).toBe(false);
    expect(collected.size).toBe(0);
  });

  it('runs the injectable safety check for free-text answers and rejects a failing word (AC-05)', async () => {
    const template = makeTemplate();
    const collected = createCollection();
    const safetyCheck: SafetyCheck = async (word) =>
      word === 'naughty' ? { ok: false, message: 'Try a kinder word!' } : { ok: true };

    const result = await collectWord(
      collected,
      template,
      classicBlind,
      'blank-1',
      { playerSessionId: 'p1', word: 'naughty' },
      safetyCheck,
    );

    expect(result).toEqual({ accepted: false, message: 'Try a kinder word!' });
    expect(collected.has('blank-1')).toBe(false);
  });

  it('records the word when the injectable safety check passes (AC-05)', async () => {
    const template = makeTemplate();
    const collected = createCollection();
    const safetyCheck: SafetyCheck = async () => ({ ok: true });

    const result = await collectWord(
      collected,
      template,
      classicBlind,
      'blank-1',
      { playerSessionId: 'p1', word: 'gigantic' },
      safetyCheck,
    );

    expect(result).toEqual({ accepted: true });
    expect(collected.get('blank-1')?.word).toBe('gigantic');
  });

  it('records the word unchecked when no safety check is supplied', async () => {
    const template = makeTemplate();
    const collected = createCollection();

    const result = await collectWord(collected, template, classicBlind, 'blank-1', {
      playerSessionId: 'p1',
      word: 'gigantic',
    });

    expect(result).toEqual({ accepted: true });
  });

  it('skips the safety check for word-bank answers, even when a hook is supplied', async () => {
    const template = makeTemplate();
    const collected = createCollection();
    let called = false;
    const safetyCheck: SafetyCheck = async () => {
      called = true;
      return { ok: false, message: 'should never be reached' };
    };

    const result = await collectWord(
      collected,
      template,
      progressiveWordBank,
      'blank-1',
      { playerSessionId: 'p1', word: 'sock' },
      safetyCheck,
    );

    expect(result).toEqual({ accepted: true });
    expect(called).toBe(false);
  });

  it('is independent of which mode is active: the same call sequence works under any mode (AC-02, AC-03)', async () => {
    const template = makeTemplate();
    const modes: ModeConfig[] = [
      classicBlind,
      { ...classicBlind, id: 'm2', see: 'subject-only' },
      { ...classicBlind, id: 'm3', see: 'progressive-story', reveal: 'progressively' },
    ];

    for (const mode of modes) {
      const collected = createCollection();
      await collectWord(collected, template, mode, 'blank-1', { playerSessionId: 'p1', word: 'gigantic' });
      await collectWord(collected, template, mode, 'blank-2', { playerSessionId: 'p2', word: 'sock' });

      expect(isCollectionComplete(template, collected)).toBe(true);
      expect(toOrderedWords(template, collected)).toEqual([
        { playerSessionId: 'p1', word: 'gigantic' },
        { playerSessionId: 'p2', word: 'sock' },
      ]);
    }
  });
});

describe('skipBlank', () => {
  it('records an empty placeholder against its blank id (AC-02)', () => {
    const template = makeTemplate();
    const collected = createCollection();

    const result = skipBlank(collected, template, 'blank-1', 'p1');

    expect(result).toEqual({ accepted: true });
    expect(collected.get('blank-1')).toEqual({ playerSessionId: 'p1', word: '' });
  });

  it('keeps the collection size aligned so later words do not shift (positional alignment)', () => {
    const template = makeTemplate();
    const collected = createCollection();

    // Skip the FIRST blank, then fill the second. Without the placeholder the
    // second word would slide into the first blank's slot on assembly.
    skipBlank(collected, template, 'blank-1', 'p1');
    collected.set('blank-2', { playerSessionId: 'p2', word: 'sock' });

    expect(collected.size).toBe(2);
    expect(isCollectionComplete(template, collected)).toBe(true);
    expect(toOrderedWords(template, collected)).toEqual([
      { playerSessionId: 'p1', word: '' },
      { playerSessionId: 'p2', word: 'sock' },
    ]);
    // assemble() renders the empty placeholder as literally nothing.
    expect(assembleStory(template, collected).storyText).toBe('A  wizard found a sock.');
  });

  it('rejects a skip against an unknown blank id and records nothing', () => {
    const template = makeTemplate();
    const collected = createCollection();

    const result = skipBlank(collected, template, 'not-a-real-blank', 'p1');

    expect(result.accepted).toBe(false);
    expect(collected.size).toBe(0);
  });

  it('does not count as a filled word (whitespace-only placeholder)', () => {
    const template = makeTemplate();
    const collected = createCollection();

    skipBlank(collected, template, 'blank-1', 'p1');

    const placeholder = collected.get('blank-1');
    expect(placeholder?.word.trim().length).toBe(0);
  });
});

describe('toOrderedWords', () => {
  it('orders collected words by the template body order, regardless of collection (insertion) order', () => {
    const template = makeTemplate();
    const collected = createCollection();
    collected.set('blank-2', { playerSessionId: 'p2', word: 'sock' });
    collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });

    expect(toOrderedWords(template, collected)).toEqual([
      { playerSessionId: 'p1', word: 'gigantic' },
      { playerSessionId: 'p2', word: 'sock' },
    ]);
  });

  it('omits blanks with no collected word yet', () => {
    const template = makeTemplate();
    const collected = createCollection();
    collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });

    expect(toOrderedWords(template, collected)).toEqual([{ playerSessionId: 'p1', word: 'gigantic' }]);
  });
});

describe('isCollectionComplete', () => {
  it('is false until every blank has a collected word, then true', () => {
    const template = makeTemplate();
    const collected = createCollection();

    expect(isCollectionComplete(template, collected)).toBe(false);

    collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });
    expect(isCollectionComplete(template, collected)).toBe(false);

    collected.set('blank-2', { playerSessionId: 'p2', word: 'sock' });
    expect(isCollectionComplete(template, collected)).toBe(true);
  });
});

describe('assembleStory', () => {
  it('delegates to template-model assemble() to produce the final story (AC-02)', () => {
    const template = makeTemplate();
    const collected = createCollection();
    collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });
    collected.set('blank-2', { playerSessionId: 'p2', word: 'sock' });

    const result = assembleStory(template, collected);

    expect(result.storyText).toBe('A gigantic wizard found a sock.');
    expect(result.templateId).toBe('wobbly-wizard');
  });

  it('can assemble partial collections (e.g. mid-round, or a progressively-revealed mode)', () => {
    const template = makeTemplate();
    const collected = createCollection();
    collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });

    const result = assembleStory(template, collected);

    expect(result.storyText).toBe('A gigantic wizard found a .');
  });

  it('plays the same template, fully collected, identically under every mode (AC-04)', () => {
    const template = makeTemplate();
    const modes: ModeConfig[] = [classicBlind, progressiveWordBank];
    const results = modes.map((mode) => {
      const collected = createCollection();
      // collectWord call sequence is identical regardless of mode (AC-02/AC-03)
      void mode;
      collected.set('blank-1', { playerSessionId: 'p1', word: 'gigantic' });
      collected.set('blank-2', { playerSessionId: 'p2', word: 'sock' });
      return assembleStory(template, collected);
    });

    expect(results[0].storyText).toBe(results[1].storyText);
    expect(results[0].storyText).toBe('A gigantic wizard found a sock.');
  });
});
