// ----------------------------------------------------------------------------
//  soloModes.test.ts - Vitest coverage for the solo mode registry
//  (single-player/02). See ./soloModes.ts.
//
//  Two concerns are proven here without a render harness (this repo has none -
//  see the engine/modes test conventions):
//    1. The registry pairs each mode with the RIGHT ModeConfig and the right
//       surface SLOT (Word Bank -> answerSurface, Progressive Story ->
//       seeContext, Progressive Reveal -> revealPresentation, Classic blind ->
//       no surfaces). Calling a *Surfaces factory builds a React element object
//       (createElement) but never renders it, so we can assert WHICH slot is
//       populated without a DOM.
//    2. `eligibleTemplates` routes each mode to the correct content gate
//       (free-text modes -> selectTemplates; Word Bank -> offerWordBankTemplates),
//       exercised against a deliberate template mix (not seedLibrary, so the
//       not-family-safe branch is covered).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { createCollection } from '../engine/engine';
import { classicBlind } from '../engine/modes/classicBlind';
import { progressiveReveal } from '../engine/modes/progressiveReveal';
import { progressiveStory } from '../engine/modes/progressiveStory';
import { wordBank } from '../engine/modes/wordBank';
import { assembleStory } from '../engine/engine';
import { blank, getBlanks, text, type Template } from '../engine/template';
import {
  DEFAULT_SOLO_MODE,
  SOLO_MODES,
  findSoloMode,
  type SoloFillContext,
  type SoloRevealContext,
} from './soloModes';

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

/** A single-blank template with a matching word bank, for the surface-wiring assertions. */
const bankTemplate = makeTemplate('surface-fixture', true, true);

function fillContext(): SoloFillContext {
  const collection = createCollection();
  const currentBlank = getBlanks(bankTemplate)[0];
  return {
    template: bankTemplate,
    collectedSoFar: collection,
    currentBlank,
    onSubmit: async () => ({ accepted: true }),
  };
}

function revealContext(): SoloRevealContext {
  const collection = createCollection();
  return { template: bankTemplate, assembled: assembleStory(bankTemplate, collection) };
}

describe('SOLO_MODES registry', () => {
  it('lists exactly the four built modes, in picker order, Classic blind first', () => {
    expect(SOLO_MODES.map((mode) => mode.config.id)).toEqual([
      'classic-blind',
      'word-bank',
      'progressive-story',
      'progressive-reveal',
    ]);
  });

  it('pairs each entry with its existing ModeConfig (no new config invented)', () => {
    expect(SOLO_MODES[0].config).toBe(classicBlind);
    expect(SOLO_MODES[1].config).toBe(wordBank);
    expect(SOLO_MODES[2].config).toBe(progressiveStory);
    expect(SOLO_MODES[3].config).toBe(progressiveReveal);
  });

  it('defaults to Classic blind (the first entry)', () => {
    expect(DEFAULT_SOLO_MODE).toBe(SOLO_MODES[0]);
    expect(DEFAULT_SOLO_MODE.config.id).toBe('classic-blind');
  });

  it('gives every mode a non-empty blurb and an icon', () => {
    for (const mode of SOLO_MODES) {
      expect(mode.blurb.length).toBeGreaterThan(0);
      expect(mode.icon.length).toBeGreaterThan(0);
    }
  });
});

describe('findSoloMode', () => {
  it('resolves each mode by its config id', () => {
    expect(findSoloMode('word-bank').config).toBe(wordBank);
    expect(findSoloMode('progressive-reveal').config).toBe(progressiveReveal);
  });

  it('falls back to Classic blind for an unknown id (never undefined)', () => {
    expect(findSoloMode('no-such-mode')).toBe(DEFAULT_SOLO_MODE);
  });
});

describe('SoloMode.fillSurfaces / revealSurfaces slot wiring', () => {
  it('Classic blind supplies no surfaces (FillBlank / Reveal render their defaults)', () => {
    const mode = findSoloMode('classic-blind');
    const fill = mode.fillSurfaces(fillContext());
    expect(fill.answerSurface).toBeUndefined();
    expect(fill.seeContext).toBeUndefined();
    expect(mode.revealSurfaces(revealContext()).revealPresentation).toBeUndefined();
  });

  it('Word Bank supplies answerSurface only', () => {
    const mode = findSoloMode('word-bank');
    const fill = mode.fillSurfaces(fillContext());
    expect(fill.answerSurface).toBeDefined();
    expect(fill.seeContext).toBeUndefined();
    expect(mode.revealSurfaces(revealContext()).revealPresentation).toBeUndefined();
  });

  it('Progressive Story supplies seeContext only', () => {
    const mode = findSoloMode('progressive-story');
    const fill = mode.fillSurfaces(fillContext());
    expect(fill.seeContext).toBeDefined();
    expect(fill.answerSurface).toBeUndefined();
    expect(mode.revealSurfaces(revealContext()).revealPresentation).toBeUndefined();
  });

  it('Progressive Reveal supplies revealPresentation only', () => {
    const mode = findSoloMode('progressive-reveal');
    const fill = mode.fillSurfaces(fillContext());
    expect(fill.answerSurface).toBeUndefined();
    expect(fill.seeContext).toBeUndefined();
    expect(mode.revealSurfaces(revealContext()).revealPresentation).toBeDefined();
  });
});

describe('SoloMode.eligibleTemplates content gate', () => {
  const mix: readonly Template[] = [
    makeTemplate('safe-with-bank', true, true),
    makeTemplate('safe-no-bank', true, false),
    makeTemplate('unsafe-with-bank', false, true),
    makeTemplate('unsafe-no-bank', false, false),
  ];

  it('free-text modes use the family-safe selection (bank-agnostic)', () => {
    const classic = findSoloMode('classic-blind');
    // Family-safe ON: only family-safe templates, regardless of bank.
    expect(classic.eligibleTemplates(mix, true).map((t) => t.id)).toEqual([
      'safe-with-bank',
      'safe-no-bank',
    ]);
    // Family-safe OFF: every template.
    expect(classic.eligibleTemplates(mix, false)).toHaveLength(4);
    // Progressive Story / Progressive Reveal share the same free-text gate.
    expect(findSoloMode('progressive-story').eligibleTemplates(mix, true).map((t) => t.id)).toEqual([
      'safe-with-bank',
      'safe-no-bank',
    ]);
    expect(findSoloMode('progressive-reveal').eligibleTemplates(mix, false)).toHaveLength(4);
  });

  it('Word Bank offers only templates that carry a bank, honoring family-safe (AC-04)', () => {
    const wordBankMode = findSoloMode('word-bank');
    expect(wordBankMode.eligibleTemplates(mix, true).map((t) => t.id)).toEqual(['safe-with-bank']);
    expect(wordBankMode.eligibleTemplates(mix, false).map((t) => t.id)).toEqual([
      'safe-with-bank',
      'unsafe-with-bank',
    ]);
  });

  it('Word Bank is disabled (no eligible templates) when no family-safe template has a bank', () => {
    const wordBankMode = findSoloMode('word-bank');
    const onlyUnsafeBanks: readonly Template[] = [
      makeTemplate('unsafe-with-bank', false, true),
      makeTemplate('safe-no-bank', true, false),
    ];
    expect(wordBankMode.eligibleTemplates(onlyUnsafeBanks, true)).toHaveLength(0);
  });
});
