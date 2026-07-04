// ----------------------------------------------------------------------------
//  remixHelpers.ts - the pure "which blank could I remix?" helper
//  (replay-remix/02, "One-blank remix of a finished tale", issue #61).
//
//  What this is: 100% composition over the EXISTING engine functions - never a
//  new engine function, never a fork of assemble()/collectWord()/assembleStory().
//  Given an already-ASSEMBLED story plus the Template it came from, this
//  returns the ordered list of "remixable blanks" - one entry per blank, with
//  its stable blankId (for re-collecting), its authored categoryLabel (for the
//  picker UI, e.g. "ADJECTIVE"), and the word currently sitting in that blank
//  (e.g. "squishy") - so a caller can render a "pick one to remix" list (AC-02)
//  without inventing a second data model. It joins `assembled.filledWords`
//  (blankId + word, already in body order per assemble.ts) against
//  `getBlanks(template)` (the authored categoryLabel per blank) purely by
//  blankId - no new state, no assumptions about array order beyond what
//  assemble() itself already guarantees.
//
//  The actual REMIX (AC-04: overwrite one blank's word, re-assemble
//  deterministically with every other word unchanged) is NOT done here - that
//  is just calling the engine's existing `collectWord` again for the SAME
//  blankId (engine.ts documents why this is safe: collection is keyed by
//  blank id, so a second `collectWord` call for the same id simply overwrites
//  the prior Map entry) followed by `assembleStory` again. This module only
//  answers "what CAN be remixed", never "how to remix it" - callers (Solo.tsx,
//  App.tsx's GroupReveal) hold the `CollectedWords` map (or the equivalent
//  server-side state) and call collectWord/assembleStory directly.
//
//  Exported for reuse: a later feature (keepsake-gallery) may want to show
//  "what got remixed" from the same blank-picker shape, per this story's tech
//  notes - so this stays a small, standalone, dependency-free module rather
//  than an inline helper in Reveal.tsx or Solo.tsx.
// ----------------------------------------------------------------------------

import type { AssembledStory } from './assemble';
import { getBlanks, type Template } from './template';

/**
 * One remixable blank, as shown on the "Remix a word" picker (AC-02): its
 * stable id (passed back to `collectWord` to re-collect just this blank), the
 * blank's authored category label (e.g. "ADJECTIVE"), and the word currently
 * filling it in the just-revealed story (e.g. "squishy").
 */
export interface RemixableBlank {
  /** The blank's stable id within the template (e.g. "blank-1"). */
  blankId: string;
  /** The blank's authored category label, exactly as FillBlank's prompt card shows it. */
  categoryLabel: string;
  /** The word currently filling this blank in the assembled story (may be empty for a skipped blank). */
  word: string;
}

/**
 * Lists every remixable blank in `assembled`, in body order, joined against
 * `template` for each blank's authored `categoryLabel` (AC-02). A filled
 * word whose blankId has no match in the template (a catalog/library drift)
 * is skipped rather than rendered with missing copy - defensive, since this
 * should never happen for a template/assembled pair that came from the same
 * round.
 */
export function listRemixableBlanks(
  assembled: AssembledStory,
  template: Template,
): RemixableBlank[] {
  const blanksById = new Map(getBlanks(template).map((blank) => [blank.id, blank]));

  const remixable: RemixableBlank[] = [];
  for (const filled of assembled.filledWords) {
    const blank = blanksById.get(filled.blankId);
    if (!blank) continue;
    remixable.push({
      blankId: filled.blankId,
      categoryLabel: blank.categoryLabel,
      word: filled.word,
    });
  }
  return remixable;
}
