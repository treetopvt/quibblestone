// ----------------------------------------------------------------------------
//  favorites.ts - the device-local FAVORITES list (story-selection/06,
//  "Favorite a story and replay it (device-local)").
//
//  This is the storage half of the favorites feature: a small localStorage-
//  backed module remembering which story templates a player on THIS device
//  has starred, so a star control on Reveal / Round Complete can toggle a
//  favorite, and a Favorites screen can list them for a one-tap replay. It
//  mirrors the posture ./playedHistory.ts already documents (the SAME
//  device-local convenience identity.ts and story-selection/03 use):
//  anonymous, tied only to this browser's storage, no account, no server
//  round-trip, no sync across devices (AC-05).
//
//  Stored shape (AC-05, README section 6 - no PII): an ORDERED array of
//  `{ templateId, title }` objects and NOTHING ELSE - no words a player
//  typed, no nicknames, no timestamps. `title` is a small CACHED display
//  string (so the Favorites list can show a name even if the seed library
//  ever drops that id) - it carries no personal information, just the
//  story's own title. Order is MOST-RECENTLY-FAVORITED FIRST (newest at the
//  front) - the exact order AC-02's Favorites list wants, so the list screen
//  can render `loadFavorites()` directly with no re-sorting.
//
//  Capped (mirroring playedHistory.ts's MAX_HISTORY_SIZE): a favorite is
//  always ONE of the templates in the bundled seed library, so the list can
//  never hold more distinct entries than the library itself - a generous,
//  self-computing ceiling that exists purely as a defensive backstop against
//  unbounded growth (e.g. a corrupted entry that grew past reason), not a
//  limit an ordinary player is expected to hit. addFavorite drops the OLDEST
//  entries (the back of the newest-first list) first if the cap is ever hit.
//
//  Robustness: every localStorage access is wrapped in try/catch (it can
//  throw or be absent in private-browsing modes, disabled storage, quota, or
//  SSR) and the parsed JSON is VALIDATED by hand - each entry must be an
//  object with non-empty string `templateId` and `title` fields and nothing
//  is trusted blindly - a corrupt or absent entry simply resets to "no
//  favorites" rather than throwing (same discipline as identity.ts's
//  loadIdentity and playedHistory.ts's loadPlayedIds). Every WRITE (add /
//  remove) silently no-ops on a storage failure - favoriting is a device
//  convenience, never a requirement gameplay depends on to proceed.
//
//  Pure storage, NOT a selection rule: this module has zero knowledge of the
//  freshness pipeline, safety, or length - it only reads/writes an ordered
//  list of { templateId, title }. The explicit-replay bypass (AC-04: playing
//  a favorite skips freshness and does NOT re-stamp playedHistory.ts) lives at
//  the call sites (Solo.tsx, App.tsx's group host-pick handler), not here.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { seedLibrary } from './seedLibrary';

// Versioned key: bump the suffix if the stored shape ever changes, so an old
// entry is simply ignored (loadFavorites returns []) rather than mis-read.
const STORAGE_KEY = 'qs.favorites.v1';

/**
 * A generous ceiling on how many favorites can be stored, computed from the
 * live seed library length (see the file header) - a favorite is always one
 * of the library's own templates, so the list can never legitimately need
 * more entries than that. A defensive backstop, not a limit ordinary play
 * should ever reach; recomputed from the live library rather than a
 * hardcoded number so it never needs a manual bump when a template is added
 * or removed.
 */
export const MAX_FAVORITES = seedLibrary.length;

/** One favorited story template: its id plus a cached title for display (AC-05 - no other fields). */
export interface FavoriteEntry {
  templateId: string;
  title: string;
}

/**
 * True when `value` is a NON-BLANK string. Whitespace-only values (e.g. `'   '`)
 * are rejected so corrupted storage can never yield a favorite with a blank
 * templateId/title, matching the server's IsNullOrWhiteSpace convention for the
 * explicit-pick templateId.
 */
function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

/** True when `value` is a well-shaped FavoriteEntry (AC-05: templateId + title only). */
function isFavoriteEntry(value: unknown): value is FavoriteEntry {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  return isNonEmptyString(record.templateId) && isNonEmptyString(record.title);
}

/** True when `value` is an array containing only well-shaped FavoriteEntry objects. */
function isFavoriteEntryArray(value: unknown): value is FavoriteEntry[] {
  return Array.isArray(value) && value.every(isFavoriteEntry);
}

/**
 * Loads the favorites list from device-local storage, most-recently-favorited
 * first. Returns `[]` when there are none, storage is unavailable, or the
 * stored value fails validation - never throws.
 */
export function loadFavorites(): FavoriteEntry[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) {
      return [];
    }

    // Parse into `unknown` and narrow by hand - never trust the stored shape.
    const parsed: unknown = JSON.parse(raw);
    if (!isFavoriteEntryArray(parsed)) {
      return [];
    }

    // Normalize each entry to EXACTLY { templateId, title } (AC-05: nothing
    // else). Any stray fields from corruption, an older stored shape, or a
    // manual edit are dropped HERE, so they can never be preserved in memory
    // and written back on a later add/remove - the "ids + title only" storage
    // contract holds even for pre-existing junk.
    const normalized = parsed.map((entry) => ({ templateId: entry.templateId, title: entry.title }));

    // Defensive backstop (see file header): even a corrupted/oversized entry
    // never grows the in-memory list past the cap - the newest-first order
    // means the OLDEST entries (the tail) are the ones dropped.
    return normalized.length > MAX_FAVORITES ? normalized.slice(0, MAX_FAVORITES) : normalized;
  } catch {
    // Storage unavailable / disabled, quota, or malformed JSON - treat as "none".
    return [];
  }
}

/** Whether `templateId` is currently favorited on this device. */
export function isFavorite(templateId: string): boolean {
  return loadFavorites().some((entry) => entry.templateId === templateId);
}

/**
 * Favorites `entry` (AC-01, AC-02): moves it to the FRONT of the list (newest
 * first) whether it is new or was already favorited (a re-add refreshes its
 * recency rather than creating a duplicate row), trims to MAX_FAVORITES from
 * the back (oldest dropped first) if needed, and writes the result back.
 * Silently no-ops on a storage failure - persistence here is a convenience,
 * never a requirement (mirrors identity.ts's saveIdentity).
 */
export function addFavorite(entry: FavoriteEntry): void {
  try {
    const current = loadFavorites();
    const next = [entry, ...current.filter((existing) => existing.templateId !== entry.templateId)];
    const capped = next.length > MAX_FAVORITES ? next.slice(0, MAX_FAVORITES) : next;
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(capped));
  } catch {
    // Ignore: a failed write just means this device does not remember the favorite.
  }
}

/**
 * Unfavorites `templateId` (AC-01): removes it from the list if present and
 * writes the result back. A no-op (not an error) if it was never favorited.
 * Silently no-ops on a storage failure, same posture as addFavorite.
 */
export function removeFavorite(templateId: string): void {
  try {
    const current = loadFavorites();
    const next = current.filter((entry) => entry.templateId !== templateId);
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    // Ignore: a failed write just means a stale favorite stays around.
  }
}

/**
 * Toggles `entry`'s favorited state (AC-01): unfavorites it if already
 * favorited, otherwise favorites it. Returns the NEW state (true = now
 * favorited) so a caller (e.g. FavoriteStarButton) can drive its own local
 * display state directly off the return value without a second read.
 */
export function toggleFavorite(entry: FavoriteEntry): boolean {
  if (isFavorite(entry.templateId)) {
    removeFavorite(entry.templateId);
    return false;
  }
  addFavorite(entry);
  return true;
}
