// ----------------------------------------------------------------------------
//  reconnect.test.ts - Vitest coverage for the device-local reconnect handle
//  (session-engine/09, see ./reconnect.ts).
//
//  The suite's vitest.config.ts runs in the `node` environment (no DOM), so
//  there is no real `window.localStorage` - this file stubs a tiny in-memory
//  Storage-shaped fake onto `globalThis.window` before each test, mirroring
//  `web/src/content/playedHistory.test.ts`'s pattern (no jsdom dependency
//  needed for a single localStorage-shaped fake).
//
//  Asserts: a valid `{code, token}` handle round-trips (AC-01), a malformed /
//  corrupt / legacy-shaped stored value degrades to null rather than throwing
//  (AC-01, AC-06), a save-then-clear (the AC-04 "failed Rejoin" and AC-05
//  "deliberate leave" paths) leaves nothing behind, and the stored shape is
//  exactly `{code, token}` - no nickname, no extra fields (AC-06).
// ----------------------------------------------------------------------------

import { beforeEach, describe, expect, it } from 'vitest';
import { clearReconnectHandle, loadReconnectHandle, saveReconnectHandle } from './reconnect';

const STORAGE_KEY = 'qs.reconnect.v1';

/** A minimal in-memory Storage-shaped fake - just enough of the interface this module calls. */
function createFakeStorage(): Storage {
  const store = new Map<string, string>();
  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key: string) => {
      const value = store.get(key);
      return value === undefined ? null : value;
    },
    key: () => null,
    removeItem: (key: string) => {
      store.delete(key);
    },
    setItem: (key: string, value: string) => {
      store.set(key, value);
    },
  };
}

let fakeStorage: Storage;

beforeEach(() => {
  fakeStorage = createFakeStorage();
  // Stub the global `window` this module reads/writes through - no jsdom
  // dependency needed for a single localStorage-shaped fake.
  (globalThis as unknown as { window: Window }).window = {
    localStorage: fakeStorage,
  } as unknown as Window;
});

describe('loadReconnectHandle', () => {
  it('returns null when nothing has been stored yet', () => {
    expect(loadReconnectHandle()).toBeNull();
  });

  it('round-trips a valid {code, token} handle (AC-01)', () => {
    saveReconnectHandle('ABCD', 'super-secret-token');
    expect(loadReconnectHandle()).toEqual({ code: 'ABCD', token: 'super-secret-token' });
  });

  it('returns null for corrupt (non-JSON) storage instead of throwing', () => {
    fakeStorage.setItem(STORAGE_KEY, 'not json{{{');
    expect(() => loadReconnectHandle()).not.toThrow();
    expect(loadReconnectHandle()).toBeNull();
  });

  it('returns null when the stored value is not handle-shaped', () => {
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify({ nickname: 'not-a-handle' }));
    expect(loadReconnectHandle()).toBeNull();

    fakeStorage.setItem(STORAGE_KEY, JSON.stringify({ code: 'ABCD' })); // missing token
    expect(loadReconnectHandle()).toBeNull();

    fakeStorage.setItem(STORAGE_KEY, JSON.stringify({ code: '', token: 'x' })); // empty code
    expect(loadReconnectHandle()).toBeNull();

    fakeStorage.setItem(STORAGE_KEY, JSON.stringify({ code: 'ABCD', token: 42 })); // wrong type
    expect(loadReconnectHandle()).toBeNull();

    fakeStorage.setItem(STORAGE_KEY, JSON.stringify(['ABCD', 'token'])); // legacy/wrong shape
    expect(loadReconnectHandle()).toBeNull();
  });

  it('stores ONLY {code, token} - no nickname, name, or cross-room history (AC-06)', () => {
    saveReconnectHandle('WXYZ', 'a-token');
    const raw = fakeStorage.getItem(STORAGE_KEY);
    expect(raw).not.toBeNull();
    const parsed: unknown = JSON.parse(raw ?? '{}');
    expect(parsed).toEqual({ code: 'WXYZ', token: 'a-token' });
  });

  it('uses its own key, separate from identity.ts (qs.identity.v1)', () => {
    expect(STORAGE_KEY).not.toBe('qs.identity.v1');
  });
});

describe('saveReconnectHandle', () => {
  it('overwrites whatever was stored before - a device holds at most one handle (AC-01)', () => {
    saveReconnectHandle('AAAA', 'token-1');
    saveReconnectHandle('BBBB', 'token-2');
    expect(loadReconnectHandle()).toEqual({ code: 'BBBB', token: 'token-2' });
  });

  it('never throws when storage is unavailable', () => {
    (globalThis as unknown as { window: Window }).window = {} as unknown as Window;
    expect(() => saveReconnectHandle('AAAA', 'token-1')).not.toThrow();
  });
});

describe('clearReconnectHandle', () => {
  it('removes a stored handle (AC-05: deliberate leave / Home)', () => {
    saveReconnectHandle('AAAA', 'token-1');
    clearReconnectHandle();
    expect(loadReconnectHandle()).toBeNull();
  });

  it('leaves the handle cleared after a simulated failed Rejoin (AC-04)', () => {
    saveReconnectHandle('AAAA', 'token-1');
    // Simulate the failure path: a Rejoin came back Ok:false, so the caller
    // discards the stale handle immediately.
    clearReconnectHandle();
    expect(loadReconnectHandle()).toBeNull();
  });

  it('never throws when storage is unavailable', () => {
    (globalThis as unknown as { window: Window }).window = {} as unknown as Window;
    expect(() => clearReconnectHandle()).not.toThrow();
  });

  it('is a no-op (never throws) when nothing was stored', () => {
    expect(() => clearReconnectHandle()).not.toThrow();
    expect(loadReconnectHandle()).toBeNull();
  });
});
