// ----------------------------------------------------------------------------
//  mode.test.ts - Vitest spec for the three-axes ModeConfig type (AC-01,
//  AC-04). Since ModeConfig is pure data with no behavior, this spec mostly
//  exercises that the type system allows every axis combination to be
//  expressed - including the axis values Slice 1 does not implement
//  (subject-only, progressive-story, word-bank, progressively) - which is the
//  AC-01 requirement that the interface ALLOW those modes even though this
//  story does not build them.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import type { ModeAnswerAxis, ModeConfig, ModeRevealAxis, ModeSeeAxis } from './mode';

describe('ModeConfig', () => {
  it('expresses Classic blind (Slice 1) as a config of the three axes', () => {
    const classicBlind: ModeConfig = {
      id: 'classic-blind',
      label: 'Classic (Blind)',
      see: 'nothing',
      answer: 'free-text',
      reveal: 'at-end',
    };

    expect(classicBlind.see).toBe('nothing');
    expect(classicBlind.answer).toBe('free-text');
    expect(classicBlind.reveal).toBe('at-end');
  });

  it('allows a subject-only, progressively-revealed, word-bank mode to be expressed (not implemented this story, but expressible)', () => {
    const futureMode: ModeConfig = {
      id: 'subject-word-bank-progressive',
      label: 'Subject + Word Bank + Progressive (future)',
      see: 'subject-only',
      answer: 'word-bank',
      reveal: 'progressively',
    };

    expect(futureMode.see).toBe('subject-only');
    expect(futureMode.answer).toBe('word-bank');
    expect(futureMode.reveal).toBe('progressively');
  });

  it('allows every value of the "see" axis', () => {
    const values: ModeSeeAxis[] = ['nothing', 'subject-only', 'progressive-story'];
    expect(values).toHaveLength(3);
  });

  it('allows every value of the "answer" axis', () => {
    const values: ModeAnswerAxis[] = ['free-text', 'word-bank'];
    expect(values).toHaveLength(2);
  });

  it('allows every value of the "reveal" axis', () => {
    const values: ModeRevealAxis[] = ['at-end', 'progressively'];
    expect(values).toHaveLength(2);
  });

  it('allows two modes to differ only by configuration, with no shared code change required (AC-03)', () => {
    const a: ModeConfig = {
      id: 'mode-a',
      label: 'Mode A',
      see: 'nothing',
      answer: 'free-text',
      reveal: 'at-end',
    };
    const b: ModeConfig = { ...a, id: 'mode-b', label: 'Mode B', reveal: 'progressively' };

    expect(a.reveal).toBe('at-end');
    expect(b.reveal).toBe('progressively');
    expect(a.see).toBe(b.see);
    expect(a.answer).toBe(b.answer);
  });
});
