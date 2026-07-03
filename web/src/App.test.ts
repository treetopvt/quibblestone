// ----------------------------------------------------------------------------
//  App.test.ts - Vitest coverage for App.tsx's pure helpers: buildContributorLookup
//  (reveal-delight/04 word attribution, AC-01/AC-03/AC-06) and
//  shouldHoldLiveRouteForResume (session-engine/10, AC-01/AC-03 - the live-route
//  guards' "hold for a pending resume, or fall through to Home" decision).
//
//  App.tsx itself is the router shell with no render harness in this codebase
//  (no @testing-library/react wired up - see TaleFeedback.test.ts's header note
//  for the same posture); this file exercises the pure, exported logic each
//  guard consults, so the unattributed-blank guard (AC-03) and the resume-hold
//  condition (AC-01/AC-03) are covered without needing to mount React.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { buildContributorLookup, shouldHoldLiveRouteForResume } from './App';

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

describe('shouldHoldLiveRouteForResume (session-engine/10)', () => {
  it('AC-03: never holds when there is no stored reconnect handle, regardless of status/isRejoining', () => {
    expect(
      shouldHoldLiveRouteForResume({ status: 'connecting', isRejoining: false, hasReconnectHandle: false }),
    ).toBe(false);
    expect(
      shouldHoldLiveRouteForResume({ status: 'connected', isRejoining: true, hasReconnectHandle: false }),
    ).toBe(false);
    expect(
      shouldHoldLiveRouteForResume({ status: 'disconnected', isRejoining: false, hasReconnectHandle: false }),
    ).toBe(false);
  });

  it('AC-01: holds during the COLD-RELOAD window - a handle exists but the connection has not reached connected yet, before story 09\'s mount-time Rejoin can even fire', () => {
    expect(
      shouldHoldLiveRouteForResume({ status: 'connecting', isRejoining: false, hasReconnectHandle: true }),
    ).toBe(true);
  });

  it('AC-01: holds while a Rejoin invoke is in flight, even once connected', () => {
    expect(
      shouldHoldLiveRouteForResume({ status: 'connected', isRejoining: true, hasReconnectHandle: true }),
    ).toBe(true);
  });

  it('AC-01: holds in the connected-but-resume-not-yet-fired window - the first `connected` render lands before story 09\'s mount-time effect flips isRejoining and before room populates, so this must NOT flash Home', () => {
    expect(
      shouldHoldLiveRouteForResume({ status: 'connected', isRejoining: false, hasReconnectHandle: true }),
    ).toBe(true);
  });

  it('AC-03: falls through (redirects Home) on a SETTLED disconnect even with a handle - a cold reload during a real outage never auto-retries the initial start, so holding would strand the player forever; the redirect (plus ResumingLiveScreen\'s escape hatch) is the way out', () => {
    expect(
      shouldHoldLiveRouteForResume({ status: 'disconnected', isRejoining: false, hasReconnectHandle: true }),
    ).toBe(false);
  });
});
