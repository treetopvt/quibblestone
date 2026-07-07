// ----------------------------------------------------------------------------
//  FillBlank - the blank-filling screen shared by SOLO and (later) GROUP play
//  (game-modes/02, docs/design/README.md screen 4).
//
//  REUSE CONTRACT (read this before changing the props): this component is
//  TRANSPORT-AGNOSTIC and purely controlled/presentational. It renders one
//  Blank, collects a word, and hands the word to whatever `onSubmitWord`
//  callback its parent injects - it never imports the safety client
//  (web/src/safety/checkWord.ts), fetch, or the SignalR hook directly. That is
//  what lets:
//    - single-player wire `onSubmitWord` to a REST-backed check
//      (checkWord.ts) plus engine.ts's `collectWord`, and
//    - group-play (later) wire `onSubmitWord` to a hub invoke instead,
//  without either caller forking this screen or this screen knowing which
//  transport is in play. `onSkip` and advancing to the next blank/reveal are
//  likewise fully owned by the parent (AC-07) - FillBlank never decides what
//  comes next and never renders any surrounding story text (blind by
//  construction, AC-02/AC-07).
//
//  What it renders (AC-01 to AC-06):
//    - an optional tale-title pill (the tale's title only, one line, ellipsis)
//      when the parent passes `subject` - Classic blind is
//      `see: 'subject-only'`, so the player sees WHICH tale they are carving
//      but never the story narrative or the filled words (the reveal stays a
//      surprise);
//    - a progress row ("Word N of M" + a chisel icon on the left) and, on the
//      right, a small purple-tint "Blind" chip (eye-slash icon) instead of a
//      redundant "X to go" count - the segment bar already conveys remaining
//      count, and the chip carries the "no peeking" cue that used to live in a
//      full-width reassurance banner. An adaptive-length segment bar (one
//      segment per blank, not a hardcoded 8 - AC-01) follows, with
//      completed/current segments gold and the current segment gently
//      pulsing;
//    - a stone-tablet prompt card (arched, carved rim, glow) with a purple
//      category chip, the blank's prompt sentence, and its sub-hint (AC-02);
//    - a carved input slot (react-hook-form controlled) plus 3 tappable
//      "spark word" chips that fill the input (AC-03);
//    - a gold "Next word" CTA that awaits `onSubmitWord` before advancing,
//      showing the rejection message inline and letting the player retry
//      (AC-05), plus a low-pressure "Skip this word" link (AC-06).
//
//  Fit-to-viewport (UX de-clutter, 2026-07): the whole screen is a fixed-height
//  flex column (`height: 100dvh`, `overflow: hidden`) so a round never page
//  scrolls on a single phone viewport (~390x844) - the old full-width "Blind
//  mode - no peeking..." banner and the redundant "X to go" counter are gone,
//  and that reclaimed vertical space is what lets a 10-blank round fit without
//  scrolling. The AppBar (when rendered) sits at the top of the column; the
//  subject pill + progress row + segment bar + prompt card + answer area live
//  in a `flex: 1; min-height: 0` middle region that scrolls internally ONLY if
//  the content is taller than the viewport allows (the page itself never
//  scrolls); the `<BottomActionBar>` stays pinned to the bottom of the column.
//
//  Child safety: the real safety check happens in the PARENT's
//  `onSubmitWord` (this is why it is async and awaited) - a word is never
//  treated as accepted, cleared, or advanced past until that promise
//  resolves with `accepted: true`. This screen never records or displays an
//  unchecked word.
//
//  Mode-aware slots (game-modes/03): two OPTIONAL props let a mode defer this
//  screen's shape away from hardcoded Classic-blind rendering, without a
//  second path into collection:
//    - `seeContext` renders ABOVE the prompt card (below the tale-title pill,
//      above the progress row) when supplied - e.g. a "story so far" view.
//      Purely additive: never replaces `subject`. It also SUPPRESSES the
//      "Blind" chip on the progress row - a visible story-so-far contradicts
//      claiming the round is blind, exactly like the old reassurance banner's
//      suppression rule (AC-04's intent, carried forward onto the chip).
//    - `answerSurface` REPLACES the carved free-text input + "Need a spark?"
//      chip row when supplied - e.g. a tappable word-bank list. Because the
//      gold "Next word" CTA's enabled state keys off the free-text form
//      value (which stays permanently empty when a non-free-text surface is
//      in play), FillBlank also hides its OWN CTA whenever `answerSurface` is
//      supplied - the surface owns its own submit affordance and MUST still
//      call this component's `onSubmitWord` prop. "Skip this word" keeps
//      rendering either way. This preserves the singular constraint: exactly
//      ONE path into `onSubmitWord`, never two (AC-06). Both slots are typed
//      via the shared `ModeSurfaces` contract (./modeSurfaces.ts), which a
//      future mode picker (out of this story's scope) resolves per active
//      mode; Solo.tsx and GroupRound.tsx pass neither today; and get Classic
//      blind for free.
//
//  Styling: every color/radius/spacing token comes from web/src/theme.ts (no
//  hex/px literals except where the story calls for a literal pixel value -
//  the 66px input slot height and its 18px radius - per the sx borderRadius
//  gotcha: a bare number multiplies by theme.shape.borderRadius, so a literal
//  string is used instead). Icons are FontAwesome only, registered in
//  web/src/fontawesome.ts. No em dashes in any prose/comments/strings.
// ----------------------------------------------------------------------------

import type { ReactNode } from 'react';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, keyframes, useTheme } from '@mui/material/styles';
import { Box, Button, Link, Stack, TextField, Typography } from '@mui/material';
import { AppBar } from '../components';
import type { Blank } from '../engine/template';

export interface FillBlankProps {
  /**
   * The tale's title/subject, shown as a small one-line pill at the top
   * (Classic blind is `see: 'subject-only'` - the player sees WHICH tale they
   * are carving, never the surrounding story narrative or the filled words).
   * The parent passes this only when the active mode's `see` axis is
   * 'subject-only'; omit it for a fully-blind ('nothing') mode. Never the story
   * body - just the subject.
   */
  subject?: string;
  /** The current blank to fill. FillBlank renders only this - no surrounding story text (blind mode). */
  blank: Blank;
  /** 1-based position of this blank among the round's total blanks, for the progress row. */
  wordNumber: number;
  /** Total number of blanks in this round - drives the adaptive segment-bar length. */
  totalWords: number;
  /**
   * Submits the typed/chosen word. The PARENT runs the real safety check
   * (and records the word) and resolves with the outcome; FillBlank awaits
   * this before clearing the input or advancing. On `accepted: false`, the
   * `message` is shown inline and the player can retry without advancing.
   */
  onSubmitWord: (word: string) => Promise<{ accepted: boolean; message?: string }>;
  /** Skips this blank, leaving it empty. The parent decides what comes next (AC-07). */
  onSkip: () => void;
  /**
   * Optional leave/exit action. When provided, an app bar with a leave (x) icon is
   * rendered so a player can bail out mid-fill (group play -> leave the room; solo ->
   * back home) - every other in-game screen has this exit, and without it the
   * word-entry screen is a trap. Omit to render no app bar (unchanged layout).
   */
  onExit?: () => void;
  /** Accessible label for the leave action (default "Leave game"). */
  exitLabel?: string;
  /**
   * Optional mode-supplied context rendered ABOVE the prompt card (below the
   * tale-title pill, above the progress row) - e.g. a "story so far" view for
   * a progressive-story mode (game-modes/05, `ModeSurfaces.seeContext`).
   * Purely additive: never replaces `subject`. Omitted entirely by default,
   * which keeps today's Classic-blind layout pixel-identical (AC-01). When
   * supplied, the progress row's "Blind" chip is suppressed - a visible
   * story-so-far contradicts claiming the round is blind (see the render site
   * below).
   */
  seeContext?: ReactNode;
  /**
   * Optional mode-supplied surface that REPLACES the carved free-text input +
   * "Need a spark?" chip row when supplied - e.g. a tappable word-bank list
   * (game-modes/04, `ModeSurfaces.answerSurface`). The surface owns its own
   * submit affordance and MUST call this component's `onSubmitWord` itself
   * (there is never a second path into collection, AC-06); FillBlank hides its
   * OWN gold "Next word" CTA in this case, since that CTA's enabled state
   * keys off the free-text form value, which stays empty when a non-free-text
   * surface is in play. "Skip this word" still renders either way. Omitted by
   * default, which keeps the free-text input + spark chips + gold CTA
   * rendering exactly as today (AC-02).
   */
  answerSurface?: ReactNode;
}

/** Form values for the single controlled word input (react-hook-form). */
interface FillBlankFormValues {
  word: string;
}

/** Max length enforced on the free-text word input (AC-03). */
const WORD_MAX_LENGTH = 20;

/** Gentle glow pulse on the current segment of the progress bar (AC-01), ~1.8s. */
const segmentPulse = keyframes`
  0%, 100% { box-shadow: 0 0 0 0 var(--qs-segment-glow); }
  50% { box-shadow: 0 0 10px 2px var(--qs-segment-glow); }
`;

/**
 * The small purple-tint "Blind" chip (eye-slash + label) that sits on the
 * right of the progress row. Replaces the old full-width "Blind mode - no
 * peeking at the story..." reassurance banner: same "no peeking" cue, a
 * fraction of the vertical space. Rendered by <ProgressRow> only when the
 * caller has no `seeContext` (see the FillBlank header's mode-aware-slots
 * note) - a mode that shows a story-so-far view must not claim "blind".
 */
function BlindChip() {
  const theme = useTheme();

  return (
    <Stack
      direction="row"
      alignItems="center"
      spacing={0.75}
      sx={{
        px: 1.5,
        py: 0.5,
        borderRadius: 999,
        bgcolor: alpha(theme.palette.primary.main, 0.1),
        flexShrink: 0,
      }}
    >
      <Box sx={{ color: 'primary.main', fontSize: 12, display: 'flex' }}>
        <FontAwesomeIcon icon="eye-slash" />
      </Box>
      <Typography
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 800,
          fontSize: 12,
          color: 'primary.main',
        }}
      >
        Blind
      </Typography>
    </Stack>
  );
}

/**
 * The adaptive progress row: "Word N of M" + chisel icon on the left; the
 * "Blind" chip on the right (AC-04's intent) when the round has no
 * `seeContext` - the old "X to go" counter is gone, since the segment bar
 * below already conveys remaining count.
 */
function ProgressRow({
  wordNumber,
  totalWords,
  showBlindChip,
}: {
  wordNumber: number;
  totalWords: number;
  showBlindChip: boolean;
}) {
  return (
    <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 2 }}>
      <Stack direction="row" alignItems="center" spacing={1.25}>
        <Box sx={{ color: 'primary.main', fontSize: 16, display: 'flex' }}>
          <FontAwesomeIcon icon="hammer" />
        </Box>
        <Typography
          sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 15 }}
        >
          Word {wordNumber} of {totalWords}
        </Typography>
      </Stack>
      {showBlindChip && <BlindChip />}
    </Stack>
  );
}

/** One segment-bar segment. Completed/current segments are gold; the current one pulses. */
function ProgressSegment({ state }: { state: 'done' | 'current' | 'upcoming' }) {
  const theme = useTheme();
  const isFilled = state === 'done' || state === 'current';

  return (
    <Box
      sx={{
        flex: 1,
        height: 8,
        borderRadius: 999,
        bgcolor: isFilled ? 'gold.main' : theme.palette.stoneSlot.alt,
        ...(state === 'current' && {
          '--qs-segment-glow': alpha(theme.palette.gold.main, 0.8),
          animation: `${segmentPulse} 1.8s ease-in-out infinite`,
        }),
      }}
    />
  );
}

/** Adaptive-length segment bar: one segment per blank, not a hardcoded count (AC-01). */
function SegmentBar({ wordNumber, totalWords }: { wordNumber: number; totalWords: number }) {
  const segments = Array.from({ length: totalWords }, (_, index) => {
    const position = index + 1;
    if (position < wordNumber) return 'done' as const;
    if (position === wordNumber) return 'current' as const;
    return 'upcoming' as const;
  });

  return (
    <Stack direction="row" spacing={1} sx={{ mb: 2.5 }}>
      {segments.map((state, index) => (
        <ProgressSegment key={index} state={state} />
      ))}
    </Stack>
  );
}

/** The stone-tablet prompt card: category chip, prompt sentence, sub-hint (AC-02). */
function PromptCard({ blank }: { blank: Blank }) {
  const theme = useTheme();

  return (
    <Box
      sx={{
        position: 'relative',
        px: 5,
        pt: 5,
        pb: 4,
        // No bottom margin: the prompt card is the LAST element in the flexible
        // top zone, so trimming this (and the paddings/margins around it) hands
        // more height back to that zone and keeps the occasional overflow
        // scrollbar off shorter phones (fit-to-viewport tightening).
        mb: 0,
        borderRadius: '30px',
        textAlign: 'center',
        background: theme.palette.tablet.gradient,
        boxShadow: `0 22px 44px -22px ${alpha(theme.palette.primary.main, 0.5)}, inset 0 3px 0 ${alpha(
          theme.palette.common.white,
          0.5,
        )}, inset 0 -4px 12px ${alpha(theme.palette.stoneEdge.main, 0.35)}`,
      }}
    >
      {/* Glowing carved rim (matches Home's hero tablet treatment). */}
      <Box
        aria-hidden
        sx={{
          position: 'absolute',
          inset: '8px',
          borderRadius: '24px',
          border: `2.5px solid ${alpha(theme.palette.stoneEdge.main, 0.5)}`,
          boxShadow: `inset 0 0 16px ${alpha(theme.palette.gold.main, 0.28)}`,
          pointerEvents: 'none',
        }}
      />

      {/* Centered category chip: purple pill, sparkle icon, uppercase category label. */}
      <Box
        sx={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 1,
          px: 3,
          py: 1,
          mb: 2,
          borderRadius: 999,
          bgcolor: alpha(theme.palette.primary.main, 0.14),
        }}
      >
        <Box sx={{ color: 'primary.main', fontSize: 13, display: 'flex' }}>
          <FontAwesomeIcon icon="wand-magic-sparkles" />
        </Box>
        <Typography
          variant="overline"
          sx={{ fontSize: 12, fontWeight: 800, lineHeight: 1, color: 'primary.main' }}
        >
          {blank.categoryLabel}
        </Typography>
      </Box>

      <Typography
        sx={{
          fontFamily: '"Fredoka", sans-serif',
          fontWeight: 600,
          fontSize: 29,
          lineHeight: 1.2,
          color: 'primary.main',
        }}
      >
        {blank.prompt}
      </Typography>

      <Typography
        sx={{
          mt: 1.5,
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 700,
          fontSize: 14.5,
          color: 'text.secondary',
        }}
      >
        {blank.subHint}
      </Typography>
    </Box>
  );
}

/**
 * The tale-title pill (Classic blind is `see: 'subject-only'`): a one-line
 * purple-tint pill with a book icon and the tale's title, truncated with an
 * ellipsis so a long title never wraps or pushes the fold. This is the ONLY
 * story-level context shown - never the narrative or the filled words, so the
 * reveal stays a surprise (AC-02). Replaces the old two-line "Your tale"
 * overline + title stack to reclaim vertical space (fit-to-viewport). Rendered
 * only when a `subject` is passed.
 */
function TaleTitlePill({ subject }: { subject: string }) {
  const theme = useTheme();

  return (
    <Stack direction="row" justifyContent="center" sx={{ mb: 2, minWidth: 0 }}>
      <Stack
        direction="row"
        alignItems="center"
        spacing={1}
        sx={{
          px: 2.25,
          py: 1,
          maxWidth: '100%',
          borderRadius: '16px',
          bgcolor: alpha(theme.palette.primary.main, 0.08),
        }}
      >
        <Box sx={{ color: 'primary.main', fontSize: 14, display: 'flex', flexShrink: 0 }}>
          <FontAwesomeIcon icon="book-open" />
        </Box>
        <Typography
          noWrap
          sx={{
            fontFamily: '"Fredoka", sans-serif',
            fontWeight: 600,
            fontSize: 15,
            lineHeight: 1.2,
            color: 'primary.main',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
          }}
        >
          {subject}
        </Typography>
      </Stack>
    </Stack>
  );
}

export function FillBlank({
  subject,
  blank,
  wordNumber,
  totalWords,
  onSubmitWord,
  onSkip,
  onExit,
  exitLabel,
  seeContext,
  answerSurface,
}: FillBlankProps) {
  const theme = useTheme();
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { control, handleSubmit, setValue, watch } = useForm<FillBlankFormValues>({
    defaultValues: { word: '' },
  });

  const currentWord = watch('word');

  const submit = async (values: FillBlankFormValues) => {
    const word = values.word.trim();
    if (!word || submitting) return;

    setSubmitting(true);
    setErrorMessage(null);
    try {
      const result = await onSubmitWord(word);
      if (!result.accepted) {
        setErrorMessage(result.message ?? 'That word is not allowed here. Try another!');
        return;
      }
      setValue('word', '');
    } finally {
      setSubmitting(false);
    }
  };

  const handleSparkTap = (sparkWord: string) => {
    setValue('word', sparkWord);
    setErrorMessage(null);
  };

  return (
    <Box
      sx={{
        position: 'relative',
        height: '100dvh',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        // Tablet / desktop: a wider column than the 430 phone width (viewport pass).
        maxWidth: { xs: 430, sm: 560 },
        mx: 'auto',
      }}
    >
      {/* Optional leave affordance (matches every other in-game screen). Rendered
          only when the parent wires onExit, so callers that omit it keep the old
          no-app-bar layout. The app bar is a flex-column child (not fixed), so it
          simply takes its own height at the top of the column. */}
      {onExit && (
        <AppBar
          title="Carving words"
          leftAction={{ icon: 'xmark', label: exitLabel ?? 'Leave game', onClick: onExit }}
        />
      )}

      {/* Prompt zone: flexes to fill the space between the app bar and the pinned
          interaction zone below, so the prompt card's height variation between
          blanks (a short prompt vs a long one) is absorbed HERE and never pushes
          the word bank / input or the primary action around. Scrolls internally
          only if a prompt is unusually tall; the page itself never scrolls. */}
      <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 5.5, pt: onExit ? 2 : 4 }}>
        {subject && <TaleTitlePill subject={subject} />}
        {seeContext}
        <ProgressRow
          wordNumber={wordNumber}
          totalWords={totalWords}
          showBlindChip={seeContext === undefined}
        />
        <SegmentBar wordNumber={wordNumber} totalWords={totalWords} />
        <PromptCard blank={blank} />
      </Box>

      {/* Interaction zone: PINNED as the bottom cluster. It is the LAST flex child
          of the fixed-height column, so it anchors to the viewport bottom and the
          word bank / free-text input and the primary action hold a CONSTANT
          position no matter how tall the prompt above is (the drift fix - the
          prompt zone above absorbs all the height variation). The free-text input
          keeps its own <form> so Enter still submits; the word-bank surface owns
          its own "Choose this word" submit. */}
      <Box
        sx={{
          flexShrink: 0,
          display: 'flex',
          flexDirection: 'column',
          gap: 1.5,
          px: 5.5,
          pt: 1.5,
          pb: 'calc(12px + env(safe-area-inset-bottom, 0px))',
          borderTop: `1px solid ${alpha(theme.palette.stoneEdge.main, 0.16)}`,
        }}
      >
        {answerSurface === undefined ? (
          <Box
            component="form"
            onSubmit={handleSubmit(submit)}
            sx={{ display: 'flex', flexDirection: 'column' }}
          >
              {/* Carved input slot (AC-03): fixed literal height/radius per the design's
                  content-level exception - a bare number in sx borderRadius would
                  multiply by theme.shape.borderRadius (20). */}
              <Box
                sx={{
                  position: 'relative',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 2,
                  px: 3,
                  height: '66px',
                  mb: 2.5,
                  borderRadius: '18px',
                  bgcolor: theme.palette.stoneSlot.main,
                  boxShadow: `inset 0 3px 8px ${alpha(theme.palette.stoneEdge.main, 0.45)}`,
                }}
              >
                <Box sx={{ color: 'stoneEdge.main', fontSize: 18, display: 'flex', flexShrink: 0 }}>
                  <FontAwesomeIcon icon="pen-nib" />
                </Box>
                <Controller
                  name="word"
                  control={control}
                  render={({ field }) => (
                    <TextField
                      {...field}
                      onChange={(event) => {
                        setErrorMessage(null);
                        field.onChange(event.currentTarget.value.slice(0, WORD_MAX_LENGTH));
                      }}
                      variant="standard"
                      fullWidth
                      placeholder="type a fun word..."
                      slotProps={{
                        // analytics/01 (AC-03): data-clarity-mask tags THE free-text
                        // word field so Microsoft Clarity never records a child's
                        // typed word in a session replay - a real, code-level
                        // defense-in-depth beyond the project-level "Mask" (strict)
                        // setting the runbook requires (analytics.ts). Never unmask.
                        htmlInput: {
                          maxLength: WORD_MAX_LENGTH,
                          'aria-label': 'Your word',
                          'data-clarity-mask': 'true',
                        },
                        input: { disableUnderline: true },
                      }}
                      sx={{
                        '& .MuiInputBase-input': {
                          fontFamily: '"Fredoka", sans-serif',
                          fontWeight: 500,
                          fontSize: 24,
                          color: 'text.primary',
                          p: 0,
                        },
                      }}
                    />
                  )}
                />
              </Box>

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

              {/* "Need a spark?" row: 3 tappable teal chips from blank.sparkWords (AC-03).
                  No trailing margin - the pinned zone's `gap` spaces it from the CTA. */}
              <Stack>
                <Typography
                  sx={{
                    mb: 1.25,
                    fontFamily: '"Nunito", sans-serif',
                    fontWeight: 700,
                    fontSize: 12.5,
                    color: 'text.secondary',
                  }}
                >
                  Need a spark?
                </Typography>
                <Stack direction="row" spacing={1.25} flexWrap="wrap" useFlexGap>
                  {blank.sparkWords.map((sparkWord) => (
                    <Box
                      key={sparkWord}
                      component="button"
                      type="button"
                      onClick={() => handleSparkTap(sparkWord)}
                      sx={{
                        border: 'none',
                        cursor: 'pointer',
                        px: 2.5,
                        py: 1.25,
                        borderRadius: 999,
                        bgcolor: alpha(theme.palette.teal.main, 0.14),
                        color: theme.palette.teal.dark,
                        fontFamily: '"Nunito", sans-serif',
                        fontWeight: 700,
                        fontSize: 13.5,
                        '&:hover': { bgcolor: alpha(theme.palette.teal.main, 0.22) },
                      }}
                    >
                      {sparkWord}
                    </Box>
                  ))}
                </Stack>
              </Stack>
          </Box>
        ) : (
          answerSurface
        )}

        {/* Primary action, now in the pinned interaction zone (not an absolute
            bar): the gold "Next word" CTA for free text. It uses react-hook-form's
            handleSubmit on click and is hidden when `answerSurface` is supplied -
            that surface owns its own submit (the word-bank "Choose this word"),
            keeping exactly ONE path into onSubmitWord (AC-06). */}
        {answerSurface === undefined && (
          <Button
            type="button"
            variant="contained"
            fullWidth
            disabled={submitting || currentWord.trim().length === 0}
            onClick={handleSubmit(submit)}
          >
            {submitting ? 'Checking...' : 'Next word'}
            <FontAwesomeIcon icon="arrow-right" style={{ width: 18, height: 18 }} />
          </Button>
        )}
        <Box sx={{ textAlign: 'center' }}>
          <Link
            component="button"
            type="button"
            onClick={onSkip}
            underline="none"
            sx={{
              fontFamily: '"Nunito", sans-serif',
              fontWeight: 700,
              fontSize: 13.5,
              color: 'primary.main',
            }}
          >
            Stuck? Skip this word
          </Link>
        </Box>
      </Box>
    </Box>
  );
}
