// ----------------------------------------------------------------------------
//  classicBlind.test.ts - Vitest spec for the Classic (Blind) ModeConfig
//  (game-modes/02, AC-08).
//
//  Two things worth proving here:
//    1. The config itself is exactly the three axes the story specifies
//       (see='subject-only', answer='free-text', reveal='at-end'), plus stable
//       id/label metadata.
//    2. A light integration through engine.ts's collectWord: passing
//       classicBlind + a stub SafetyCheck rejects a failing word (never
//       recorded) and records a passing one. This proves the safety seam
//       sits on the mode-agnostic collection path for this mode too - the
//       mode itself never reimplements or bypasses it (see engine.ts header).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { classicBlind } from './classicBlind';
import { collectWord, createCollection, type SafetyCheck } from '../engine';
import { blank, text, type Template } from '../template';

describe('classicBlind', () => {
  it('is expressed as exactly the three Classic-blind axis values', () => {
    expect(classicBlind.see).toBe('subject-only');
    expect(classicBlind.answer).toBe('free-text');
    expect(classicBlind.reveal).toBe('at-end');
  });

  it('carries a stable id and human-facing label', () => {
    expect(classicBlind.id).toBe('classic-blind');
    expect(classicBlind.label).toBe('Classic (Blind)');
  });

  it('routes free-text submissions through the injected safety check on the collection path (AC-05)', async () => {
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
        text(' wizard.'),
      ],
    };

    const safetyCheck: SafetyCheck = async (word) =>
      word === 'naughty' ? { ok: false, message: 'Try a kinder word!' } : { ok: true };

    const collected = createCollection();

    const rejected = await collectWord(
      collected,
      template,
      classicBlind,
      'blank-1',
      { playerSessionId: 'p1', word: 'naughty' },
      safetyCheck,
    );
    expect(rejected).toEqual({ accepted: false, message: 'Try a kinder word!' });
    expect(collected.has('blank-1')).toBe(false);

    const accepted = await collectWord(
      collected,
      template,
      classicBlind,
      'blank-1',
      { playerSessionId: 'p1', word: 'gigantic' },
      safetyCheck,
    );
    expect(accepted).toEqual({ accepted: true });
    expect(collected.get('blank-1')).toEqual({ playerSessionId: 'p1', word: 'gigantic' });
  });
});
