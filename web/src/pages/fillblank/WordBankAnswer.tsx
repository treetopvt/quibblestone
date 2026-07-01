// ----------------------------------------------------------------------------
//  WordBankAnswer - the Word Bank mode's answer surface (game-modes/04,
//  AC-01, AC-02, AC-03, AC-07).
//
//  What this is: a tappable chip/tile list of curated words drawn from a
//  template's `wordBank` (web/src/engine/template.ts's `WordBankEntry`),
//  filtered down to the entries whose `category` matches the CURRENT blank's
//  `category` (AC-02). It plugs into FillBlank's `answerSurface` slot
//  (game-modes/03's `ModeSurfaces` contract, web/src/pages/modeSurfaces.ts) -
//  it does NOT import or edit FillBlank.tsx itself (AC-01, AC-07). Whichever
//  parent resolves the active mode (a future mode picker, out of this story's
//  scope) is responsible for instantiating this component and passing it into
//  FillBlank's `answerSurface` prop; this file only supplies the surface plus
//  a colocated factory (`wordBankSurfaces`, below) that pairs it with the
//  `ModeSurfaces` shape the way `web/src/engine/modes/wordBank.ts` pairs the
//  axis config.
//
//  Tap-then-submit, not tap-to-submit: tapping a chip SELECTS it (visually
//  highlighted, same teal family as FillBlank's "Need a spark?" row) as the
//  current answer; a separate "Choose this word" submit affordance actually
//  records it. This mirrors FillBlank's own free-text flow (type, then tap
//  "Next word") so the player always gets one deliberate confirm step, not an
//  accidental tap-and-advance.
//
//  Child safety (AC-04, AC-05): tapped words are recorded via the SAME
//  `onSubmitWord` callback every mode calls (there is never a second path into
//  collection - see FillBlank.tsx's header and modeSurfaces.ts's
//  `answerSurface` doc) - `engine.ts`'s `collectWord` already skips the
//  free-text safety check when `mode.answer === 'word-bank'`, because these
//  words are curated/pre-vetted, not player-authored free text. Family-safe
//  gating happens BEFORE this component ever renders - at content-selection
//  time, via `web/src/content/wordBankOffering.ts` - never a per-tap check
//  here. This component carries no PII: a tapped word is anonymous curated
//  content.
//
//  Pure helper (AC-02, unit-testable without rendering): `wordsForCategory`
//  is exported standalone so the category-filter logic can be asserted in a
//  plain Vitest .ts file (see WordBankAnswer.test.ts) with no render harness
//  needed (this repo has none - see the engine/modes header conventions).
//
//  Styling: every color/spacing token comes from web/src/theme.ts (no hex/px
//  literals). Reuses the teal MUI Chip tap language already established by
//  FillBlank's "Need a spark?" spark-word row, so the same visual affordance
//  family is instantly recognizable across modes. Big tap targets (chunky
//  chip padding) per the design brief (README section 10 / CLAUDE.md section
//  A). Icons are FontAwesome only, registered in web/src/fontawesome.ts.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography } from '@mui/material';
import type { Blank, BlankCategory, WordBankEntry } from '../../engine/template';
import type { ModeSurfaces } from '../modeSurfaces';

/**
 * Returns only the word-bank entries whose category matches `category`
 * (AC-02). Pure and standalone so it is unit-testable without rendering
 * anything - see WordBankAnswer.test.ts.
 */
export function wordsForCategory(
  wordBank: readonly WordBankEntry[],
  category: BlankCategory,
): WordBankEntry[] {
  return wordBank.filter((entry) => entry.category === category);
}

export interface WordBankAnswerProps {
  /** The template's full curated word list (web/src/engine/template.ts's `Template.wordBank`). */
  wordBank: readonly WordBankEntry[];
  /** The blank currently being filled; only entries matching its category are shown (AC-02). */
  blank: Blank;
  /**
   * Submits the selected word. The parent wires this to the SAME
   * `onSubmitWord` FillBlank was given (AC-03) - there is never a second path
   * into `collectWord`. Resolves with the outcome; a rejected submission (e.g.
   * an unknown blank id) is shown inline and the player can pick again without
   * advancing.
   */
  onSubmit: (word: string) => Promise<{ accepted: boolean; message?: string }>;
}

/**
 * The tappable word-bank list: select a word, then confirm with "Choose this
 * word". Renders nothing extra when the filtered list is empty (AC-06's
 * spirit carried into the surface itself - a bank-less/mismatched-category
 * template never crashes, it simply has nothing to tap).
 */
export function WordBankAnswer({ wordBank, blank, onSubmit }: WordBankAnswerProps) {
  const theme = useTheme();
  const [selected, setSelected] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const entries = wordsForCategory(wordBank, blank.category);

  const handleTap = (word: string) => {
    setSelected(word);
    setErrorMessage(null);
  };

  const handleSubmit = async () => {
    if (!selected || submitting) return;
    setSubmitting(true);
    setErrorMessage(null);
    try {
      const result = await onSubmit(selected);
      if (!result.accepted) {
        setErrorMessage(result.message ?? 'That word did not work. Try another!');
        return;
      }
      setSelected(null);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Stack sx={{ mb: 4 }}>
      <Typography
        sx={{
          mb: 1.25,
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 700,
          fontSize: 12.5,
          color: 'text.secondary',
        }}
      >
        Tap a word from the bank
      </Typography>

      <Stack direction="row" spacing={1.25} flexWrap="wrap" useFlexGap sx={{ mb: 2.5 }}>
        {entries.map((entry) => {
          const isSelected = entry.word === selected;
          return (
            <Box
              key={entry.word}
              component="button"
              type="button"
              onClick={() => handleTap(entry.word)}
              sx={{
                border: 'none',
                cursor: 'pointer',
                px: 3,
                py: 1.75,
                borderRadius: 999,
                bgcolor: isSelected
                  ? alpha(theme.palette.teal.main, 0.32)
                  : alpha(theme.palette.teal.main, 0.14),
                color: theme.palette.teal.dark,
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 15,
                '&:hover': { bgcolor: alpha(theme.palette.teal.main, 0.22) },
              }}
            >
              {entry.word}
            </Box>
          );
        })}
      </Stack>

      {errorMessage && (
        <Typography
          role="alert"
          sx={{
            mb: 2.5,
            fontFamily: '"Nunito", sans-serif',
            fontWeight: 700,
            fontSize: 13.5,
            color: 'coral.main',
          }}
        >
          {errorMessage}
        </Typography>
      )}

      <Button
        type="button"
        variant="contained"
        fullWidth
        disabled={submitting || !selected}
        onClick={handleSubmit}
      >
        {submitting ? 'Choosing...' : 'Choose this word'}
        <FontAwesomeIcon icon="arrow-right" style={{ width: 18, height: 18 }} />
      </Button>
    </Stack>
  );
}

/**
 * Colocated `ModeSurfaces` factory pairing this component with Word Bank's
 * `ModeConfig` (web/src/engine/modes/wordBank.ts), matching the pattern every
 * mode uses (game-modes/03's contract): pure axis config lives in
 * `engine/modes/`, the paired React surface lives here in the pages layer. A
 * future mode picker (out of this story's scope) supplies the runtime props
 * and reads `.answerSurface` off the result to pass into FillBlank.
 */
export function wordBankSurfaces(args: WordBankAnswerProps): ModeSurfaces {
  return {
    answerSurface: <WordBankAnswer {...args} />,
  };
}
