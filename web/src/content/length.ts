// ----------------------------------------------------------------------------
//  length.ts - the story-LENGTH content stage (story-selection/01).
//
//  This module is the SECOND content-selection stage in QuibbleStone's one
//  selection pipeline, sitting ALONGSIDE the family-safe gate (./familySafe.ts):
//  given a set of templates (already family-safe-gated by the caller) and a
//  length preference, it returns which templates fit that preference. A
//  template's length class (quick | full) is DERIVED, never authored - it comes
//  purely from how many blanks the template has (getBlanks(template).length)
//  against the SINGLE threshold constant below. There is NO new authored tag and
//  NO change to the Template schema or the engine (story-selection/01 AC-01).
//
//  The pipeline, in fixed order (story-selection/01 AC-03 mirrors it server-side):
//    family-safe gate (selectTemplates) -> length filter (selectByLength) ->
//    empty-pool fallback -> random pick.
//  The family-safe gate ALWAYS runs FIRST; this stage only ever sees the subset
//  that gate already allowed, so relaxing length can never widen the safety set.
//
//  Length classes (AC-01), derived from blank count:
//    - 'quick' : QUICK_MAX_BLANKS (6) blanks or fewer - a short story a solo
//                player or a tiny group finishes fast.
//    - 'full'  : more than QUICK_MAX_BLANKS blanks - the longer stories that
//                give every player in a bigger room multiple turns.
//  One exported threshold constant is the single source of truth for the split
//  (AC-01) so the boundary lives in exactly one place, not scattered at call
//  sites.
//
//  Empty-pool fallback (AC-06): if a length preference would select NOTHING
//  from the family-safe pool (e.g. 'quick' when the pool happens to hold only
//  full stories), selection must DEGRADE to the family-safe pool rather than
//  fail the round - a longer story is a fine outcome, an errored round is not.
//  selectByLengthOrFallback below is the compose helper that expresses that.
//
//  Pure by construction: data in, data out. No React, no fetch, no SignalR, no
//  mutation of the input array - safe to unit test in isolation (length.test.ts)
//  and safe to import from any layer.
//
//  ======================== KEEP THE SERVER MIRROR IN SYNC ====================
//  api/src/Safety/LengthContentSelector.cs is the C# MIRROR of this module: it
//  applies the SAME split over TemplateCatalogEntry.BlankCount with the SAME
//  threshold and the SAME empty-pool fallback. There is no shared source and no
//  codegen - the web stage and the C# stage are kept in behavioral lockstep BY
//  HAND. Change the threshold or the filter/fallback behavior here and you MUST
//  change LengthContentSelector.cs to match, or solo and group play will offer
//  different pools for the same preference.
//  ============================================================================
// ----------------------------------------------------------------------------

import { getBlanks, type Template } from '../engine/template';

/**
 * The SINGLE length-class threshold (story-selection/01 AC-01): a template with
 * this many blanks or FEWER is 'quick'; more than this is 'full'. Exported so
 * both classifyLength below and any caller reason about the boundary through one
 * token instead of a magic number. Mirrored by LengthContentSelector.QuickMaxBlanks
 * on the server (keep the two equal by hand).
 */
export const QUICK_MAX_BLANKS = 6;

/** A template's derived length class - never authored, always computed from blank count. */
export type LengthClass = 'quick' | 'full';

/**
 * A caller's length preference. 'any' (the default everywhere until a UI asks
 * otherwise, story-selection/02) means "do not filter by length at all".
 */
export type LengthPreference = 'quick' | 'full' | 'any';

/**
 * Derives a template's length class from its blank count (AC-01): 'quick' when
 * it has QUICK_MAX_BLANKS blanks or fewer, 'full' otherwise. Reads ONLY the
 * blank count via getBlanks - no authored length tag exists or is consulted.
 */
export function classifyLength(template: Template): LengthClass {
  return getBlanks(template).length <= QUICK_MAX_BLANKS ? 'quick' : 'full';
}

/**
 * The length selection rule (AC-02): given a list of templates and a length
 * preference, returns the templates that fit.
 *
 * - 'quick' -> only templates that classify as 'quick'.
 * - 'full'  -> only templates that classify as 'full'.
 * - 'any'   -> every template, unfiltered (a shallow copy, so callers can treat
 *              the result as their own array without mutating the source pool).
 *
 * Never mutates `templates`. This stage assumes `templates` is ALREADY the
 * family-safe-gated pool (selectTemplates ran first) - it does not re-check
 * safety, only length.
 */
export function selectByLength(
  templates: readonly Template[],
  lengthPref: LengthPreference,
): Template[] {
  if (lengthPref === 'any') {
    return [...templates];
  }
  return templates.filter((t) => classifyLength(t) === lengthPref);
}

/**
 * The compose helper that adds the empty-pool fallback (AC-06): applies
 * selectByLength to the family-safe pool, and if the length preference selected
 * NOTHING, degrades to the family-safe pool (a shallow copy) rather than
 * returning an empty pool that would fail the round. With 'any', or whenever the
 * length filter matches at least one template, the filtered result is returned
 * unchanged.
 *
 * `familySafePool` MUST already be the output of the family-safe gate - the
 * fallback degrades to a longer story, never to unsafe content. Never mutates
 * the input.
 */
export function selectByLengthOrFallback(
  familySafePool: readonly Template[],
  lengthPref: LengthPreference,
): Template[] {
  const filtered = selectByLength(familySafePool, lengthPref);
  return filtered.length > 0 ? filtered : [...familySafePool];
}
