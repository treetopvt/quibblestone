// ----------------------------------------------------------------------------
//  wordBankJumble.ts - the FREE, deterministic "Fresh Runes" reshuffle for Word
//  Bank mode (game-modes/07, AC-01/02/06/07, the non-AI layer).
//
//  What this answers: "given the curated word pool for a blank's category and
//  the words already offered so far, which FRESH subset should the jumble show
//  next?" It re-samples a DIFFERENT in-category subset from the growing,
//  already-vetted pool so a player who does not like the current options can
//  tap "Fresh runes" for new ones - instant, offline, and FREE (AC-02). It is
//  also the always-safe fallback the AI cost gate's circuit-breaker degrades to
//  (ai-on-demand-generation/05 / game-modes/07 AC-03): when the AI path is
//  unavailable, quota-exhausted, or breaker-open, the caller falls back to THIS.
//
//  Why pure (like ./wordBankOffering.ts and ./fresh.ts): data in, data out, no
//  React / fetch / SignalR, no mutation of the input. Deterministic given its
//  inputs (no Math.random) so it is unit-testable in a plain Vitest .ts file
//  (wordBankJumble.test.ts) and so the same inputs always yield the same set.
//
//  Curated words skip the free-text profanity filter here EXACTLY as
//  game-modes/04 documents (they come from pre-vetted lists - README section 6);
//  the family-safe gate is applied UPSTREAM at content-selection time
//  (./wordBankOffering.ts decides which templates' banks are offered), never a
//  per-tap check here. The ONE place a jumble word must pass the safety filter
//  is the AI layer, which is a different SOURCE handled server-side by the gate
//  (game-modes/07 AC-04); this deterministic layer never touches it.
//
//  The "cumulative shown" contract (AC-02, favor not-just-shown + cycle):
//  callers pass EVERY word offered so far for this blank as `alreadyShown`. The
//  helper hands back the not-yet-shown words first (a genuinely fresh set each
//  tap), and only once the whole pool has been seen does it CYCLE - refilling
//  from the start rather than ever returning an empty list. So a player walks
//  the entire curated pool for the category before any word repeats.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import type { BlankCategory, WordBankEntry } from '../engine/template';

/**
 * The default number of options a jumble offers at once. The jumble payload is
 * a small, tappable set (ADR 0001 sizes the AI jumble at ~6-10 words; the free
 * reshuffle mirrors it so the two sources look identical to the player). A
 * caller may pass its own `size`; a category pool smaller than `size` simply
 * offers everything it has (never pads, never errors - AC-02).
 */
export const DEFAULT_OFFERING_SIZE = 8;

/**
 * Normalizes a word for "have we shown this already?" comparisons: trimmed and
 * lower-cased, so casing/whitespace differences never leak a duplicate into a
 * "fresh" set. Output words keep their authored casing - this is only the
 * comparison key.
 */
function normalize(word: string): string {
  return word.trim().toLowerCase();
}

/**
 * The unique, in-category words from `pool`, in first-seen (authored) order. A
 * curated bank may legitimately repeat a word within a category (see
 * WordBankAnswer's composite-key note); the jumble de-duplicates so a "fresh"
 * set never shows the same word twice. Pure - never mutates `pool`.
 */
function uniqueWordsForCategory(
  pool: readonly WordBankEntry[],
  category: BlankCategory,
): string[] {
  const seen = new Set<string>();
  const words: string[] = [];
  for (const entry of pool) {
    if (entry.category !== category) {
      continue;
    }
    const key = normalize(entry.word);
    if (key.length === 0 || seen.has(key)) {
      continue;
    }
    seen.add(key);
    words.push(entry.word);
  }
  return words;
}

/**
 * Returns the next FRESH in-category subset for a jumble (game-modes/07 AC-02).
 *
 * Given the curated `pool`, the blank's `category`, and the cumulative set of
 * words `alreadyShown` for this blank so far, it returns up to `size` words,
 * favoring words NOT in `alreadyShown` (a genuinely different set each tap). If
 * fewer than `size` fresh words remain, it fills the remainder from the pool
 * from the start - so the list CYCLES gracefully (never empty, never an error)
 * once the whole category pool has been walked (AC-02). Deterministic: the same
 * `(pool, category, alreadyShown, size)` always yields the same subset.
 *
 * Returns the plain word strings (what the surface displays and submits via the
 * standard `collectWord` path - game-modes/07 AC-06); duplicates in the pool are
 * collapsed. An empty or mismatched-category pool yields an empty array, which
 * the surface renders as "nothing to jumble" and soft-disables the action
 * (AC-02: never an empty list forced on the player, never a throw).
 */
export function nextOptions(
  pool: readonly WordBankEntry[],
  category: BlankCategory,
  alreadyShown: readonly string[],
  size: number = DEFAULT_OFFERING_SIZE,
): string[] {
  const candidates = uniqueWordsForCategory(pool, category);
  if (candidates.length === 0) {
    return [];
  }

  // Offer at most the whole pool - a small category never pads or errors.
  const take = Math.min(Math.max(size, 0), candidates.length);
  if (take === 0) {
    return [];
  }

  const shown = new Set(alreadyShown.map(normalize));
  const fresh = candidates.filter((word) => !shown.has(normalize(word)));

  // Enough not-yet-shown words to fill the set: hand back a fully fresh subset.
  if (fresh.length >= take) {
    return fresh.slice(0, take);
  }

  // The pool is (nearly) exhausted: take every fresh word, then CYCLE - refill
  // the remainder from the pool head - so the action always yields a full,
  // non-empty set rather than dwindling to nothing (AC-02 "cycles gracefully").
  const freshKeys = new Set(fresh.map(normalize));
  const refill = candidates.filter((word) => !freshKeys.has(normalize(word)));
  return [...fresh, ...refill].slice(0, take);
}
