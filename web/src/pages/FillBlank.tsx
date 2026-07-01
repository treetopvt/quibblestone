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
//    - an optional subject label (the tale's title only) when the parent passes
//      `subject` - Classic blind is `see: 'subject-only'`, so the player sees
//      WHICH tale they are carving but never the story narrative or the filled
//      words (the reveal stays a surprise);
//    - a progress row ("Word N of M" + a chisel icon + "X to go" in teal) and
//      an adaptive-length segment bar (one segment per blank, not a
//      hardcoded 8 - AC-01), with completed/current segments gold and the
//      current segment gently pulsing;
//    - a stone-tablet prompt card (arched, carved rim, glow) with a purple
//      category chip, the blank's prompt sentence, and its sub-hint (AC-02);
//    - a carved input slot (react-hook-form controlled) plus 3 tappable
//      "spark word" chips that fill the input (AC-03);
//    - a purple-tint blind-mode reassurance panel (AC-04);
//    - a gold "Next word" CTA that awaits `onSubmitWord` before advancing,
//      showing the rejection message inline and letting the player retry
//      (AC-05), plus a low-pressure "Skip this word" link (AC-06).
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
//    - `seeContext` renders ABOVE the prompt card (below the subject label,
//      above the progress row) when supplied - e.g. a "story so far" view.
//      Purely additive: never replaces `subject`; omitted, layout is
//      byte-for-byte identical to Classic blind's original rendering.
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
import { AppBar, BottomActionBar, BottomActionBarSpacer } from '../components';
import type { Blank } from '../engine/template';

export interface FillBlankProps {
  /**
   * The tale's title/subject, shown as a small subject label at the top
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
   * subject label, above the progress row) - e.g. a "story so far" view for a
   * progressive-story mode (game-modes/05, `ModeSurfaces.seeContext`). Purely
   * additive: never replaces `subject`. Omitted entirely by default, which
   * keeps today's Classic-blind layout pixel-identical (AC-01).
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

/** The adaptive progress row: "Word N of M" + chisel icon, and "X to go" in teal. */
function ProgressRow({ wordNumber, totalWords }: { wordNumber: number; totalWords: number }) {
  const theme = useTheme();
  const toGo = Math.max(0, totalWords - wordNumber);

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
      <Typography
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 800,
          fontSize: 12.5,
          color: theme.palette.teal.dark,
        }}
      >
        {toGo} to go
      </Typography>
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
    <Stack direction="row" spacing={1} sx={{ mb: 4 }}>
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
        pt: 6,
        pb: 5,
        mb: 4,
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
          mb: 3,
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
          mt: 2,
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
 * The subject-only header (Classic blind is `see: 'subject-only'`): a small
 * "the tale you are carving" kicker plus the tale's title. This is the ONLY
 * story-level context shown - never the narrative or the filled words, so the
 * reveal stays a surprise (AC-02/AC-04). Rendered only when a `subject` is
 * passed.
 */
function SubjectLabel({ subject }: { subject: string }) {
  return (
    <Stack alignItems="center" spacing={0.5} sx={{ mb: 3 }}>
      <Typography
        variant="overline"
        sx={{ fontSize: 11, fontWeight: 800, color: 'text.secondary', lineHeight: 1 }}
      >
        Your tale
      </Typography>
      <Typography
        sx={{
          fontFamily: '"Fredoka", sans-serif',
          fontWeight: 600,
          fontSize: 16,
          lineHeight: 1.2,
          textAlign: 'center',
          color: 'primary.main',
        }}
      >
        {subject}
      </Typography>
    </Stack>
  );
}

/** The blind-mode reassurance panel (AC-04): purple tint, eye-slash icon. */
function BlindReassurance() {
  const theme = useTheme();

  return (
    <Stack
      direction="row"
      alignItems="center"
      spacing={2}
      sx={{
        px: 3,
        py: 2.5,
        mb: 4,
        borderRadius: '18px',
        bgcolor: alpha(theme.palette.primary.main, 0.08),
      }}
    >
      <Box sx={{ color: 'primary.main', fontSize: 20, display: 'flex', flexShrink: 0 }}>
        <FontAwesomeIcon icon="eye-slash" />
      </Box>
      <Typography
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 700,
          fontSize: 13.5,
          color: 'text.secondary',
        }}
      >
        Blind mode - no peeking at the story. The big reveal comes at the end!
      </Typography>
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
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      {/* Optional leave affordance (matches every other in-game screen). Rendered
          only when the parent wires onExit, so callers that omit it keep the old
          no-app-bar layout. */}
      {onExit && (
        <AppBar
          title="Carving words"
          leftAction={{ icon: 'xmark', label: exitLabel ?? 'Leave game', onClick: onExit }}
        />
      )}
      <Stack component="form" onSubmit={handleSubmit(submit)} sx={{ px: 5.5, pt: onExit ? 2 : 4 }}>
        {subject && <SubjectLabel subject={subject} />}
        {seeContext}
        <ProgressRow wordNumber={wordNumber} totalWords={totalWords} />
        <SegmentBar wordNumber={wordNumber} totalWords={totalWords} />

        <PromptCard blank={blank} />

        {answerSurface ?? (
          <>
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
                      htmlInput: { maxLength: WORD_MAX_LENGTH, 'aria-label': 'Your word' },
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

            {/* "Need a spark?" row: 3 tappable teal chips from blank.sparkWords (AC-03). */}
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
          </>
        )}

        <BlindReassurance />
        <BottomActionBarSpacer />
      </Stack>

      <BottomActionBar>
        {/* The gold CTA lives in BottomActionBar, which sits OUTSIDE the
            <Stack component="form"> above (so it can be pinned via its own
            absolutely-positioned wrapper) - so this button cannot rely on a
            native form submit and instead explicitly invokes react-hook-form's
            handleSubmit on click. Hidden when `answerSurface` is supplied: its
            disabled/submitting state keys off the free-text form value, which
            stays permanently empty for a non-free-text surface - that surface
            owns its own submit affordance instead (see FillBlankProps header),
            keeping exactly ONE path into onSubmitWord (AC-06). */}
        {!answerSurface && (
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
      </BottomActionBar>
    </Box>
  );
}
