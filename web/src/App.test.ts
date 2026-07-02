// ----------------------------------------------------------------------------
//  App.test.ts - Vitest coverage for App.tsx's pure buildContributorLookup
//  (reveal-delight/04 word attribution, AC-01/AC-03/AC-06).
//
//  App.tsx itself is the router shell with no render harness in this codebase
//  (no @testing-library/react wired up - see TaleFeedback.test.ts's header note
//  for the same posture); this file exercises the pure, exported lookup builder
//  GroupReveal's Reveal `wordAttribution` prop is derived from, so the
//  unattributed-blank guard (AC-03) is covered without needing to mount React.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { buildContributorLookup } from './App';

describe('buildContributorLookup', () => {
  it('AC-01: resolves a filled word\'s nickname (its playerSessionId) to nickname + variant', () => {
    const lookup = buildContributorLookup([
      { word: 'wobbly', nickname: 'Pip', variant: 'purple' },
      { word: 'noodle', nickname: 'Wren', variant: 'teal' },
    ]);
    expect(lookup.contributorFor('Pip')).toEqual({ nickname: 'Pip', variant: 'purple' });
    expect(lookup.contributorFor('Wren')).toEqual({ nickname: 'Wren', variant: 'teal' });
  });

  it('AC-03: an unfilled blank (empty nickname) is never indexed, so it resolves to undefined', () => {
    const lookup = buildContributorLookup([
      { word: '', nickname: '', variant: '' },
      { word: 'gizmo', nickname: 'Flint', variant: 'gold' },
    ]);
    expect(lookup.contributorFor('')).toBeUndefined();
  });

  it('AC-03: a playerSessionId this reveal never carried resolves to undefined (never crashes)', () => {
    const lookup = buildContributorLookup([{ word: 'zoom', nickname: 'Bramble', variant: 'sand' }]);
    expect(lookup.contributorFor('someone-who-left')).toBeUndefined();
  });

  it('dedupes repeat contributions from the same nickname to one contributor entry', () => {
    const lookup = buildContributorLookup([
      { word: 'first', nickname: 'Juniper', variant: 'plum' },
      { word: 'second', nickname: 'Juniper', variant: 'plum' },
    ]);
    expect(lookup.contributorFor('Juniper')).toEqual({ nickname: 'Juniper', variant: 'plum' });
  });
});
