// ----------------------------------------------------------------------------
//  assemble.ts - the pure function that turns a Template + an ordered set of
//  player words into the final, deterministic story text (AC-06).
//
//  Why pure: assemble() takes a Template and a list of submitted words and
//  returns a new AssembledStory, touching no UI, no SignalR, no Date.now(),
//  no randomness. Same inputs -> same output, every time. That determinism is
//  exactly what the reveal screen and the Round Complete per-player word
//  counts depend on (docs/design/README.md, screens 6-7): the reveal colors
//  every filled-in word, and the recap sums each player's contributed-word
//  count from the SAME assembled result. Purity also makes this the prime
//  unit-test target in a codebase with no test harness yet (see
//  assemble.test.ts, and CLAUDE.md section 9 - platform-devops/01 will own
//  the canonical harness later; this story seeds a minimal one).
//
//  Word-count-mismatch rule (documented here since it is a design decision,
//  not just an implementation detail - see assemble.test.ts for coverage):
//    - If FEWER words are supplied than blanks, the unfilled trailing blanks
//      are left as an empty-string fill (attributed to no player). This lets
//      a caller render a partial assembly (e.g. mid-collection) without
//      throwing.
//    - If MORE words are supplied than blanks, the extra words are ignored
//      (only the first N - where N = blank count - are used). Extra words
//      never affect the produced text.
//    - Both directions are non-throwing: assemble() never throws on a length
//      mismatch, so a caller never needs a try/catch on the happy path of
//      "the host moved on before everyone finished."
//
//  Who imports this: the-reveal (renders `storyText` / `filledWords`),
//  single-player + group-play (compute per-player word counts from
//  `filledWords`), game-modes (no special handling needed - assemble() is
//  mode-agnostic, per AC-05).
// ----------------------------------------------------------------------------

import { getBlanks, type Template } from './template';

/**
 * One word submitted by a player, in the order it should fill the template's
 * blanks. `playerSessionId` is the anonymous session id (join code + nickname
 * world - no PII, per README section 6) used to attribute the word back to
 * its author for the reveal / recap.
 */
export interface SubmittedWord {
  playerSessionId: string;
  word: string;
}

/**
 * A single filled blank in the assembled result: the blank it filled, the
 * word used, and which player submitted it (or `undefined` if no word was
 * available for that blank - see the mismatch rule above).
 */
export interface FilledBlank {
  blankId: string;
  word: string;
  playerSessionId: string | undefined;
}

/** The deterministic output of assemble(): final story text plus per-word attribution. */
export interface AssembledStory {
  templateId: string;
  title: string;
  /** The final story text with every blank replaced in order. */
  storyText: string;
  /** Per-blank attribution, in body order, for reveal highlighting and per-player counts. */
  filledWords: readonly FilledBlank[];
}

/**
 * Deterministically assembles a Template and an ordered list of submitted
 * words into final story text, preserving per-word attribution (AC-06).
 *
 * Blanks are filled in the order they appear in `template.body` (AC-01),
 * matched 1:1 against `words` in the order given. See the file header for the
 * word-count-mismatch rule. This function is pure: it never mutates its
 * inputs and never throws.
 */
export function assemble(template: Template, words: readonly SubmittedWord[]): AssembledStory {
  const blanks = getBlanks(template);
  const filledWords: FilledBlank[] = blanks.map((b, index) => {
    const submitted = index < words.length ? words[index] : undefined;
    return {
      blankId: b.id,
      word: submitted?.word ?? '',
      playerSessionId: submitted?.playerSessionId,
    };
  });

  let blankIndex = 0;
  const storyText = template.body
    .map((segment) => {
      if (segment.type === 'text') {
        return segment.text;
      }
      const filled = filledWords[blankIndex];
      blankIndex += 1;
      return filled.word;
    })
    .join('');

  return {
    templateId: template.id,
    title: template.title,
    storyText,
    filledWords,
  };
}
