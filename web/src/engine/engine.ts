// ----------------------------------------------------------------------------
//  engine.ts - collect + assemble orchestration, independent of the active
//  mode (game-modes/01, AC-02, AC-03, AC-05).
//
//  This is the "engine" half of "one engine, many thin modes": it collects
//  words for a template's blanks and hands them to template-model's
//  assemble() to produce the final story. It does this the SAME way no
//  matter which ModeConfig (mode.ts) is active - a mode only changes what a
//  player sees and when the reveal happens; it never changes how collection
//  or assembly work (AC-02, AC-03). Any future mode is therefore a new
//  ModeConfig value, not a new branch in this file. If implementing a mode
//  ever forces a change to collectWord/assembleStory or to template.ts, that
//  is an abstraction leak (playbook Principle 2) - stop and flag it rather
//  than patching around it.
//
//  Collection model: `collectWord` records ONE submitted word against ONE
//  blank id into a `CollectedWords` map (blankId -> SubmittedWord), keyed by
//  blank id rather than relying on array order. This keeps collection
//  resilient to out-of-order submission (group play: players answer
//  concurrently) while still producing an ordered SubmittedWord[] for
//  assemble() via `toOrderedWords`, which walks the template's blanks in
//  body order (matching assemble()'s own ordering contract - see
//  assemble.ts).
//
//  Safety filter seam (AC-05): the REAL profanity/safety filter is owned by
//  child-safety/01 and lives server-side in api/src/Safety/ (.NET) - this is
//  pure web TS and must NOT import any api/ code or reimplement the check.
//  Instead, `collectWord` accepts an OPTIONAL injectable async hook:
//
//      type SafetyCheck = (word: string) => Promise<{ ok: boolean; message?: string }>
//
//  The call site lives HERE, on the collection path, so every mode inherits
//  it for free - a mode can never bypass the filter because no mode calls
//  collectWord differently. For word-bank answers (ModeAnswerAxis ===
//  'word-bank', not implemented in Slice 1), the safety check is skipped:
//  word-bank words come from curated, pre-vetted lists (template.wordBank),
//  not free text, so there is nothing to filter (this is mode-aware
//  behavior the engine decides from `mode.answer`, not something a mode
//  re-implements). Callers (single-player, group-play) are responsible for
//  wiring the real check - this module only defines and calls the seam.
//
//  Who imports this: single-player/group-play (drive a round through collect
//  -> assemble), FillBlank (game-modes/02, calls collectWord per submission).
// ----------------------------------------------------------------------------

import { assemble, type AssembledStory, type SubmittedWord } from './assemble';
import { getBlanks, type Template } from './template';
import type { ModeConfig } from './mode';

/**
 * The injectable async safety-check hook (AC-05). Returns `ok: false` with an
 * optional friendly `message` when a free-text word should be rejected.
 * Modeled as a function type (not a class) so callers can pass anything from
 * a stub (tests) to a real REST-backed check (the eventual child-safety
 * client) without this module knowing the difference.
 */
export type SafetyCheck = (word: string) => Promise<{ ok: boolean; message?: string }>;

/** The outcome of `collectWord`: either the word was recorded, or it was rejected by the safety check. */
export type CollectResult =
  | { accepted: true }
  | { accepted: false; message: string };

/**
 * Words collected so far for a template's blanks, keyed by blank id. Keying
 * by id (not array position) makes collection order-independent: group-play
 * players can submit concurrently and each submission still lands against
 * the correct blank.
 */
export type CollectedWords = Map<string, SubmittedWord>;

/** Creates an empty collection ready to be filled in via `collectWord`. */
export function createCollection(): CollectedWords {
  return new Map();
}

/**
 * Records one player's word against one blank (AC-02), running it through
 * the safety check first when the mode answers via free text (AC-05).
 *
 * - When `mode.answer === 'free-text'` and a `safetyCheck` hook is supplied,
 *   the word is checked before being recorded; a rejected word is never
 *   added to `collected` and the caller gets `{ accepted: false, message }`
 *   back to show the player.
 * - When `mode.answer === 'word-bank'`, the safety check is skipped (the
 *   word came from a curated bank, not free text) - see file header.
 * - When no `safetyCheck` is supplied (e.g. unit tests, or a caller that
 *   checks elsewhere), the word is recorded unchecked; production call sites
 *   are expected to always pass the real hook for free-text modes.
 *
 * Mutates and returns `collected` for convenient chaining; does not mutate
 * `template`.
 */
export async function collectWord(
  collected: CollectedWords,
  template: Template,
  mode: ModeConfig,
  blankId: string,
  submission: SubmittedWord,
  safetyCheck?: SafetyCheck,
): Promise<CollectResult> {
  const knownBlankIds = new Set(getBlanks(template).map((b) => b.id));
  if (!knownBlankIds.has(blankId)) {
    return { accepted: false, message: `Unknown blank id "${blankId}" for template "${template.id}".` };
  }

  if (mode.answer === 'free-text' && safetyCheck) {
    const result = await safetyCheck(submission.word);
    if (!result.ok) {
      return { accepted: false, message: result.message ?? 'That word is not allowed here.' };
    }
  }

  collected.set(blankId, submission);
  return { accepted: true };
}

/**
 * Converts a `CollectedWords` map into the ordered `SubmittedWord[]` that
 * `assemble()` expects, walking the template's blanks in body order (the
 * same order assemble() itself uses - see assemble.ts). Blanks with no
 * collected word yet are simply omitted, which assemble() already handles
 * via its documented word-count-mismatch rule (fewer words than blanks).
 */
export function toOrderedWords(template: Template, collected: CollectedWords): SubmittedWord[] {
  return getBlanks(template)
    .map((b) => collected.get(b.id))
    .filter((word): word is SubmittedWord => word !== undefined);
}

/**
 * Returns true once every blank in the template has a collected word -
 * useful for a caller (FillBlank, group-play) to know when to move on to
 * assembly, regardless of which mode is active (AC-02).
 */
export function isCollectionComplete(template: Template, collected: CollectedWords): boolean {
  return getBlanks(template).every((b) => collected.has(b.id));
}

/**
 * Assembles the final story from whatever has been collected so far (AC-02).
 * This is a thin pass-through to template-model's `assemble()` - the engine
 * never reimplements assembly, only orders the collected words for it. Safe
 * to call before collection is complete (e.g. a 'progressively' reveal mode
 * assembling partial results) since assemble() itself is non-throwing on a
 * length mismatch.
 */
export function assembleStory(template: Template, collected: CollectedWords): AssembledStory {
  return assemble(template, toOrderedWords(template, collected));
}
