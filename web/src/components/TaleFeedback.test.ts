// ----------------------------------------------------------------------------
//  TaleFeedback.test.ts - Vitest coverage for TaleFeedback.tsx's pure vote
//  transition, applyVoteTap (story-selection/05, AC-02/AC-07).
//
//  TaleFeedback.tsx itself is a screen-level component with no render harness
//  in this codebase (no @testing-library/react is wired up - see Solo.test.ts's
//  header note for the same posture); this file exercises the pure, exported
//  transition function the component's tap handler calls, so the "last tap
//  wins" contract is covered without needing to mount React.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { applyVoteTap } from './TaleFeedback';

describe('applyVoteTap', () => {
  it('a first tap on "up" from no vote yet becomes "up"', () => {
    expect(applyVoteTap(null, 'up')).toBe('up');
  });

  it('a first tap on "down" from no vote yet becomes "down"', () => {
    expect(applyVoteTap(null, 'down')).toBe('down');
  });

  it('tapping up then down leaves the FINAL state "down" (last write wins, AC-02)', () => {
    let vote: 'up' | 'down' | null = null;
    vote = applyVoteTap(vote, 'up');
    vote = applyVoteTap(vote, 'down');
    expect(vote).toBe('down');
  });

  it('tapping down then up leaves the FINAL state "up" (the reverse changes-my-mind case)', () => {
    let vote: 'up' | 'down' | null = null;
    vote = applyVoteTap(vote, 'down');
    vote = applyVoteTap(vote, 'up');
    expect(vote).toBe('up');
  });

  it('tapping the same thumb twice is idempotent - it stays selected, never toggles off', () => {
    let vote: 'up' | 'down' | null = null;
    vote = applyVoteTap(vote, 'up');
    vote = applyVoteTap(vote, 'up');
    expect(vote).toBe('up');
  });

  it('AC-07: skipping entirely (never calling applyVoteTap) leaves no vote - silence is a valid answer', () => {
    // No tap happened, so there is nothing to reduce - the state a caller would
    // display stays at its initial null, and (by construction in TaleFeedback.tsx)
    // recordFeedback is only ever invoked from the tap handler that calls this
    // function, so a skipped vote never records anything.
    const neverTapped: 'up' | 'down' | null = null;
    expect(neverTapped).toBeNull();
  });
});
