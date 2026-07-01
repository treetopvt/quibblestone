// ----------------------------------------------------------------------------
//  StorySoFarContext.tsx - the Progressive Story mode's `seeContext` surface
//  (game-modes/05, AC-01/AC-02/AC-03).
//
//  What this renders: the story-so-far - literal template text plus every
//  already-filled word, coral-highlighted exactly like the-reveal - up to
//  (but NOT including) the current blank. Nothing after the current blank is
//  ever rendered (AC-01), so a player never gets a sneak peek at unfilled
//  text or future blanks. It is passed into FillBlank's `seeContext` prop
//  (game-modes/03) by whichever parent wires the Progressive Story mode (a
//  future mode picker, out of this story's scope) - this component never
//  imports or edits FillBlank.tsx itself.
//
//  Reuse contract (AC-03): this component does NOT reinvent the
//  interleave-and-highlight logic. It calls `assembleStory` (engine.ts) -
//  unmodified - against the PARTIAL `CollectedWords` map for whatever has
//  been filled so far, then hands that partial `AssembledStory` to
//  `buildRevealParts` (the-reveal/01's pure helper, ../revealParts.ts) - also
//  unmodified - to get the SAME ordered text/word parts Reveal.tsx renders.
//  `assemble()` is documented as non-throwing on a fewer-words-than-blanks
//  mismatch (engine/assemble.ts header) specifically so this partial-collection
//  case works with zero special-casing. The only NEW logic this file adds is
//  `sliceStorySoFarParts`: a pure, exported helper that trims that full parts
//  list down to "everything before the current blank," so it is unit-testable
//  without a render harness (this repo has none yet - see mode.ts's own "pure
//  config, no React" contract for why the engine layer stays render-free, and
//  CLAUDE.md section 9 for the render-harness gap).
//
//  Child safety (AC-04): the story-so-far view only ever renders words already
//  present in `collectedSoFar` - and `collectWord` (engine.ts) only adds a word
//  to that map AFTER the safety check passes. So no player ever sees an
//  unfiltered word here, even transiently; there is nothing new to wire.
//
//  Why this lives in web/src/pages/fillblank/ and not web/src/engine/modes/:
//  it renders a React node (the ModeSurfaces `seeContext` slot is typed as
//  ReactNode - see ../modeSurfaces.ts's own header for why surfaces are a
//  pages-layer concern, not an engine one). The paired PURE ModeConfig lives
//  separately, in web/src/engine/modes/progressiveStory.ts, which imports NO
//  React - the two are combined by `progressiveStorySurfaces` below, a plain
//  factory a future mode picker calls with this mode's runtime props.
//
//  Styling: reuses the exact same theme tokens as Reveal.tsx's coral-highlight
//  treatment (theme.palette.coral.main + the same weight/underline emphasis) -
//  no hex/px literals, no new tokens needed. No icons in this surface.
// ----------------------------------------------------------------------------

import { Box, Typography } from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { assembleStory, type CollectedWords } from '../../engine/engine';
import type { Template } from '../../engine/template';
import { buildRevealParts, type RevealPart } from '../revealParts';
import type { ModeSurfaces } from '../modeSurfaces';

export interface StorySoFarContextProps {
  /** The template being played - walked (via buildRevealParts) to interleave literal text with filled words. */
  template: Template;
  /** Words collected so far, keyed by blank id - may be a PARTIAL collection (AC-03); safe to pass mid-round. */
  collectedSoFar: CollectedWords;
  /** The blank id currently being prompted for. Nothing at or after this blank is rendered (AC-01). */
  currentBlankId: string;
}

/**
 * Trims a full `buildRevealParts` output down to only the parts that precede
 * the current blank (AC-01) - literal text plus every already-filled word,
 * stopping the instant the current blank's own word-part is reached. Pure and
 * exported so it is unit-testable without a render harness: given the same
 * template + partial assembly + current blank id, it always returns the same
 * slice, with no side effects.
 *
 * Walks the parts in order (the same body order `buildRevealParts` already
 * produced) and stops BEFORE including the word-part whose `blankId` matches
 * `currentBlankId` - "up to but not including the current blank," per AC-01.
 * If `currentBlankId` is not found (e.g. the round is already complete, or a
 * bad id), the full parts list is returned unchanged - there is no "current"
 * blank left to stop before.
 */
export function sliceStorySoFarParts(parts: readonly RevealPart[], currentBlankId: string): RevealPart[] {
  const stopIndex = parts.findIndex((part) => part.kind === 'word' && part.blankId === currentBlankId);
  if (stopIndex === -1) {
    return [...parts];
  }
  return parts.slice(0, stopIndex);
}

/**
 * Renders one already-filled word with the SAME coral highlight treatment
 * Reveal.tsx uses (color + weight + underline), so the story-so-far view is
 * visually indistinguishable from how the same word will look at the final
 * reveal.
 */
function StorySoFarWord({ word }: { word: string }) {
  const theme = useTheme();

  if (word === '') {
    // A skipped blank collected as an empty placeholder (engine.ts's
    // skipBlank) - render as plain nothing, matching Reveal's own treatment
    // of an empty-word part (no stray coral underline artifact).
    return <Box component="span" />;
  }

  return (
    <Box
      component="span"
      sx={{
        color: theme.palette.coral.main,
        fontWeight: 800,
        borderBottom: `2px solid ${alpha(theme.palette.coral.main, 0.4)}`,
      }}
    >
      {word}
    </Box>
  );
}

/**
 * The Progressive Story mode's `seeContext` surface (game-modes/05): the
 * story-so-far, up to but not including the current blank, plugged into
 * FillBlank above the prompt card. Standalone - does not import or edit
 * FillBlank.tsx itself; a future mode picker passes an instance of this into
 * FillBlank's `seeContext` prop.
 */
export function StorySoFarContext({ template, collectedSoFar, currentBlankId }: StorySoFarContextProps) {
  const assembledSoFar = assembleStory(template, collectedSoFar);
  const allParts = buildRevealParts(template, assembledSoFar);
  const visibleParts = sliceStorySoFarParts(allParts, currentBlankId);

  // Nothing filled yet and no literal text before the very first blank: stay
  // fully empty rather than rendering an awkward blank panel (AC-01's "up to
  // but not including the current blank" degenerates to "nothing" for the
  // first blank of a template that opens with a blank).
  if (visibleParts.length === 0) {
    return null;
  }

  return (
    <Box sx={{ mb: 3 }}>
      <Typography
        component="p"
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 600,
          fontSize: 15.5,
          lineHeight: 1.6,
          color: 'text.primary',
        }}
      >
        {visibleParts.map((part, index) =>
          part.kind === 'text' ? (
            <Box key={`s-${index}`} component="span">
              {part.text}
            </Box>
          ) : (
            <StorySoFarWord key={`s-${index}`} word={part.word} />
          ),
        )}
      </Typography>
    </Box>
  );
}

/**
 * Colocated `ModeSurfaces` factory pairing Progressive Story's `seeContext`
 * surface with the runtime props it needs for one round. A future mode
 * picker (out of this story's scope) calls this once it has resolved the
 * active template/collection/current blank, and passes the result's
 * `seeContext` straight into FillBlank.
 */
export function progressiveStorySurfaces(args: StorySoFarContextProps): ModeSurfaces {
  return {
    seeContext: <StorySoFarContext {...args} />,
  };
}
