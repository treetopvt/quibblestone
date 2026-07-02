// ----------------------------------------------------------------------------
//  serveLog.test.ts - covers the fail-soft id minting (story-selection review
//  hardening). safeUuid must NEVER throw, because it runs during render (a
//  feedback vote id) and on every telemetry write (the session id): on http over
//  a LAN IP - QuibbleStone's "different houses / same car" case - crypto.randomUUID
//  is undefined, and an unguarded call would crash the payoff screens.
// ----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';
import { safeUuid } from './serveLog';

describe('safeUuid', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns a non-empty id when crypto.randomUUID is available', () => {
    const id = safeUuid();
    expect(typeof id).toBe('string');
    expect(id.length).toBeGreaterThan(0);
  });

  it('does not throw and still returns an id when crypto.randomUUID is missing (non-secure context)', () => {
    // Simulate http-over-LAN: crypto exists but randomUUID is not a function.
    vi.stubGlobal('crypto', {});
    expect(() => safeUuid()).not.toThrow();
    expect(safeUuid().length).toBeGreaterThan(0);
  });

  it('does not throw and still returns an id when crypto.randomUUID throws', () => {
    vi.stubGlobal('crypto', {
      randomUUID: () => {
        throw new Error('SecureContext required');
      },
    });
    expect(() => safeUuid()).not.toThrow();
    expect(safeUuid().length).toBeGreaterThan(0);
  });

  it('mints distinct fallback ids so vote/session keys do not collide', () => {
    vi.stubGlobal('crypto', {});
    expect(safeUuid()).not.toBe(safeUuid());
  });
});
