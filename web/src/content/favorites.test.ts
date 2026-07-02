// ----------------------------------------------------------------------------
//  favorites.test.ts - Vitest coverage for the device-local favorites list
//  (story-selection/06, see ./favorites.ts).
//
//  Mirrors playedHistory.test.ts's approach: the suite's vitest.config.ts runs
//  in the `node` environment (no DOM), so there is no real `window.localStorage`
//  - this file stubs a tiny in-memory Storage-shaped fake onto `globalThis.window`
//  before each test (no jsdom dependency needed for this one module). Asserts
//  the stored SHAPE (an array of { templateId, title } and nothing else, AC-05),
//  the newest-first recency order (AC-02), the toggle/dedupe rules (AC-01), and
//  that a corrupt or absent entry degrades to an empty list rather than throwing.
// ----------------------------------------------------------------------------

import { beforeEach, describe, expect, it } from 'vitest';
import {
  MAX_FAVORITES,
  addFavorite,
  isFavorite,
  loadFavorites,
  removeFavorite,
  toggleFavorite,
  type FavoriteEntry,
} from './favorites';

const STORAGE_KEY = 'qs.favorites.v1';

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

const wobblyWizard: FavoriteEntry = { templateId: 'wobbly-wizard', title: 'The Wobbly Wizard & the Golden Sock' };
const secondTale: FavoriteEntry = { templateId: 'second-tale', title: 'A Second Silly Tale' };
const thirdTale: FavoriteEntry = { templateId: 'third-tale', title: 'A Third Silly Tale' };

describe('loadFavorites', () => {
  it('returns [] when nothing has been favorited yet', () => {
    expect(loadFavorites()).toEqual([]);
  });

  it('returns [] for corrupt (non-JSON) storage instead of throwing', () => {
    fakeStorage.setItem(STORAGE_KEY, 'not json{{{');
    expect(() => loadFavorites()).not.toThrow();
    expect(loadFavorites()).toEqual([]);
  });

  it('returns [] when the stored value is not an array of well-shaped entries', () => {
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify({ nickname: 'not-favorites-shaped' }));
    expect(loadFavorites()).toEqual([]);

    // A plain string array (the OLD playedHistory shape) is not a favorites shape.
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify(['wobbly-wizard']));
    expect(loadFavorites()).toEqual([]);

    // Missing a required field.
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify([{ templateId: 'wobbly-wizard' }]));
    expect(loadFavorites()).toEqual([]);

    // A non-string field.
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify([{ templateId: 'wobbly-wizard', title: 42 }]));
    expect(loadFavorites()).toEqual([]);
  });

  it('rejects entries whose fields are whitespace-only', () => {
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify([{ templateId: '   ', title: 'Blank id' }]));
    expect(loadFavorites()).toEqual([]);
    fakeStorage.setItem(STORAGE_KEY, JSON.stringify([{ templateId: 'wobbly-wizard', title: '  ' }]));
    expect(loadFavorites()).toEqual([]);
  });

  it('normalizes each entry to exactly { templateId, title }, dropping stray fields (AC-05)', () => {
    // A pre-existing / corrupted entry carrying extra fields must not survive a
    // load - otherwise a later removeFavorite would write the junk back.
    fakeStorage.setItem(
      STORAGE_KEY,
      JSON.stringify([{ templateId: 'wobbly-wizard', title: 'The Wobbly Wizard', words: ['secret'], sneaky: true }]),
    );
    expect(loadFavorites()).toEqual([{ templateId: 'wobbly-wizard', title: 'The Wobbly Wizard' }]);
    // And the junk cannot round-trip back to storage on a subsequent write.
    removeFavorite('some-other-id');
    const raw = fakeStorage.getItem(STORAGE_KEY);
    expect(raw).not.toBeNull();
    const stored: unknown = JSON.parse(raw ?? '[]');
    expect(stored).toEqual([{ templateId: 'wobbly-wizard', title: 'The Wobbly Wizard' }]);
  });

  it('never throws when storage is unavailable', () => {
    (globalThis as unknown as { window: Window }).window = {} as unknown as Window;
    expect(() => loadFavorites()).not.toThrow();
    expect(loadFavorites()).toEqual([]);
  });
});

describe('addFavorite', () => {
  it('adds a new favorite to the front of the list', () => {
    addFavorite(wobblyWizard);
    addFavorite(secondTale);
    // secondTale was favorited most recently, so it leads (AC-02).
    expect(loadFavorites()).toEqual([secondTale, wobblyWizard]);
  });

  it('persists to localStorage', () => {
    addFavorite(wobblyWizard);
    const raw = fakeStorage.getItem(STORAGE_KEY);
    expect(raw).not.toBeNull();
    expect(JSON.parse(raw ?? '[]')).toEqual([wobblyWizard]);
  });

  it('dedupes: re-adding an existing favorite moves it to the front with no duplicate', () => {
    addFavorite(wobblyWizard);
    addFavorite(secondTale);
    addFavorite(thirdTale);
    addFavorite(wobblyWizard);
    const favorites = loadFavorites();
    expect(favorites).toEqual([wobblyWizard, thirdTale, secondTale]);
    expect(favorites.filter((f) => f.templateId === wobblyWizard.templateId)).toHaveLength(1);
  });

  it('stores ONLY templateId + title - no words, no PII, no timestamps (AC-05)', () => {
    addFavorite(wobblyWizard);
    const raw = fakeStorage.getItem(STORAGE_KEY);
    const parsed: unknown = JSON.parse(raw ?? '[]');
    expect(Array.isArray(parsed)).toBe(true);
    const entry = (parsed as unknown[])[0] as Record<string, unknown>;
    expect(Object.keys(entry).sort()).toEqual(['templateId', 'title']);
  });

  it('never throws when storage is unavailable', () => {
    (globalThis as unknown as { window: Window }).window = {} as unknown as Window;
    expect(() => addFavorite(wobblyWizard)).not.toThrow();
  });

  it('caps the stored list at MAX_FAVORITES, dropping the oldest (tail) first', () => {
    for (let i = 0; i < MAX_FAVORITES + 5; i += 1) {
      addFavorite({ templateId: `template-${i}`, title: `Tale ${i}` });
    }
    const stored = loadFavorites();
    expect(stored).toHaveLength(MAX_FAVORITES);
    // Most recently added leads; the oldest (template-0..template-4) were dropped.
    expect(stored[0].templateId).toBe(`template-${MAX_FAVORITES + 4}`);
    expect(stored.some((f) => f.templateId === 'template-0')).toBe(false);
  });
});

describe('isFavorite', () => {
  it('reflects the current favorited state', () => {
    expect(isFavorite(wobblyWizard.templateId)).toBe(false);
    addFavorite(wobblyWizard);
    expect(isFavorite(wobblyWizard.templateId)).toBe(true);
  });
});

describe('removeFavorite', () => {
  it('removes a favorited entry and persists the change', () => {
    addFavorite(wobblyWizard);
    addFavorite(secondTale);
    removeFavorite(wobblyWizard.templateId);
    expect(loadFavorites()).toEqual([secondTale]);
    expect(isFavorite(wobblyWizard.templateId)).toBe(false);
  });

  it('is a no-op when the id was never favorited', () => {
    addFavorite(wobblyWizard);
    expect(() => removeFavorite('never-favorited')).not.toThrow();
    expect(loadFavorites()).toEqual([wobblyWizard]);
  });

  it('never throws when storage is unavailable', () => {
    (globalThis as unknown as { window: Window }).window = {} as unknown as Window;
    expect(() => removeFavorite(wobblyWizard.templateId)).not.toThrow();
  });

  it('clearing storage clears favorites', () => {
    addFavorite(wobblyWizard);
    expect(loadFavorites()).toEqual([wobblyWizard]);
    fakeStorage.clear();
    expect(loadFavorites()).toEqual([]);
  });
});

describe('toggleFavorite', () => {
  it('favorites an unfavorited entry and returns true (now favorited)', () => {
    expect(toggleFavorite(wobblyWizard)).toBe(true);
    expect(isFavorite(wobblyWizard.templateId)).toBe(true);
  });

  it('unfavorites an already-favorited entry and returns false (now unfavorited)', () => {
    addFavorite(wobblyWizard);
    expect(toggleFavorite(wobblyWizard)).toBe(false);
    expect(isFavorite(wobblyWizard.templateId)).toBe(false);
  });

  it('flips back and forth cleanly across repeated taps', () => {
    expect(toggleFavorite(wobblyWizard)).toBe(true);
    expect(toggleFavorite(wobblyWizard)).toBe(false);
    expect(toggleFavorite(wobblyWizard)).toBe(true);
    expect(loadFavorites()).toEqual([wobblyWizard]);
  });
});
