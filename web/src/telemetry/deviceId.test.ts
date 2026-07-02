// ----------------------------------------------------------------------------
//  deviceId.test.ts - proves the anonymous device-id module is robust and never
//  throws (platform-devops/05, AC-03). The device id answers approximate REACH,
//  so it must survive a disabled / throwing localStorage (private browsing, quota,
//  http-over-LAN) by falling back to a transient anonymous id rather than crashing
//  a round. We test the PURE validator directly and the mint/reuse path over a
//  stubbed storage, mirroring serveLog.test.ts's fail-soft coverage.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { getOrCreateDeviceId, isValidDeviceId } from './deviceId';

describe('isValidDeviceId', () => {
  it('accepts a non-empty string', () => {
    expect(isValidDeviceId('abc-123')).toBe(true);
  });

  it('rejects null, undefined, non-strings, and the empty string', () => {
    expect(isValidDeviceId(null)).toBe(false);
    expect(isValidDeviceId(undefined)).toBe(false);
    expect(isValidDeviceId('')).toBe(false);
    expect(isValidDeviceId(42)).toBe(false);
    expect(isValidDeviceId({})).toBe(false);
  });
});

describe('getOrCreateDeviceId', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('mints and persists an id on first use, then reuses it', () => {
    const store = new Map<string, string>();
    vi.stubGlobal('window', {
      localStorage: {
        getItem: (k: string) => store.get(k) ?? null,
        setItem: (k: string, v: string) => void store.set(k, v),
      },
    });

    const first = getOrCreateDeviceId();
    expect(first.length).toBeGreaterThan(0);
    // Second call returns the SAME persisted id (a stable device count key).
    expect(getOrCreateDeviceId()).toBe(first);
  });

  it('replaces a corrupt/empty stored value rather than returning it', () => {
    const store = new Map<string, string>([['qs.telemetry.deviceId.v1', '']]);
    vi.stubGlobal('window', {
      localStorage: {
        getItem: (k: string) => store.get(k) ?? null,
        setItem: (k: string, v: string) => void store.set(k, v),
      },
    });

    const id = getOrCreateDeviceId();
    expect(id.length).toBeGreaterThan(0);
    expect(id).not.toBe('');
  });

  it('does not throw and still returns an id when storage throws', () => {
    vi.stubGlobal('window', {
      localStorage: {
        getItem: () => {
          throw new Error('storage disabled');
        },
        setItem: () => {
          throw new Error('storage disabled');
        },
      },
    });

    expect(() => getOrCreateDeviceId()).not.toThrow();
    expect(getOrCreateDeviceId().length).toBeGreaterThan(0);
  });
});
