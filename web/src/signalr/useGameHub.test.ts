// ----------------------------------------------------------------------------
//  useGameHub.test.ts - Vitest coverage for `manualReconnectDelayMs` and
//  `startWithTimeout` (B1, alpha-gate hardening; see ./useGameHub.ts's file
//  header).
//
//  The hook itself (`useGameHub`) owns a live HubConnection and drives React
//  state - there is no render harness in this codebase (no @testing-library/
//  react wired up; see App.test.ts's header note for the same posture), so it
//  is not unit-tested directly. `manualReconnectDelayMs` and `startWithTimeout`
//  are the PURE, extractable pieces of the manual reconnect loop - pinning them
//  here is the practical substitute for testing the loop's timing end-to-end.
// ----------------------------------------------------------------------------

import { describe, expect, it, vi } from 'vitest';
import { manualReconnectDelayMs, startWithTimeout, type StartStoppable } from './useGameHub';

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

describe('startWithTimeout (B1 follow-up: the connect-hang fix)', () => {
  it('resolves normally when start() resolves before the timeout, without ever calling stop()', async () => {
    const connection: StartStoppable = { start: vi.fn().mockResolvedValue(undefined), stop: vi.fn() };
    await expect(startWithTimeout(connection, 1000)).resolves.toBeUndefined();
    expect(connection.stop).not.toHaveBeenCalled();
  });

  it('rejects with the original error when start() rejects before the timeout, without calling stop()', async () => {
    const connection: StartStoppable = {
      start: vi.fn().mockRejectedValue(new Error('negotiate failed')),
      stop: vi.fn(),
    };
    await expect(startWithTimeout(connection, 1000)).rejects.toThrow('negotiate failed');
    expect(connection.stop).not.toHaveBeenCalled();
  });

  it('times out and stops the connection when start() never settles (the exact hang this fix closes)', async () => {
    vi.useFakeTimers();
    try {
      // A start() that never resolves nor rejects - the real-world case a
      // healthy local dev server never triggers but a stalled negotiate
      // against a live host can. Before this fix, nothing downstream of a
      // hang like this could ever fire.
      const connection: StartStoppable = {
        start: vi.fn(() => new Promise<void>(() => {})),
        stop: vi.fn().mockResolvedValue(undefined),
      };
      const result = startWithTimeout(connection, 1000);
      const assertion = expect(result).rejects.toThrow('timed out');
      await vi.advanceTimersByTimeAsync(1000);
      await assertion;
      expect(connection.stop).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('ignores a start() that resolves AFTER the timeout has already fired (no double-settle, no crash)', async () => {
    vi.useFakeTimers();
    try {
      let resolveStart: (() => void) | undefined;
      const connection: StartStoppable = {
        start: vi.fn(() => new Promise<void>((resolve) => { resolveStart = resolve; })),
        stop: vi.fn().mockResolvedValue(undefined),
      };
      const result = startWithTimeout(connection, 1000);
      const assertion = expect(result).rejects.toThrow('timed out');
      await vi.advanceTimersByTimeAsync(1000);
      await assertion;
      // The abandoned start() finally resolves late - must be a silent no-op,
      // not a second settle of the already-rejected promise.
      expect(() => resolveStart?.()).not.toThrow();
    } finally {
      vi.useRealTimers();
    }
  });
});
