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
import { useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, keyframes, useTheme } from '@mui/material/styles';
import { Box, Button, Link, Stack, Typography } from '@mui/material';
import { AppBar, BottomActionBar, BottomActionBarSpacer, TaleFeedback } from '../components';
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
  /**
   * Optional per-tale thumbs feedback slot (story-selection/05, AC-01): when
   * supplied, renders the quiet <TaleFeedback> control below the story panel,
   * subordinate to the CTAs. Single-player passes this ({@link templateId} +
   * mode "solo"). Group play's transient reveal (before its own Round Complete
   * recap, group-play/04) OMITS it - the group's vote surface lives on
   * RoundComplete.tsx instead, so a single round is never asked about twice.
   */
  taleFeedback?: { templateId: string; mode: string };
  /**
   * Optional reaction-row slot (reveal-delight/01, AC-01): rendered in the bottom
   * region, ABOVE the pinned <BottomActionBar> and inside the same
   * BottomActionBarSpacer reservation so it is never hidden behind the bar. Like
   * `attribution` / `taleFeedback` this keeps Reveal ROOM-AGNOSTIC - it renders
   * whatever node the caller passes and knows nothing about counts, the hub, or
   * solo-vs-group. Solo passes <ReactionRow> backed by local state (AC-05); group
   * play passes one backed by the hub's ReactionCountsChanged broadcast (AC-04).
   */
  reactionRow?: ReactNode;
  /**
   * Optional Golden Guardian funniest-word vote (reveal-delight/03). Like every
   * other slot here this keeps Reveal ROOM-AGNOSTIC: Reveal knows nothing about
   * the room, the hub, or who won - it only turns each NON-empty coral word into a
   * tap target and paints the caller-supplied winner. OMIT it entirely in solo
   * (AC-06: there is no room to vote in, so the mechanic is absent - not a
   * disabled no-op). When supplied:
   *   - `phase` 'voting': each coral word is tappable once the carve-in has
   *     finished (AC-01); a tap calls `onVote(blankId)` to cast/MOVE my single
   *     vote, and my current pick (`myVote`) is shown selected. A low-key
   *     "N of M voted" status renders (per-word counts are NOT shown mid-vote,
   *     AC-02). The host (only) may pass `onCloseVoting` to reveal the winner
   *     early via a low-pressure affordance (AC-03).
   *   - `phase` 'resolved': the `winningBlankId` coral word gets a gold ring/glow
   *     (theme.palette.gold.main) and a short warm announcement names it - never a
   *     ranked list or a "loser" callout (AC-03).
   *   - `phase` 'off': render nothing vote-related (a general escape hatch; group
   *     play uses 'voting'/'resolved', solo omits the prop outright).
   * The `blankId` value is an OPAQUE token Reveal assigns per coral word (its
   * body-order blank position) and hands back through `onVote` / matches against
   * `winningBlankId` - the caller passes it to the hub verbatim (AC-07: it is just
   * an already-vetted, already-displayed word's position, no new text, no PII).
   */
  goldenGuardian?: GoldenGuardianVote;
}

/** The Golden Guardian vote slot on the Reveal (reveal-delight/03). See RevealProps.goldenGuardian. */
export interface GoldenGuardianVote {
  /** 'voting' while the room picks, 'resolved' once a winner is known, 'off' to render nothing. */
  phase: 'voting' | 'resolved' | 'off';
  /** Cast (or MOVE) my single vote to the tapped coral word's opaque blank token. */
  onVote: (blankId: string) => void;
  /** The blank token I currently voted for (shown selected), or undefined if I have not voted. */
  myVote?: string;
  /** How many present players have voted so far (the "N of M voted" status, AC-02). */
  votedCount: number;
  /** The total present players who can vote (the "M" in "N of M voted"). */
  totalVoters: number;
  /** When resolved: the winning coral word's blank token (gets the gold ring + announcement). */
  winningBlankId?: string;
  /** Host-only (AC-03): a low-pressure "Reveal the winner" affordance to close voting early. Omit for non-hosts. */
  onCloseVoting?: () => void;
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

// Word-by-word "carving" entrance (reveal-delight/02, AC-01/AC-02): each
// coral filled word pops from a smaller scale up to its natural size. This is
// TRANSFORM ONLY, deliberately - never an `opacity` keyframe step, per this
// feature's documented footgun (an opacity keyframe with fill-mode:both can
// leave a re-rendered list item stuck invisible, which here would make the
// WHOLE story look half-missing). The literal template text and empty-word
// gaps are untouched by this keyframe (AC-01).
const carveIn = keyframes`
  from { transform: scale(.4); }
  to { transform: scale(1); }
`;

// Stagger between each filled word's carve-in entrance, in body order
// (AC-01). Computed per filled-word index, not the raw `parts` index (which
// also counts literal text gaps).
const CARVE_STAGGER_MS = 140;

// The carve-in keyframe's own duration (matches the `0.4s` on the word span's
// animation shorthand below). reveal-delight/03 (AC-01) reads it to know when the
// LAST word has finished carving, so the vote step only becomes interactive after
// the story is fully shown.
const CARVE_DURATION_MS = 400;

// reveal-delight/03 (AC-03): the winning coral word's gentle one-shot pop when the
// vote resolves. TRANSFORM ONLY (scale) - never an opacity keyframe on this
// re-rendered word span (this feature's documented footgun) - so a re-render can
// never strand the winning word invisible. The gold ring/glow itself is a static
// box-shadow applied via sx, not animated here.
const winnerPop = keyframes`
  0% { transform: scale(1); }
  45% { transform: scale(1.14); }
  100% { transform: scale(1); }
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
  taleFeedback,
  reactionRow,
  goldenGuardian,
}: RevealProps) {
  const theme = useTheme();
  const parts = buildRevealParts(template, assembled);

  // reveal-delight/03 (AC-01): the vote step must only become interactive once the
  // story is FULLY shown - i.e. once the last coral word has finished carving in
  // (reveal-delight/02), or IMMEDIATELY when reduced-motion is on / there is no
  // carve to wait for. Count the filled (non-empty) coral words - the carve stagger
  // follows their body order - and flip `carveComplete` after the last one lands.
  const filledWordCount = parts.reduce(
    (count, part) => (part.kind === 'word' && part.word !== '' ? count + 1 : count),
    0,
  );
  const [carveComplete, setCarveComplete] = useState(false);
  useEffect(() => {
    // Respect reduced-motion (the carve is skipped there, so voting is ready at
    // once) and the no-filled-words edge (nothing to carve). Otherwise wait for the
    // last word: (n-1) staggers + one carve duration, plus a little slack.
    const prefersReducedMotion =
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (prefersReducedMotion || filledWordCount === 0) {
      setCarveComplete(true);
      return;
    }
    setCarveComplete(false);
    const totalMs = (filledWordCount - 1) * CARVE_STAGGER_MS + CARVE_DURATION_MS + 80;
    const timer = setTimeout(() => setCarveComplete(true), totalMs);
    return () => clearTimeout(timer);
  }, [filledWordCount]);

  // Voting is live only while phase is 'voting' AND the carve-in has completed
  // (AC-01). Resolution paints the winner regardless of carve timing.
  const voteInteractive = goldenGuardian?.phase === 'voting' && carveComplete;
  const voteResolved = goldenGuardian?.phase === 'resolved';

  // Resolve the winning coral word's TEXT from its opaque blank token (body-order
  // blank position) for the warm announcement (AC-03) - Reveal owns the token, so
  // it maps it back here rather than the caller shipping the word.
  const winningWord = (() => {
    if (!voteResolved || goldenGuardian?.winningBlankId === undefined) return '';
    let position = 0;
    for (const part of parts) {
      if (part.kind !== 'word') continue;
      const token = String(position);
      position += 1;
      if (token === goldenGuardian.winningBlankId && part.word !== '') return part.word;
    }
    return '';
  })();

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
                {(() => {
                  // Running counter over FILLED (non-empty) words only, in body
                  // order, so the carve-in stagger (reveal-delight/02, AC-01)
                  // follows the story's reading order rather than the raw
                  // `parts` index, which also counts literal text gaps.
                  let filledWordIndex = 0;
                  // reveal-delight/03: a SEPARATE counter over EVERY blank (empty or
                  // filled), in body order, so each coral word's opaque vote token
                  // (its blank position) stays aligned with the server's blank
                  // indices - a vote token round-trips to the right word/contributor.
                  let blankPos = 0;
                  return parts.map((part, index) => {
                    if (part.kind === 'text') {
                      return (
                        <Box key={`p-${index}`} component="span">
                          {part.text}
                        </Box>
                      );
                    }
                    // This blank occupies one body-order position whether or not it
                    // was filled - advance the token counter for empty blanks too so
                    // it never drifts out of step with the server's blank indices.
                    const token = String(blankPos);
                    blankPos += 1;
                    // A skipped blank arrives as an empty-word part. Render it as
                    // plain nothing (no coral treatment) so it reads as a natural
                    // gap rather than a stray zero-width coral underline artifact
                    // (Gate-2 CR-W-001). Only NON-empty, player-supplied words get
                    // the coral pop (and, when voting, the tap target).
                    if (part.word === '') {
                      return <Box key={`p-${index}`} component="span" />;
                    }
                    const delayMs = filledWordIndex * CARVE_STAGGER_MS;
                    filledWordIndex += 1;
                    // reveal-delight/03: additive vote state on THIS coral word.
                    const isMyVote = voteInteractive && goldenGuardian?.myVote === token;
                    const isWinner = voteResolved && goldenGuardian?.winningBlankId === token;
                    return (
                      <Box
                        key={`p-${index}`}
                        component="span"
                        // reveal-delight/03 (AC-01): a tap casts/moves my single
                        // vote once voting is interactive; a no-op otherwise. Kept an
                        // inline span (not a <button>) so the story's text flow is
                        // preserved; role/tabIndex/keydown make it operable.
                        onClick={voteInteractive ? () => goldenGuardian?.onVote(token) : undefined}
                        role={voteInteractive ? 'button' : undefined}
                        tabIndex={voteInteractive ? 0 : undefined}
                        aria-pressed={voteInteractive ? isMyVote : undefined}
                        aria-label={
                          voteInteractive ? `Vote for "${part.word}" as the funniest word` : undefined
                        }
                        onKeyDown={
                          voteInteractive
                            ? (event) => {
                                if (event.key === 'Enter' || event.key === ' ') {
                                  event.preventDefault();
                                  goldenGuardian?.onVote(token);
                                }
                              }
                            : undefined
                        }
                        sx={{
                          // AC-02: coral COLOR comes from the theme token (never a
                          // hardcoded hex); the weight/underline emphasis is
                          // content-level styling applied via sx, per the coral
                          // reconciliation note.
                          color: theme.palette.coral.main,
                          fontWeight: 800,
                          borderBottom: `2px solid ${alpha(theme.palette.coral.main, 0.4)}`,
                          // Carving entrance (reveal-delight/02, AC-01/AC-02):
                          // a pure CSS `transform: scale` keyframe, staggered by
                          // body order. Never blocks interactivity elsewhere on
                          // the screen (AC-04) - it only animates this word span.
                          // `transform` does NOT apply to non-replaced INLINE
                          // boxes (CSS Transforms Level 1), so the word span must
                          // be inline-block for the scale to take effect - without
                          // this the carve is a silent no-op (Gate-1 CR-001). The
                          // coral color/weight/underline are unchanged and a word
                          // is a single token, so wrapping and the final rendered
                          // frame stay identical (AC-06).
                          display: 'inline-block',
                          animation: `${carveIn} 0.4s ease-out ${delayMs}ms both`,
                          '@media (prefers-reduced-motion: reduce)': {
                            animation: 'none',
                          },
                          // reveal-delight/03 (AC-01): tappable-word affordance while
                          // voting - ADDITIVE only (coral treatment above unchanged).
                          // `outline` marks my pick without shifting the inline text.
                          ...(voteInteractive && {
                            cursor: 'pointer',
                            borderRadius: '8px',
                            px: 0.5,
                            bgcolor: isMyVote ? alpha(theme.palette.gold.main, 0.22) : 'transparent',
                            outline: isMyVote ? `2px solid ${theme.palette.gold.main}` : 'none',
                            outlineOffset: '1px',
                          }),
                          // reveal-delight/03 (AC-03): the single winner gets a GOLD
                          // ring/glow + a gentle transform-only pop. Never a loser
                          // callout - only this one word is ever styled.
                          ...(isWinner && {
                            borderRadius: '8px',
                            px: 0.5,
                            bgcolor: alpha(theme.palette.gold.main, 0.2),
                            boxShadow: `0 0 0 3px ${theme.palette.gold.main}, 0 0 14px ${alpha(theme.palette.gold.main, 0.7)}`,
                            animation: `${winnerPop} 0.5s ease-out both`,
                          }),
                        }}
                      >
                        {part.word}
                      </Box>
                    );
                  });
                })()}
              </Typography>
            ) : (
              revealPresentation
            )}
          </Box>
        </Box>

        {/* Golden Guardian funniest-word vote status (reveal-delight/03). Sits
            below the story panel: a low-key "tap the funniest word" prompt +
            "N of M voted" status while voting (AC-02), and a warm, singular winner
            announcement once resolved (AC-03) - NEVER a ranked list or a loser
            callout. Rendered only when the caller opts in (absent in solo, AC-06).
            The gold ring on the winning WORD itself lives in the story body above. */}
        {goldenGuardian && goldenGuardian.phase !== 'off' && (
          <Box sx={{ mt: 3, textAlign: 'center' }}>
            {voteResolved ? (
              <Stack alignItems="center" spacing={1}>
                <Box
                  component="span"
                  sx={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 1,
                    px: 2,
                    py: 1,
                    borderRadius: 999,
                    bgcolor: alpha(theme.palette.gold.main, 0.18),
                    color: theme.palette.gold.dark,
                    fontFamily: '"Nunito", sans-serif',
                    fontWeight: 800,
                    fontSize: 13.5,
                  }}
                >
                  <FontAwesomeIcon icon="crown" style={{ width: 16, height: 16 }} />
                  {winningWord
                    ? 'Crowned the funniest word this round'
                    : 'No favorite picked this round'}
                </Box>
                {winningWord && (
                  <Typography
                    sx={{
                      fontFamily: '"Fredoka", sans-serif',
                      fontWeight: 700,
                      fontSize: 18,
                      color: 'primary.main',
                    }}
                  >
                    The funniest word this round: "{winningWord}"
                  </Typography>
                )}
              </Stack>
            ) : (
              <Stack alignItems="center" spacing={1.25}>
                <Stack direction="row" alignItems="center" spacing={1}>
                  <Box sx={{ color: 'gold.main', display: 'flex' }}>
                    <FontAwesomeIcon icon="hand-pointer" style={{ width: 16, height: 16 }} />
                  </Box>
                  <Typography
                    sx={{
                      fontFamily: '"Fredoka", sans-serif',
                      fontWeight: 600,
                      fontSize: 16,
                      color: 'text.primary',
                    }}
                  >
                    {carveComplete
                      ? 'Tap the funniest word'
                      : 'The tale is still carving in...'}
                  </Typography>
                </Stack>
                <Stack direction="row" alignItems="center" spacing={0.75}>
                  <Box sx={{ color: 'teal.main', display: 'flex' }}>
                    <FontAwesomeIcon icon="circle-check" style={{ width: 14, height: 14 }} />
                  </Box>
                  <Typography
                    sx={{
                      fontFamily: '"Nunito", sans-serif',
                      fontWeight: 700,
                      fontSize: 12.5,
                      color: 'text.secondary',
                    }}
                  >
                    {goldenGuardian.votedCount} of {goldenGuardian.totalVoters} voted
                  </Typography>
                </Stack>
                {/* Host-only low-pressure "Reveal the winner" affordance (AC-03) -
                    mirrors the Waiting screen's "no rush, but the host can move
                    things along" posture. Only rendered when the caller (host)
                    supplies onCloseVoting; never shown to non-hosts. */}
                {goldenGuardian.onCloseVoting && (
                  <Link
                    component="button"
                    type="button"
                    onClick={goldenGuardian.onCloseVoting}
                    underline="none"
                    sx={{
                      fontFamily: '"Nunito", sans-serif',
                      fontWeight: 800,
                      fontSize: 13,
                      color: 'gold.dark',
                    }}
                  >
                    Reveal the winner
                  </Link>
                )}
              </Stack>
            )}
          </Box>
        )}

        {/* Quiet per-tale curation vote (story-selection/05, AC-01): sits below
            the story panel, visually subordinate to the CTAs in the pinned bar
            below. Omitted entirely when the caller does not opt in (group
            play's transient reveal - see the taleFeedback prop doc). */}
        {taleFeedback && (
          <TaleFeedback templateId={taleFeedback.templateId} mode={taleFeedback.mode} />
        )}

        <BottomActionBarSpacer />
      </Stack>

      <BottomActionBar>
        {/* Reaction row (reveal-delight/01, AC-01): pinned ABOVE the action
            buttons, inside the same bottom cluster, so it is always visible and
            tappable regardless of viewport height. It must live INSIDE the bar
            (not just above the BottomActionBarSpacer): Reveal's bar holds two
            CTAs plus the exit link, so it is far taller than the spacer's
            single-CTA reservation - a row placed only above the spacer sits
            under the taller absolute bar and its scrim swallows the tap. Reveal
            stays room-agnostic - it renders whatever node the caller passed. */}
        {reactionRow}
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
