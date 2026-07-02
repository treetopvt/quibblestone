// ----------------------------------------------------------------------------
//  playedHistory.test.ts - Vitest coverage for the solo device-local
//  played-template history (story-selection/03, see ./playedHistory.ts).
//
//  The suite's vitest.config.ts runs in the `node` environment (no DOM), so
//  there is no real `window.localStorage` - this file stubs a tiny in-memory
//  Storage-shaped fake onto `globalThis.window` before each test (no new
//  dependency: jsdom is deliberately NOT pulled in for this one module).
//  Asserts the stored SHAPE (an ordered array of id strings, nothing else),
//  the dedupe-and-move-to-end append rule, the cap, and that a corrupt or
//  absent entry degrades to an empty history rather than throwing.
// ----------------------------------------------------------------------------

import { beforeEach, describe, expect, it } from 'vitest';
import { MAX_HISTORY_SIZE, appendPlayedId, loadPlayedIds, resetPlayedHistory } from './playedHistory';

const STORAGE_KEY = 'qs.playedTemplates.v1';

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

describe('loadPlayedIds', () => {
  it('returns [] when nothing has been stored yet', () => {
    expect(loadPlayedIds()).toEqual([]);
  });

  it('returns the stored ids in order', () => {
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify(['a', 'b', 'c']));
    expect(loadPlayedIds()).toEqual(['a', 'b', 'c']);
  });

  it('returns [] for corrupt (non-JSON) storage instead of throwing', () => {
    fakeStorage.setItem(STORAGE_KEY, 'not json{{{');
    expect(() => loadPlayedIds()).not.toThrow();
    expect(loadPlayedIds()).toEqual([]);
  });

  it('returns [] when the stored value is not an array of strings', () => {
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify({ nickname: 'not-history-shaped' }));
    expect(loadPlayedIds()).toEqual([]);

    fakeStorage.setItem(STORAGE_KEY, JSON.stringify(['ok', 42, 'also-ok']));
    expect(loadPlayedIds()).toEqual([]);
  });

  it('stores ids ONLY - no words, no PII, no timestamps (AC-06)', () => {
    appendPlayedId('some-template-id');
    const raw = fakeStorage.getItem(STORAGE_KEY);
    expect(raw).not.toBeNull();
    const parsed: unknown = JSON.parse(raw ?? '[]');
    expect(parsed).toEqual(['some-template-id']);
  });
});

describe('appendPlayedId', () => {
  it('appends a new id to the end', () => {
    appendPlayedId('a');
    appendPlayedId('b');
    expect(loadPlayedIds()).toEqual(['a', 'b']);
  });

  it('dedupes and moves a re-played id to the end (most-recently-played last)', () => {
    appendPlayedId('a');
    appendPlayedId('b');
    appendPlayedId('c');
    appendPlayedId('a');
    expect(loadPlayedIds()).toEqual(['b', 'c', 'a']);
  });

  it('never throws when storage is unavailable', () => {
    (globalThis as unknown as { window: Window }).window = {} as unknown as Window;
    expect(() => appendPlayedId('a')).not.toThrow();
  });

  it('caps the stored history at MAX_HISTORY_SIZE, dropping the oldest first', () => {
    for (let i = 0; i < MAX_HISTORY_SIZE + 5; i += 1) {
      appendPlayedId(`template-${i}`);
    }
    const stored = loadPlayedIds();
    expect(stored).toHaveLength(MAX_HISTORY_SIZE);
    // The oldest entries (template-0..template-4) were dropped; the most
    // recent MAX_HISTORY_SIZE remain, in order.
    expect(stored[stored.length - 1]).toBe(`template-${MAX_HISTORY_SIZE + 4}`);
    expect(stored).not.toContain('template-0');
  });
});

describe('resetPlayedHistory', () => {
  it('clears the stored history', () => {
    appendPlayedId('a');
    appendPlayedId('b');
    resetPlayedHistory();
    expect(loadPlayedIds()).toEqual([]);
  });

  it('never throws when storage is unavailable', () => {
    (globalThis as unknown as { window: Window }).window = {} as unknown as Window;
    expect(() => resetPlayedHistory()).not.toThrow();
  });
});
