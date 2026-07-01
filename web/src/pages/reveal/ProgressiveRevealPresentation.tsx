// ----------------------------------------------------------------------------
//  ProgressiveRevealPresentation - the Progressive Reveal mode's
//  `revealPresentation` surface (game-modes/06, AC-02/AC-03).
//
//  What this plugs into: Reveal.tsx (the-reveal/01, wired by game-modes/03)
//  exposes an OPTIONAL `revealPresentation` prop that REPLACES its default
//  coral-highlight body when supplied. This component is that replacement for
//  Progressive Reveal: it reuses `buildRevealParts(template, assembled)`
//  (../revealParts.ts) READ-ONLY - the exact same ordered text/word parts
//  Reveal's default path renders - against the already-COMPLETE
//  `AssembledStory` (AC-04: pacing is a rendering concern layered on top of a
//  finished assembly, never a collection/assembly concern). It does not
//  import or edit Reveal.tsx; a future mode picker (out of scope) is what
//  passes an instance of this component into Reveal's `revealPresentation`
//  prop.
//
//  Pacing (AC-02/AC-03): every `RevealTextPart` (literal story text) renders
//  IMMEDIATELY and unconditionally, so the sentence shape is visible
//  throughout the whole sequence - only the `RevealWordPart`s (the
//  player-filled, coral-highlighted words) are staged in one at a time. A
//  local `step` counter increases on a fixed interval (no player control, no
//  animation library - a plain `setInterval`, matching the keyframe/interval
//  approach FillBlank/Reveal already use for segmentPulse/tabletGlow/
//  twinkle). Once `step` reaches the total word count, every word is showing
//  and the rendered output is IDENTICAL to Reveal's default final body (same
//  Typography styling, same coral treatment, same empty-word-part handling
//  for a skipped blank) - AC-02/AC-03's "matches the default reveal's final
//  state exactly" requirement.
//
//  Pure pacing math is extracted into `howManyWordsRevealedAtStep` (below) so
//  it is unit-testable under Vitest without a render harness (this repo has
//  none yet, see ../../engine/modes/progressiveReveal.test.ts).
//
//  Child safety (AC-05): every word here already passed the safety filter on
//  `collectWord`'s collection path (during FILLING, before the reveal ever
//  runs) - this component only paces already-vetted words already carried in
//  `assembled.filledWords`. It introduces no second, unfiltered path and
//  collects no PII.
//
//  Styling: every color/spacing/font token here mirrors Reveal.tsx's own
//  default body block exactly (theme.palette.coral.main for the highlight,
//  the same literal Nunito/Fredoka font-family strings Reveal.tsx already
//  uses for this exact Typography, since it must slot into the SAME
//  stone-tablet scroll panel seamlessly). Icons: none needed here. No
//  animation library - a `setInterval` timer drives `step`, cleared on
//  unmount.
// ----------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Typography } from '@mui/material';
import type { AssembledStory } from '../../engine/assemble';
import type { Template } from '../../engine/template';
import type { ModeSurfaces } from '../modeSurfaces';
import { buildRevealParts, type RevealPart } from '../revealParts';

/** Milliseconds between each newly-revealed word (a fixed, non-interactive pace - see story's Out of Scope). */
const STEP_INTERVAL_MS = 700;

/**
 * Pure pacing helper: given the ordered `RevealPart`s and how many pacing
 * steps have elapsed, returns how many `RevealWordPart`s should be visible
 * (in body order). Step 0 reveals no words; each subsequent step reveals one
 * more, until every word part has been revealed - never more than the total
 * word-part count, so a caller can keep incrementing `step` past completion
 * without needing to clamp it themselves. Exported so it is directly
 * unit-testable without rendering (progressiveReveal.test.ts).
 */
export function howManyWordsRevealedAtStep(parts: readonly RevealPart[], step: number): number {
  const totalWords = parts.filter((part) => part.kind === 'word').length;
  if (step < 0) return 0;
  return Math.min(step, totalWords);
}

export interface ProgressiveRevealPresentationProps {
  /** The template whose body is walked (via buildRevealParts) to interleave literal text with words. */
  template: Template;
  /** The already-complete assembled story to pace out - never a partial/in-progress assembly. */
  assembled: AssembledStory;
}

/**
 * Paces the assembled story's filled words in one at a time, matching
 * Reveal's default coral-highlight body once every word has landed.
 */
export function ProgressiveRevealPresentation({ template, assembled }: ProgressiveRevealPresentationProps) {
  const theme = useTheme();
  const parts = buildRevealParts(template, assembled);
  const totalWords = parts.filter((part) => part.kind === 'word').length;

  const [step, setStep] = useState(0);

  useEffect(() => {
    // Reset pacing whenever the underlying story changes (e.g. a new round
    // reuses this same component instance).
    setStep(0);

    if (totalWords === 0) return;

    const timer = setInterval(() => {
      setStep((current) => {
        const next = current + 1;
        if (next >= totalWords) {
          clearInterval(timer);
        }
        return next;
      });
    }, STEP_INTERVAL_MS);

    return () => clearInterval(timer);
    // Deps are complete: the effect body reads only `totalWords` (plus the
    // stable `setStep`); `template`/`assembled` are included so pacing resets
    // when a new round reuses this instance, even if the word count is unchanged.
  }, [template, assembled, totalWords]);

  const revealedWordCount = howManyWordsRevealedAtStep(parts, step);
  let wordsSoFar = 0;

  return (
    <Typography
      component="p"
      sx={{
        fontFamily: '"Nunito", sans-serif',
        fontWeight: 600,
        fontSize: 17.5,
        lineHeight: 1.72,
        color: 'text.primary',
      }}
    >
      {parts.map((part, index) => {
        if (part.kind === 'text') {
          // Literal text renders immediately and unconditionally, so the
          // sentence shape is visible throughout the whole sequence (AC-03).
          return (
            <Box key={`p-${index}`} component="span">
              {part.text}
            </Box>
          );
        }

        const wordPosition = wordsSoFar;
        wordsSoFar += 1;

        // Not-yet-revealed word: rendered as nothing until its step arrives
        // (AC-03) - no placeholder, no layout-shifting blank box.
        if (wordPosition >= revealedWordCount) {
          return <Box key={`p-${index}`} component="span" />;
        }

        // A skipped blank arrives as an empty-word part, same as Reveal's
        // default body - render it as plain nothing rather than a stray
        // zero-width coral underline artifact.
        if (part.word === '') {
          return <Box key={`p-${index}`} component="span" />;
        }

        return (
          <Box
            key={`p-${index}`}
            component="span"
            sx={{
              // Same coral treatment as Reveal's default body (AC-02): color
              // from the theme token, weight/underline as content-level sx.
              color: theme.palette.coral.main,
              fontWeight: 800,
              borderBottom: `2px solid ${alpha(theme.palette.coral.main, 0.4)}`,
            }}
          >
            {part.word}
          </Box>
        );
      })}
    </Typography>
  );
}

/**
 * This mode's ModeSurfaces pairing (game-modes/03's contract): supplies only
 * `revealPresentation`, leaving `seeContext`/`answerSurface` undefined so
 * FillBlank keeps rendering its Classic-blind-identical subject-only default
 * (AC-01). Colocated here (not in engine/modes/progressiveReveal.ts) because a
 * ModeSurfaces value carries a React node - see modeSurfaces.ts's header for
 * why surfaces live in pages/, not engine/.
 */
export function progressiveRevealSurfaces(args: ProgressiveRevealPresentationProps): ModeSurfaces {
  return {
    revealPresentation: <ProgressiveRevealPresentation {...args} />,
  };
}
