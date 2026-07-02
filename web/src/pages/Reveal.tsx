// ----------------------------------------------------------------------------
//  Reveal - the payoff screen (the-reveal/01, docs/design/README.md Screens
//  screen 6; issue #34).
//
//  This is the moment "everyone has been waiting to laugh at" (README section
//  10 - "the payoff moment ... deserves the most love"): CSS-only confetti, a
//  "Your tale is carved!" header with twinkling star glyphs, an optional
//  caller-supplied attribution slot, and the assembled story rendered inside a
//  glowing stone-tablet scroll panel with every player-filled word popping in
//  coral. Text only for Slice 1 (AC-05): no TTS audio, no AI illustration.
//
//  REUSE CONTRACT (read this before changing the props): this screen is
//  consumed by BOTH single-player/01 (solo, a personal word-count summary) and
//  later group-play (a "carved by [names] & crew" byline) - see RevealProps.
//  Reveal itself renders NEITHER of those bylines; it only exposes an optional
//  `attribution` slot that the caller fills in (or omits entirely). This is
//  what lets solo reuse the exact same screen without editing it. Likewise
//  `onPlayAgain` / `onHome` are caller-owned: Reveal never decides what
//  "another round" or "go home" means for its host feature.
//
//  Rendering the story: Reveal does NOT re-implement assemble()'s attribution
//  logic. It walks `template.body` via the pure `buildRevealParts()` helper
//  (./revealParts.ts, unit-tested in revealParts.test.ts) to interleave literal
//  text with each filled word IN PLACE, matching filled words to blanks in body
//  order - exactly how assemble() itself paired them.
//
//  Child safety (AC-04): every word in `assembled.filledWords` already passed
//  the safety filter upstream (solo checks at submit via the engine boundary;
//  group checks server-side, per child-safety/01). Reveal renders those vetted
//  words verbatim and introduces no new unfiltered free-text surface.
//
//  Share the tale: Reveal OWNS the Web Share integration internally (mirrors
//  Lobby.tsx's ShareWidget, session-engine/04) - feature-detects
//  `navigator.share`, shares `{ title, text }`, swallows a user-cancelled
//  AbortError, and falls back to `navigator.clipboard` when Web Share is
//  unavailable. The Share button stays visible in the bottom bar either way
//  (AC-06) - unlike ShareWidget's Copy/Share pair, there is no separate Copy
//  button here, so Share must always offer SOME action.
//
//  Narration bar (AC-07): rendered but INACTIVE in Slice 1 - the play button is
//  disabled (a "coming soon" affordance) and the waveform bars are static (no
//  animation). The real estate is reserved so Phase 3 can wire TTS with no
//  layout change; no audio is implemented here.
//
//  Mode-aware slot (game-modes/03): an optional `revealPresentation` node
//  REPLACES the default coral-highlight body (the `parts.map(...)` block
//  below) when supplied - e.g. a paced, word-by-word reveal for a
//  progressively-reveal mode (game-modes/06, `ModeSurfaces.revealPresentation`).
//  It renders inside the SAME stone-tablet scroll panel, in place of the
//  default body only - the title, narration bar, confetti, and bottom CTAs are
//  unaffected either way. Omitted by default, which keeps today's
//  `buildRevealParts(template, assembled)` rendering byte-for-byte (AC-03).
//  `buildRevealParts` itself is not touched by this slot - any presentation
//  that needs the same highlight-correctness logic (05/06) reuses it
//  read-only, exactly as this file already does.
//
//  Styling: every color comes from theme tokens (theme.palette.coral.main for
//  the highlight color per the coral reconciliation note - the WEIGHT/underline
//  emphasis is content-level sx, but the color itself is never a hardcoded hex).
//  The stone-tablet gradient/glow reuses theme.palette.tablet.gradient (see
//  Home.tsx's hero tablet and Lobby.tsx's ShareWidget for the same pattern).
//  Arched radii and the pulsing glow keyframe use literal px strings/durations
//  per the story's technical notes (a bare sx borderRadius number multiplies by
//  theme.shape.borderRadius = 20, which would corrupt an arched shape). Icons
//  are FontAwesome only, registered in web/src/fontawesome.ts. No em dashes in
//  any prose/comments/strings.
// ----------------------------------------------------------------------------

import type { ReactNode } from 'react';
import { useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, keyframes, useTheme } from '@mui/material/styles';
import { Box, Button, Link, Stack, Typography } from '@mui/material';
import { AppBar, BottomActionBar, BottomActionBarSpacer } from '../components';
import type { AssembledStory } from '../engine/assemble';
import type { Template } from '../engine/template';
import { buildRevealParts } from './revealParts';

export interface RevealProps {
  /** The assembled story to render (title, storyText, per-blank filled words). */
  assembled: AssembledStory;
  /** The template whose body is walked to interleave literal text with highlighted words. */
  template: Template;
  /**
   * Optional content rendered under the celebratory header. Group play passes
   * a "carved by [names] & crew" byline; single-player passes a personal
   * summary (title + word count). Reveal renders NOTHING here if omitted -
   * it never hardcodes a crew byline, so solo can reuse this screen as-is.
   */
  attribution?: ReactNode;
  /** Gold primary CTA handler. The parent owns what happens next. */
  onPlayAgain: () => void;
  /**
   * Optional label for the gold primary CTA. Defaults to "Play another round"
   * (single-player replays in place). Group play repurposes this same CTA to
   * advance to its Round Complete recap first (group-play/04, AC-01), so it
   * passes a label like "See the round recap" to avoid two identically-labelled
   * "Play another round" buttons back to back.
   */
  playAgainLabel?: string;
  /** Optional home/close action for the app bar. Omit to render a balancing spacer. */
  onHome?: () => void;
  /**
   * Optional low-key "leave this screen" action, rendered as a ghost link below
   * the two primary CTAs so there is a DISCOVERABLE exit (the app-bar icon alone
   * is easy to miss under the confetti). Caller supplies the label so the intent
   * reads right per flow - solo passes "Back to home"; group play routes its exit
   * through Round Complete ("Back to lobby") and can omit this.
   */
  exitAction?: { label: string; onClick: () => void };
  /**
   * Optional mode-supplied presentation that REPLACES the default
   * coral-highlight story body when supplied - e.g. a paced, word-by-word
   * reveal for a progressively-reveal mode (game-modes/06,
   * `ModeSurfaces.revealPresentation`). Rendered inside the same stone-tablet
   * scroll panel, in place of the `buildRevealParts` body only. Omitted by
   * default, which keeps today's coral-highlight rendering byte-for-byte
   * (AC-03).
   */
  revealPresentation?: ReactNode;
}

// The stone tablet's pulsing glow (docs/design/Reveal.dc.html qsTabletGlow):
// alternates between a purple-tinted and gold-tinted shadow over ~4s.
const tabletGlow = keyframes`
  0%, 100% { box-shadow: 0 26px 55px -22px var(--qs-glow-purple), 0 0 0 6px var(--qs-glow-rim), inset 0 3px 0 var(--qs-glow-inner), inset 0 -5px 14px var(--qs-glow-edge); }
  50% { box-shadow: 0 30px 60px -20px var(--qs-glow-gold), 0 0 0 6px var(--qs-glow-rim), inset 0 3px 0 var(--qs-glow-inner), inset 0 -5px 14px var(--qs-glow-edge); }
`;

// A twinkling star glyph (docs/design/Reveal.dc.html qsTwinkle): fades and
// scales in place, never affecting layout of neighboring content.
const twinkle = keyframes`
  0%, 100% { opacity: .3; transform: scale(.8); }
  50% { opacity: 1; transform: scale(1.2); }
`;

// CSS-only confetti fall+spin (AC / out-of-scope: no canvas, no library).
// Each piece translates down and rotates; durations/delays vary per piece.
const confettiFall = keyframes`
  0% { transform: translateY(-10px) rotate(0deg); }
  100% { transform: translateY(14px) rotate(220deg); }
`;

/** One CSS-only confetti piece: color, shape, position, and animation timing. */
interface ConfettiPiece {
  top: number;
  left?: number;
  right?: number;
  size: number;
  round: boolean;
  color: 'coral' | 'teal' | 'primary' | 'gold';
  rotate?: number;
  duration: number;
  delay: number;
}

// 8 pieces, palette colors only, scattered across the celebratory header band
// (docs/design/Reveal.dc.html confetti layout, AC / out-of-scope note).
const CONFETTI_PIECES: readonly ConfettiPiece[] = [
  { top: 8, left: 42, size: 9, round: false, color: 'coral', rotate: 20, duration: 2.6, delay: 0 },
  { top: 34, left: 88, size: 8, round: true, color: 'teal', duration: 3.1, delay: 0.3 },
  { top: 0, left: 150, size: 10, round: false, color: 'primary', rotate: 40, duration: 2.9, delay: 0.5 },
  { top: 50, right: 120, size: 8, round: false, color: 'gold', rotate: -15, duration: 3.3, delay: 0.2 },
  { top: 14, right: 64, size: 9, round: true, color: 'coral', duration: 2.7, delay: 0.6 },
  { top: 40, right: 34, size: 9, round: false, color: 'primary', rotate: 25, duration: 3.0, delay: 0.15 },
  { top: -10, left: 108, size: 7, round: false, color: 'teal', rotate: 30, duration: 3.4, delay: 0.45 },
  { top: 60, left: 60, size: 8, round: false, color: 'gold', duration: 2.8, delay: 0.35 },
];

/** CSS-only confetti band: 8 pieces, palette colors, fall+spin (AC, out-of-scope). */
function Confetti() {
  const theme = useTheme();
  const colorFor = (color: ConfettiPiece['color']) => theme.palette[color].main;

  return (
    <Box
      aria-hidden
      sx={{
        position: 'absolute',
        inset: '0 0 auto 0',
        height: 220,
        overflow: 'hidden',
        pointerEvents: 'none',
      }}
    >
      {CONFETTI_PIECES.map((piece, index) => (
        <Box
          key={index}
          sx={{
            position: 'absolute',
            top: piece.top,
            left: piece.left,
            right: piece.right,
            width: piece.size,
            height: piece.round ? piece.size : piece.size * 1.5,
            bgcolor: colorFor(piece.color),
            borderRadius: piece.round ? '50%' : '2px',
            transform: piece.rotate ? `rotate(${piece.rotate}deg)` : undefined,
            animation: `${confettiFall} ${piece.duration}s ease-in-out ${piece.delay}s infinite alternate`,
          }}
        />
      ))}
    </Box>
  );
}

/** The celebratory header: twinkling stars + "Your tale is carved!" (AC-01). */
function CelebrationHeader() {
  return (
    <Stack alignItems="center" sx={{ position: 'relative', textAlign: 'center', px: 5.5, pt: 0.5, pb: 1.5 }}>
      <Stack direction="row" alignItems="center" spacing={2}>
        <Box sx={{ color: 'gold.main', fontSize: 20, display: 'flex', animation: `${twinkle} 2.4s ease-in-out infinite` }}>
          <FontAwesomeIcon icon="star" />
        </Box>
        <Typography
          component="h2"
          sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 700, fontSize: 26, color: 'text.primary' }}
        >
          Your tale is carved!
        </Typography>
        <Box
          sx={{
            color: 'gold.main',
            fontSize: 20,
            display: 'flex',
            animation: `${twinkle} 2.4s ease-in-out .8s infinite`,
          }}
        >
          <FontAwesomeIcon icon="star" />
        </Box>
      </Stack>
    </Stack>
  );
}

/** The narration bar: play/pause, waveform, label - RENDERED but INACTIVE (AC-07). */
function NarrationBar({ title }: { title: string }) {
  const theme = useTheme();
  // Exactly 12 static waveform bars (docs/design/Reveal.dc.html qsWave layout).
  // No animation in Slice 1 - the waveform does not move (AC-07).
  const barColors = [
    'primary.main', 'primary.main', 'teal.main', 'primary.main', 'gold.main',
    'primary.main', 'primary.main', 'teal.main', 'coral.main', 'primary.main',
    'primary.main', 'teal.main',
  ] as const;

  return (
    <Stack
      direction="row"
      alignItems="center"
      spacing={1.5}
      sx={{
        px: 4,
        py: 3.5,
        bgcolor: alpha(theme.palette.primary.main, 0.1),
        borderBottom: `1.5px solid ${alpha(theme.palette.stoneEdge.main, 0.22)}`,
      }}
    >
      {/* Disabled play affordance: "coming soon" in Slice 1 (AC-07). Real
          narration wiring (Phase 3) swaps this to a live toggle with no
          layout change. */}
      <Box
        component="button"
        type="button"
        disabled
        aria-label="Narration coming soon"
        sx={{
          flexShrink: 0,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: 48,
          height: 48,
          border: 'none',
          borderRadius: '50%',
          bgcolor: 'primary.main',
          color: theme.palette.common.white,
          opacity: 0.5,
          cursor: 'not-allowed',
        }}
      >
        <FontAwesomeIcon icon="play" style={{ width: 18, height: 18 }} />
      </Box>
      <Box sx={{ minWidth: 0, flex: 1 }}>
        <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 15 }}>
          {title}
        </Typography>
        <Typography
          sx={{ fontFamily: '"Nunito", sans-serif', fontWeight: 700, fontSize: 11.5, color: 'text.secondary', mt: 0.25 }}
        >
          Narration coming soon
        </Typography>
      </Box>
      {/* Static waveform (does not animate in Slice 1, AC-07). */}
      <Stack direction="row" alignItems="flex-end" spacing={0.5} sx={{ height: 18, flexShrink: 0 }}>
        {barColors.map((color, index) => (
          <Box
            key={index}
            sx={{ width: 3, height: index % 3 === 0 ? 16 : 10, bgcolor: color, borderRadius: '2px' }}
          />
        ))}
      </Stack>
    </Stack>
  );
}

export function Reveal({
  assembled,
  template,
  attribution,
  onPlayAgain,
  playAgainLabel = 'Play another round',
  onHome,
  exitAction,
  revealPresentation,
}: RevealProps) {
  const theme = useTheme();
  const parts = buildRevealParts(template, assembled);

  // Feature-detect Web Share once - it does not change over the component's
  // lifetime (mirrors Lobby.tsx's ShareWidget, session-engine/04).
  const [canShare] = useState(
    () => typeof navigator !== 'undefined' && typeof navigator.share === 'function',
  );

  // Copy the tale text as the always-available fallback for Share (AC-06 -
  // the button must always DO something). Guards navigator/clipboard and
  // swallows a denied-permission rejection.
  const copyTale = async () => {
    if (typeof navigator === 'undefined' || !navigator.clipboard) return;
    try {
      await navigator.clipboard.writeText(assembled.storyText);
    } catch {
      // Clipboard permission denied or unavailable - fail silently, no error surfaced.
    }
  };

  const handleShare = async () => {
    if (canShare) {
      try {
        await navigator.share({ title: assembled.title, text: assembled.storyText });
      } catch (error) {
        // A user cancellation (AbortError) is intentional - leave it be. Any
        // OTHER rejection (non-secure context, unsupported payload, etc.) means
        // the share never happened, so fall back to clipboard rather than leave
        // the button a silent no-op (AC-06).
        if (error instanceof Error && error.name === 'AbortError') return;
        await copyTale();
      }
      return;
    }
    // Web Share unavailable at all: copy the tale so Share stays actionable.
    await copyTale();
  };

  return (
    <Box
      sx={{
        position: 'relative',
        minHeight: '100dvh',
        maxWidth: 430,
        mx: 'auto',
        overflow: 'hidden',
        // Landscape (design-system/03): a handed-off phone that auto-rotates
        // must not trap the tale in an unreadable sliver. Widen the portrait
        // column and let the page scroll (overflow visible) so the story below
        // can render full-length instead of inside a short capped box. Portrait
        // is untouched - every override is scoped to `orientation: landscape`.
        '@media (orientation: landscape)': { maxWidth: 720, overflow: 'visible' },
      }}
    >
      <Confetti />

      {/* Keep the app bar (and its home icon) above the confetti - the confetti
          is absolutely positioned, so without this it paints over the top-left
          icon and hides the exit. */}
      <Box sx={{ position: 'relative', zIndex: 1 }}>
        <AppBar
          title="The Reveal"
          leftAction={onHome ? { icon: 'house', label: 'Go home', onClick: onHome } : undefined}
        />
      </Box>

      <CelebrationHeader />

      {attribution && (
        <Box sx={{ px: 5.5, pb: 1.5, textAlign: 'center' }}>{attribution}</Box>
      )}

      <Stack sx={{ px: 5, pb: 0 }}>
        {/* STONE-TABLET scroll panel (AC-01): arched radius, glowing carved rim,
            pulsing purple/gold shadow. Literal px strings for the arch and
            glow - a bare sx borderRadius number multiplies by
            theme.shape.borderRadius (20), which would corrupt this shape. */}
        <Box
          sx={{
            position: 'relative',
            borderRadius: '40px 40px 28px 28px',
            background: theme.palette.tablet.gradient,
            overflow: 'hidden',
            '--qs-glow-purple': alpha(theme.palette.primary.main, 0.55),
            '--qs-glow-gold': alpha(theme.palette.gold.main, 0.6),
            '--qs-glow-rim': alpha(theme.palette.common.white, 0.3),
            '--qs-glow-inner': alpha(theme.palette.common.white, 0.55),
            '--qs-glow-edge': alpha(theme.palette.stoneEdge.main, 0.4),
            animation: `${tabletGlow} 4s ease-in-out infinite`,
          }}
        >
          <NarrationBar title="Hear it in the Guardian's voice" />

          {/* Story scroll: independently scrollable, capped so the pinned
              bottom bar can never obscure it (AC-06). In landscape the cap is
              lifted (design-system/03): a short landscape viewport turns 48vh
              into an unreadable sliver, so the panel renders full-length and the
              whole page scrolls instead. Portrait keeps the capped inner scroll. */}
          <Box
            sx={{
              maxHeight: '48vh',
              overflowY: 'auto',
              px: 5,
              py: 4,
              '@media (orientation: landscape)': { maxHeight: 'none', overflowY: 'visible' },
            }}
          >
            <Typography
              component="h3"
              sx={{
                mb: 3,
                fontFamily: '"Fredoka", sans-serif',
                fontWeight: 700,
                fontSize: 23,
                lineHeight: 1.18,
                color: 'primary.main',
              }}
            >
              {assembled.title}
            </Typography>
            {revealPresentation === undefined ? (
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
                    return (
                      <Box key={`p-${index}`} component="span">
                        {part.text}
                      </Box>
                    );
                  }
                  // A skipped blank arrives as an empty-word part. Render it as
                  // plain nothing (no coral treatment) so it reads as a natural
                  // gap rather than a stray zero-width coral underline artifact
                  // (Gate-2 CR-W-001). Only NON-empty, player-supplied words get
                  // the coral pop.
                  if (part.word === '') {
                    return <Box key={`p-${index}`} component="span" />;
                  }
                  return (
                    <Box
                      key={`p-${index}`}
                      component="span"
                      sx={{
                        // AC-02: coral COLOR comes from the theme token (never a
                        // hardcoded hex); the weight/underline emphasis is
                        // content-level styling applied via sx, per the coral
                        // reconciliation note.
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
            ) : (
              revealPresentation
            )}
          </Box>
        </Box>

        <BottomActionBarSpacer />
      </Stack>

      <BottomActionBar>
        <Button
          variant="contained"
          fullWidth
          onClick={onPlayAgain}
          startIcon={<FontAwesomeIcon icon="arrow-rotate-right" style={{ width: 20, height: 20 }} />}
        >
          {playAgainLabel}
        </Button>
        <Button
          variant="outlined"
          fullWidth
          onClick={handleShare}
          startIcon={<FontAwesomeIcon icon="share-nodes" style={{ width: 18, height: 18 }} />}
        >
          Share the tale
        </Button>
        {exitAction && (
          <Box sx={{ textAlign: 'center' }}>
            <Link
              component="button"
              type="button"
              onClick={exitAction.onClick}
              underline="none"
              sx={{
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 13.5,
                color: 'primary.main',
              }}
            >
              {exitAction.label}
            </Link>
          </Box>
        )}
      </BottomActionBar>
    </Box>
  );
}
