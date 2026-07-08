// ----------------------------------------------------------------------------
//  useGameHub.test.ts - Vitest coverage for the PURE, extractable pieces of the
//  reconnect logic: `manualReconnectDelayMs` + `startWithTimeout` (B1, alpha-gate
//  hardening) and the reconnect-jitter helpers `withReconnectJitter`,
//  `jitteredManualReconnectDelayMs`, and `jitteredAutoReconnectDelayMs` (the
//  load-test follow-up that de-syncs a reconnect storm - see
//  docs/load-testing/findings.md and ./useGameHub.ts's file header).
//
//  The hook itself (`useGameHub`) owns a live HubConnection and drives React
//  state - there is no render harness in this codebase (no @testing-library/
//  react wired up; see App.test.ts's header note for the same posture), so it
//  is not unit-tested directly. `manualReconnectDelayMs` and `startWithTimeout`
//  are the PURE, extractable pieces of the manual reconnect loop - pinning them
//  here is the practical substitute for testing the loop's timing end-to-end.
// ----------------------------------------------------------------------------

import { describe, expect, it, vi } from 'vitest';
import {
  AUTO_RECONNECT_BASE_MS,
  jitteredAutoReconnectDelayMs,
  jitteredManualReconnectDelayMs,
  manualReconnectDelayMs,
  startWithTimeout,
  withReconnectJitter,
  type StartStoppable,
} from './useGameHub';

// A deterministic stand-in for Math.random so each jittered delay is exactly
// assertable (production always passes the real Math.random).
const fixedRandom = (value: number) => () => value;

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

describe('withReconnectJitter (reconnect-storm de-sync)', () => {
  it('leaves an immediate (0ms) base untouched so a lone client still retries fast', () => {
    expect(withReconnectJitter(0, fixedRandom(0))).toBe(0);
    expect(withReconnectJitter(0, fixedRandom(0.99))).toBe(0);
  });

  it('treats a negative base as 0 rather than returning a negative delay', () => {
    expect(withReconnectJitter(-1000, fixedRandom(0.5))).toBe(0);
  });

  it('maps the random draw across the equal-jitter band [base/2, base]', () => {
    // random=0 -> the low end (base/2); random->1 -> the high end (base).
    expect(withReconnectJitter(2000, fixedRandom(0))).toBe(1000);
    expect(withReconnectJitter(2000, fixedRandom(0.5))).toBe(1500);
    expect(withReconnectJitter(2000, fixedRandom(1))).toBe(2000);
  });

  it('never exceeds the base and never drops below half, across the whole random range', () => {
    for (const base of [2000, 5000, 10000, 30000]) {
      for (const r of [0, 0.01, 0.25, 0.5, 0.75, 0.999999]) {
        const d = withReconnectJitter(base, fixedRandom(r));
        expect(d).toBeGreaterThanOrEqual(base / 2);
        expect(d).toBeLessThanOrEqual(base);
      }
    }
  });

  it('de-syncs: two clients drawing different randoms get different delays from one base', () => {
    // The whole point - identical base, different random draw, different wait.
    expect(withReconnectJitter(30000, fixedRandom(0.1))).not.toBe(
      withReconnectJitter(30000, fixedRandom(0.9)),
    );
  });

  it('is deterministic for a given random source', () => {
    expect(withReconnectJitter(10000, fixedRandom(0.42))).toBe(
      withReconnectJitter(10000, fixedRandom(0.42)),
    );
  });
});

describe('jitteredManualReconnectDelayMs (manual loop, jittered)', () => {
  it('spreads each schedule step to its band low end (base/2) at random=0', () => {
    expect(jitteredManualReconnectDelayMs(0, fixedRandom(0))).toBe(1000); // base 2000
    expect(jitteredManualReconnectDelayMs(1, fixedRandom(0))).toBe(2500); // base 5000
    expect(jitteredManualReconnectDelayMs(2, fixedRandom(0))).toBe(5000); // base 10000
    expect(jitteredManualReconnectDelayMs(3, fixedRandom(0))).toBe(15000); // base 30000
  });

  it('stays within [base/2, base] of the underlying schedule for every attempt', () => {
    for (const attempt of [0, 1, 2, 3, 4, 100]) {
      const base = manualReconnectDelayMs(attempt);
      for (const r of [0, 0.5, 0.999999]) {
        const d = jitteredManualReconnectDelayMs(attempt, fixedRandom(r));
        expect(d).toBeGreaterThanOrEqual(base / 2);
        expect(d).toBeLessThanOrEqual(base);
      }
    }
  });

  it('inherits the schedule clamp - a far attempt still rides the 30s step (jittered)', () => {
    expect(jitteredManualReconnectDelayMs(100, fixedRandom(1))).toBe(30000);
    expect(jitteredManualReconnectDelayMs(100, fixedRandom(0))).toBe(15000);
  });
});

describe('jitteredAutoReconnectDelayMs (withAutomaticReconnect policy, jittered)', () => {
  it('mirrors the default delays [0, 2s, 10s, 30s], jittered, for the first four attempts', () => {
    expect(jitteredAutoReconnectDelayMs(0, fixedRandom(0.5))).toBe(0); // immediate stays immediate
    expect(jitteredAutoReconnectDelayMs(1, fixedRandom(0))).toBe(1000); // base 2000
    expect(jitteredAutoReconnectDelayMs(2, fixedRandom(0))).toBe(5000); // base 10000
    expect(jitteredAutoReconnectDelayMs(3, fixedRandom(0))).toBe(15000); // base 30000
  });

  it('GIVES UP after the 4th attempt (null) - preserving the onclose -> manual-loop handoff', () => {
    // The safety-critical invariant: the jittered policy must stop on exactly
    // the same attempt the default does, or the manual reconnect loop (which
    // `onclose` kicks off) would never engage.
    expect(jitteredAutoReconnectDelayMs(4)).toBeNull();
    expect(jitteredAutoReconnectDelayMs(5)).toBeNull();
    expect(jitteredAutoReconnectDelayMs(99)).toBeNull();
  });

  it('returns null for a negative count rather than throwing or indexing out of bounds', () => {
    expect(jitteredAutoReconnectDelayMs(-1)).toBeNull();
  });

  it('offers exactly as many retry attempts as the default schedule (no more, no fewer)', () => {
    const nonNull = Array.from({ length: 10 }, (_, count) =>
      jitteredAutoReconnectDelayMs(count, fixedRandom(0.5)),
    ).filter((d) => d !== null);
    expect(nonNull).toHaveLength(AUTO_RECONNECT_BASE_MS.length);
  });

  it('keeps every non-null delay within [base/2, base] of the mirrored schedule', () => {
    AUTO_RECONNECT_BASE_MS.forEach((base, count) => {
      for (const r of [0, 0.5, 0.999999]) {
        const d = jitteredAutoReconnectDelayMs(count, fixedRandom(r));
        if (d === null) throw new Error(`unexpected null delay at count ${count}`);
        const lower = base === 0 ? 0 : base / 2;
        expect(d).toBeGreaterThanOrEqual(lower);
        expect(d).toBeLessThanOrEqual(base);
      }
    });
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
