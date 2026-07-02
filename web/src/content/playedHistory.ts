// ----------------------------------------------------------------------------
//  playedHistory.ts - the SOLO device-local played-template history
//  (story-selection/03).
//
//  This is the storage half of the freshness-rotation stage (see ./fresh.ts,
//  the PURE selection rule): it remembers which template ids a solo player on
//  THIS device has already played, so Solo.tsx's random pick can be filtered
//  through selectFreshOrRecycle before the round begins. It is a small,
//  device-local convenience, in the same posture identity.ts already
//  documents (build/host-identity) and the one keepsake-gallery/03 plans for
//  its local gallery: anonymous, tied only to this browser's storage, no
//  account, no server round-trip, no sync across devices (AC-01).
//
//  Stored shape (AC-06, README section 6 - no PII): an ORDERED array of
//  template id STRINGS and NOTHING ELSE - no words a player typed, no
//  nicknames, no timestamps that could tie a play session to a person. Order
//  is oldest-played-first / most-recently-played-last, which is exactly the
//  shape ./fresh.ts's recycle step wants (least-recently-played first).
//  Clearing browser storage simply resets freshness back to "nothing played
//  yet" - that is expected, documented behavior, not a bug (AC-06).
//
//  Capped (AC-06's "cannot grow unbounded" spirit, mirroring keepsake-
//  gallery/03's cap discussion): appendPlayedId dedupes an id that is already
//  present (moving it to the end, since it was just played again after a
//  recycle) and the whole list is trimmed to MAX_HISTORY_SIZE entries,
//  dropping the OLDEST first - a generous ceiling comfortably above today's
//  seed library size so ordinary play never trims anything; it exists purely
//  as a defensive backstop against unbounded growth, not a limit anyone is
//  expected to hit.
//
//  Robustness: every localStorage access is wrapped in try/catch (it can
//  throw or be absent in private-browsing modes, disabled storage, quota, or
//  SSR) and the parsed JSON is VALIDATED to be an array of strings before
//  it is trusted - a corrupt or absent entry simply resets to "no history"
//  rather than throwing or being read blindly (same discipline as
//  identity.ts's loadIdentity).
//
//  Pure storage, NOT a selection rule: this module has zero knowledge of
//  templates, safety, or length - it only reads/writes an ordered id list.
//  ./fresh.ts (the pure selection stage) has zero knowledge of localStorage in
//  the other direction - Solo.tsx is the only place that wires the two
//  together, keeping the pure pipeline stage unit-testable without a DOM.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { seedLibrary } from './seedLibrary';

// Versioned key: bump the suffix if the stored shape ever changes, so an old
// entry is simply ignored (loadPlayedIds returns []) rather than mis-read.
const STORAGE_KEY = 'qs.playedTemplates.v1';

/**
 * A generous ceiling on how many template ids the history can hold, well
 * above today's seed library size (see the file header) - a defensive
 * backstop, not a limit ordinary play should ever reach. Recomputed from the
 * live library length rather than a hardcoded number so it never needs a
 * manual bump when a template is added or removed.
 */
export const MAX_HISTORY_SIZE = seedLibrary.length;

/** True when `value` is an array containing only non-empty strings. */
function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((entry) => typeof entry === 'string' && entry.length > 0);
}

/**
 * Loads the played-template id history from device-local storage, oldest
 * first / most-recently-played last. Returns `[]` when there is none, storage
 * is unavailable, or the stored value fails validation - never throws.
 */
export function loadPlayedIds(): string[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) {
      return [];
    }

    // Parse into `unknown` and narrow by hand - never trust the stored shape.
    const parsed: unknown = JSON.parse(raw);
    if (!isStringArray(parsed)) {
      return [];
    }

    // Defensive backstop (see file header): even a corrupted/oversized entry
    // never grows the in-memory list past the cap, keeping only the most
    // recently played ids.
    return parsed.length > MAX_HISTORY_SIZE ? parsed.slice(parsed.length - MAX_HISTORY_SIZE) : parsed;
  } catch {
    // Storage unavailable / disabled, quota, or malformed JSON - treat as "none".
    return [];
  }
}

/**
 * Records `id` as just-played (AC-01): reads the current history, moves `id`
 * to the end if it was already present (dedupe - it is the most recently
 * played again after a recycle) or appends it if new, trims to
 * MAX_HISTORY_SIZE from the front (oldest dropped first) if needed, and
 * writes the result back. Silently no-ops on a storage failure - persistence
 * here is a convenience for freshness, never a requirement gameplay depends
 * on to proceed (mirrors identity.ts's saveIdentity).
 */
export function appendPlayedId(id: string): void {
  try {
    const current = loadPlayedIds();
    const next = [...current.filter((existing) => existing !== id), id];
    const capped = next.length > MAX_HISTORY_SIZE ? next.slice(next.length - MAX_HISTORY_SIZE) : next;
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(capped));
  } catch {
    // Ignore: a failed write just means freshness does not remember this play.
  }
}

/**
 * Clears the device's played-template history (e.g. an explicit "reset my
 * randomizer" affordance, should one ever be added - no such UI exists yet).
 * Silently no-ops on a storage failure, same posture as appendPlayedId.
 */
export function resetPlayedHistory(): void {
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Ignore: a failed removal just means stale history stays around.
  }
}
