// ----------------------------------------------------------------------------
//  wordBankOffering.ts - which templates' word banks are offered for Word
//  Bank mode (game-modes/04, AC-05, AC-06).
//
//  This module answers exactly one question: "given the family-safe toggle's
//  current position, which templates' `wordBank`s may Word Bank mode offer as
//  its source list?" It is a content-SELECTION-time gate, not a per-tap check
//  (AC-05) - the same "decide once, up front" shape README section 3 already
//  establishes for the entitlement seam, applied here to content gating
//  instead of billing. Word Bank mode's per-tap surface
//  (web/src/pages/fillblank/WordBankAnswer.tsx) never re-checks family-safe
//  itself; it only ever renders a bank this module has already decided to
//  offer.
//
//  Two rules, both mandatory:
//    1. `template.wordBank` must be present at all (AC-06) - a template with
//       no word bank is NEVER offered, regardless of the family-safe toggle's
//       position. This is what keeps Word Bank mode from crashing or
//       rendering an empty list for a bank-less template: the mode is simply
//       not offered for it in the first place.
//    2. When the family-safe toggle is ON, the template must ALSO be tagged
//       `tags.familySafe === true` (AC-05). This reuses the EXACT rule
//       child-safety/02 already established (`isFamilySafe` /
//       `selectTemplates`, ./familySafe.ts) rather than inventing a second,
//       parallel gating rule - word-bank content is still gated by the
//       family-safe toggle even though individual bank entries skip the
//       free-text profanity filter (README section 6), because the toggle
//       and the profanity filter are two DIFFERENT, complementary guards (see
//       familySafe.ts's own header on this distinction).
//
//  Pure by construction: data in, data out, no React/fetch/SignalR, no
//  mutation of the input array - safe to unit test in isolation
//  (wordBankOffering.test.ts) and safe to import from any layer (a future mode
//  picker deciding whether Word Bank mode is selectable for the session's
//  current template list).
//
//  Who imports this: a future mode picker (single-player/group-play, out of
//  this story's scope) that decides which modes/templates a session may
//  offer; this story's own test file that proves AC-05/AC-06.
// ----------------------------------------------------------------------------

import { isFamilySafe } from './familySafe';
import type { Template } from '../engine/template';

/**
 * Returns the templates whose word banks Word Bank mode may offer, given the
 * family-safe toggle's current position (AC-05, AC-06):
 *   - a template with no `wordBank` at all is NEVER included (AC-06);
 *   - when `familySafeOn` is true, a template must ALSO be tagged
 *     `tags.familySafe === true` (AC-05), reusing `isFamilySafe` rather than
 *     re-deriving the family-safe rule;
 *   - when `familySafeOn` is false, every template WITH a word bank is
 *     included, unfiltered by family-safe tag.
 *
 * Never mutates `templates`; returns a fresh array either way.
 */
export function offerWordBankTemplates(
  templates: readonly Template[],
  familySafeOn: boolean,
): Template[] {
  const withBank = templates.filter((t) => t.wordBank !== undefined && t.wordBank.length > 0);
  if (!familySafeOn) {
    return withBank;
  }
  return withBank.filter(isFamilySafe);
}
