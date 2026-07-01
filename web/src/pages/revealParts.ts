// ----------------------------------------------------------------------------
//  revealParts.ts - pure helper that turns a Template + an AssembledStory into
//  an ordered list of renderable "parts" for the Reveal screen (the-reveal/01,
//  AC-01/AC-02).
//
//  Why this exists as its own pure module: Reveal.tsx needs to interleave the
//  template's literal body text with each player-filled word so the filled
//  words can be highlighted coral IN PLACE (not just appended or listed
//  separately). That interleaving is a pure, testable transform - walk
//  `template.body` in order, and every time a 'blank' segment is hit, pull the
//  next entry from `assembled.filledWords` (assemble() already produced those
//  1:1 in body order, see engine/assemble.ts). Extracting it here keeps the
//  highlight-correctness logic unit-testable under Vitest (pure-logic only,
//  see vitest.config.ts) without standing up a React render harness, which this
//  repo does not have yet.
//
//  This module does NOT re-implement assembly (assemble.ts already produced
//  the attributed words) and does NOT touch the safety filter (words arrive
//  here already vetted, per AC-04) - it only re-shapes already-produced data
//  for rendering.
// ----------------------------------------------------------------------------

import type { AssembledStory } from '../engine/assemble';
import type { Template } from '../engine/template';

/** A literal run of story text, rendered as plain body text. */
export interface RevealTextPart {
  kind: 'text';
  text: string;
}

/** A filled-in blank, rendered with the coral highlight treatment (AC-02). */
export interface RevealWordPart {
  kind: 'word';
  word: string;
  blankId: string;
  playerSessionId: string | undefined;
}

/** One ordered element of the reveal body: literal text or a highlighted word. */
export type RevealPart = RevealTextPart | RevealWordPart;

/**
 * Walks `template.body` in order, interleaving literal text segments with the
 * matching filled word from `assembled.filledWords` (matched positionally,
 * since assemble() produced both lists in the same body-blank order). Pure:
 * never mutates its inputs, never throws on a length mismatch (mirrors
 * assemble()'s own non-throwing contract) - if a blank segment has no
 * corresponding filled word (should not happen given a real AssembledStory,
 * but keeps this helper defensive), it renders as an empty-word part rather
 * than crashing the reveal screen.
 */
export function buildRevealParts(template: Template, assembled: AssembledStory): RevealPart[] {
  let blankIndex = 0;

  return template.body.map((segment): RevealPart => {
    if (segment.type === 'text') {
      return { kind: 'text', text: segment.text };
    }

    const filled = assembled.filledWords[blankIndex];
    blankIndex += 1;

    return {
      kind: 'word',
      word: filled?.word ?? '',
      blankId: segment.blank.id,
      playerSessionId: filled?.playerSessionId,
    };
  });
}
