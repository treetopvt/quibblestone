// ----------------------------------------------------------------------------
//  familySafe.ts - the family-safe content GATE (child-safety/02).
//
//  This module is the family-safe TOGGLE'S content-selection rule for the
//  solo loop: given a set of templates (e.g. seedLibrary.ts) and whether the
//  family-safe toggle is on, it returns which templates a single player is
//  allowed to be offered. It reads ONLY the `tags.familySafe` signal already
//  authored on each Template (see ../engine/template.ts) - it does not
//  interpret `ageRating` or `themes`, and it does not add, invent, or infer a
//  safety signal of its own.
//
//  What this is NOT: this is NOT the profanity/safety filter on player
//  free-text submissions (child-safety/01). That filter always runs on
//  submitted words regardless of this toggle's position (AC-04) - relaxing
//  this content gate must never relax that filter. This file has no
//  knowledge of player-submitted text at all; it only ever looks at
//  hand-authored template tags.
//
//  Default posture: "safe by default" (AC-02) is a caller concern, not
//  something this module enforces by itself - there is no hidden global
//  toggle here. Whatever component owns the toggle's React state (see
//  ../components/FamilySafeToggle.tsx) MUST initialize `checked` to `true` so
//  a fresh session starts family-safe-on; this module simply honors whatever
//  boolean it is given.
//
//  Pure by construction: data in, data out. No React, no fetch, no SignalR,
//  no mutation of the input array (see selectTemplates below) - safe to unit
//  test in isolation (see familySafe.test.ts) and safe to import from any
//  layer (single-player's template pick today; group-play's host template
//  list later, per template-model/01's header comment).
// ----------------------------------------------------------------------------

import type { Template } from '../engine/template';

/**
 * The shared "safe by default" initial value (AC-02, single-player/01 AC-04):
 * any screen that owns a family-safe toggle's React state should initialize
 * it to this constant rather than hardcoding `true` locally, so the
 * safe-by-default posture is one token instead of a convention repeated at
 * every call site (a review note on the FamilySafeToggle usage above).
 */
export const FAMILY_SAFE_DEFAULT = true;

/**
 * Returns whether a single template is tagged family-safe. Reads only
 * `template.tags.familySafe` - the one signal this gate is allowed to act on.
 */
export function isFamilySafe(template: Template): boolean {
  return template.tags.familySafe;
}

/**
 * The family-safe selection rule (AC-01 / AC-03): given a list of templates
 * and the current position of the family-safe toggle, returns the templates
 * a player may be offered.
 *
 * - `familySafeOn = true` -> only templates with `tags.familySafe === true`.
 * - `familySafeOn = false` -> every template, unfiltered (a shallow copy, so
 *   callers can safely treat the result as their own array without mutating
 *   the source library).
 *
 * This is the rule single-player applies at the solo template pick; it never
 * mutates `templates` and never touches player-submitted free text.
 */
export function selectTemplates(
  templates: readonly Template[],
  familySafeOn: boolean,
): Template[] {
  if (!familySafeOn) {
    return [...templates];
  }
  return templates.filter(isFamilySafe);
}
