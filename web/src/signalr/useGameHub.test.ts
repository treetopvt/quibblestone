// ----------------------------------------------------------------------------
//  useGameHub.test.ts - Vitest coverage for `manualReconnectDelayMs` (B1,
//  alpha-gate hardening; see ./useGameHub.ts's file header).
//
//  The hook itself (`useGameHub`) owns a live HubConnection and drives React
//  state - there is no render harness in this codebase (no @testing-library/
//  react wired up; see App.test.ts's header note for the same posture), so it
//  is not unit-tested directly. `manualReconnectDelayMs` is the one PURE,
//  extractable piece of the manual reconnect loop (the backoff schedule the
//  loop consults once `withAutomaticReconnect()` gives up or the very first
//  `connection.start()` fails outright) - pinning it here is the practical
//  substitute for testing the loop's timing end-to-end.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { manualReconnectDelayMs } from './useGameHub';

describe('manualReconnectDelayMs (B1)', () => {
  it('follows the 2s / 5s / 10s / 30s schedule for the first four attempts', () => {
    expect(manualReconnectDelayMs(0)).toBe(2000);
    expect(manualReconnectDelayMs(1)).toBe(5000);
    expect(manualReconnectDelayMs(2)).toBe(10000);
    expect(manualReconnectDelayMs(3)).toBe(30000);
  });

  it('repeats the final 30s step forever once the schedule is exhausted', () => {
    expect(manualReconnectDelayMs(4)).toBe(30000);
    expect(manualReconnectDelayMs(5)).toBe(30000);
    expect(manualReconnectDelayMs(100)).toBe(30000);
  });

  it('clamps a negative attempt to the first step rather than throwing or going out of bounds', () => {
    expect(manualReconnectDelayMs(-1)).toBe(2000);
  });
});
