// ----------------------------------------------------------------------------
//  Waiting - the calm interstitial a player sees AFTER submitting their LAST
//  assigned word while the rest of the crew is still writing (group-play/03,
//  issue #32; docs/design/README.md Screens - screen 5, docs/design/screens/
//  05-waiting.png).
//
//  This screen is intentionally PASSIVE (AC-04): there is NO gold CTA anywhere
//  on it - the player's words are in and there is nothing for them to DO but
//  wait for the shared reveal to arrive (the hook's `reveal`, which App routes
//  on). The ONE action is a secondary, outlined-purple "Review my words" button
//  that opens a client-side READ-ONLY view of this client's own submitted words
//  (no server round-trip - GroupRound already holds them, AC-04). It is not a
//  progression, just a reassurance affordance, so it never carries the gold CTA
//  weight.
//
//  What it shows (AC-02/AC-03):
//    - AppBar title "Your words are in!" (no gold CTA).
//    - The hero mascot (HeroGuardian) juggling three letter tiles "W O W" - a
//      static pose; the juggling ANIMATION is explicitly out of scope for gp/03
//      (a later delight-tier pass), so the tiles sit in a fixed arc.
//    - The caption "Juggling letters while the others carve...".
//    - A status card: "[N] of [M] quibblers done" with a teal check-circle icon,
//      and "X still writing" underneath.
//    - A row of [M] <Guardian variant size={54} /> tiles: done players at FULL
//      opacity with a teal check badge overlay; still-writing players dimmed
//      (opacity 0.55) with a MUTED name and a PULSING sandstone badge (the pulse
//      is a box-shadow/transform keyframe on the BADGE, never opacity on the tile
//      itself - opacity is the dim state, so animating it would fight the dim).
//    - NO countdown (a product stance, not a gap - the crew finishes when it
//      finishes; group-play/03 out-of-scope explicitly parks a host skip/timeout).
//
//  Child safety / no PII: this screen shows only already-filtered nicknames +
//  Guardian variants (filtered at join, README section 6) and this client's OWN
//  submitted words (each already vetted server-side before it was recorded, AC-06).
//  It never shows ANOTHER player's submitted words - the progress payload carries
//  none (AC-01).
//
//  Styling: theme-driven only (no hex/rgb/raw-px literals - the pulsing badge and
//  the letter tiles use literal px strings only where the sx borderRadius gotcha
//  applies: a bare number multiplies by theme.shape.borderRadius = 20). Icons are
//  FontAwesome only (registered in web/src/fontawesome.ts). No em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, keyframes, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography } from '@mui/material';
import { AppBar, BottomActionBar, BottomActionBarSpacer, Guardian, HeroGuardian } from '../components';
import type { GuardianVariant } from '../components';
import type { CollectProgress } from '../signalr/useGameHub';

/** One of this client's own submitted words, for the read-only "Review my words" view. */
export interface MyWord {
  /** The blank's prompt sentence (Classic blind - the player only ever saw the prompt). */
  prompt: string;
  /** The word this client submitted (empty for a blank they skipped). */
  word: string;
}

export interface WaitingProps {
  /**
   * Room-wide collection progress from the hub (group-play/03), or null until the
   * first "CollectProgress" broadcast arrives. Drives the status card counts and
   * the per-player progress row. Carries NO other player's submitted words (AC-01).
   */
  progress: CollectProgress | null;
  /**
   * This client's OWN submitted words (prompt + word), held locally by GroupRound
   * so "Review my words" needs no server round-trip (AC-04). Empty words are the
   * blanks this client skipped.
   */
  myWords: MyWord[];
  /** Leave the round and return Home. */
  onLeave: () => void;
}

/**
 * The six known Guardian variants. A progress payload's variant is normalized
 * server-side to one of these (session-engine/05), but the wire type is a plain
 * string, so we narrow it here rather than reaching for a cast (TS strict).
 */
const KNOWN_VARIANTS: readonly GuardianVariant[] = ['purple', 'gold', 'coral', 'teal', 'sand', 'plum'];

/** Narrows a wire variant string to a GuardianVariant, defaulting to "teal" (matches the server default). */
function toGuardianVariant(variant: string): GuardianVariant {
  return KNOWN_VARIANTS.find((known) => known === variant) ?? 'teal';
}

// The still-writing badge's gentle pulse: a sandstone ring that breathes via
// box-shadow + a tiny scale (transform), NEVER opacity - the tile is already
// dimmed via opacity for the "still writing" state, so animating opacity would
// fight that. transform-only/box-shadow keeps the dim stable while the badge
// pulses (docs/design/README.md Implementation Gotchas).
const badgePulse = keyframes`
  0%, 100% { box-shadow: 0 0 0 0 var(--qs-badge-glow); transform: scale(1); }
  50% { box-shadow: 0 0 0 5px var(--qs-badge-glow-soft); transform: scale(1.12); }
`;

/** The three static letter tiles "W O W" the hero mascot juggles (static pose - animation out of scope). */
function JuggledLetters() {
  const theme = useTheme();
  // A fixed shallow arc (px offsets) so the tiles read as mid-juggle without any
  // motion. Colors come from the theme palette (gold/coral/teal), never hex.
  const tiles: readonly { letter: string; color: 'gold' | 'coral' | 'teal'; lift: number }[] = [
    { letter: 'W', color: 'gold', lift: 10 },
    { letter: 'O', color: 'coral', lift: 22 },
    { letter: 'W', color: 'teal', lift: 10 },
  ];

  return (
    <Stack direction="row" spacing={2} alignItems="flex-end" aria-hidden sx={{ mb: 1 }}>
      {tiles.map((tile, index) => (
        <Box
          key={index}
          sx={{
            transform: `translateY(-${tile.lift}px)`,
            width: 44,
            height: 44,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            borderRadius: '14px',
            bgcolor: theme.palette[tile.color].main,
            color: theme.palette.common.white,
            fontFamily: '"Fredoka", sans-serif',
            fontWeight: 700,
            fontSize: 22,
            boxShadow: `0 8px 16px -8px ${alpha(theme.palette[tile.color].main, 0.9)}`,
          }}
        >
          {tile.letter}
        </Box>
      ))}
    </Stack>
  );
}

/** The "[N] of [M] quibblers done" status card with the teal check-circle + "X still writing" (AC-03). */
function StatusCard({ doneCount, playerCount }: { doneCount: number; playerCount: number }) {
  const stillWriting = Math.max(0, playerCount - doneCount);

  return (
    <Stack
      alignItems="center"
      spacing={1}
      sx={{
        width: '100%',
        px: 4,
        py: 3,
        borderRadius: '24px',
        bgcolor: 'card.main',
      }}
    >
      <Stack direction="row" alignItems="center" spacing={1.5}>
        <Box sx={{ color: 'teal.main', fontSize: 22, display: 'flex' }}>
          <FontAwesomeIcon icon="circle-check" />
        </Box>
        <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 19 }}>
          {doneCount} of {playerCount} quibblers done
        </Typography>
      </Stack>
      <Typography sx={{ fontFamily: '"Nunito", sans-serif', fontWeight: 700, fontSize: 14, color: 'text.secondary' }}>
        {stillWriting} still writing
      </Typography>
    </Stack>
  );
}

/** One player's tile in the progress row: done (full opacity + teal check badge) or writing (dimmed + pulsing sandstone badge). */
function PlayerTile({ nickname, variant, done }: { nickname: string; variant: string; done: boolean }) {
  const theme = useTheme();
  const guardianVariant = toGuardianVariant(variant);

  return (
    <Stack alignItems="center" spacing={0.75} sx={{ width: 64 }}>
      <Box sx={{ position: 'relative', opacity: done ? 1 : 0.55 }}>
        <Guardian variant={guardianVariant} size={54} />
        {done ? (
          // Done: a solid teal check badge overlay at the tile's corner.
          <Box
            sx={{
              position: 'absolute',
              right: -2,
              bottom: -2,
              width: 22,
              height: 22,
              borderRadius: '50%',
              bgcolor: 'teal.main',
              color: theme.palette.common.white,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: 12,
              boxShadow: `0 2px 6px -2px ${alpha(theme.palette.teal.main, 0.9)}`,
            }}
          >
            <FontAwesomeIcon icon="check" />
          </Box>
        ) : (
          // Still writing: a pulsing sandstone badge (box-shadow/transform pulse,
          // NOT opacity - the tile itself carries the dim). The pulse ring uses
          // CSS vars fed by the theme so no raw color leaks into the keyframe.
          <Box
            aria-hidden
            sx={{
              position: 'absolute',
              right: -2,
              bottom: -2,
              width: 18,
              height: 18,
              borderRadius: '50%',
              bgcolor: 'sandstone.main',
              border: `2px solid ${theme.palette.rosterTile.border}`,
              '--qs-badge-glow': alpha(theme.palette.stoneEdge.main, 0.55),
              '--qs-badge-glow-soft': alpha(theme.palette.stoneEdge.main, 0),
              animation: `${badgePulse} 1.6s ease-in-out infinite`,
            }}
          />
        )}
      </Box>
      <Typography
        noWrap
        sx={{
          maxWidth: '100%',
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 700,
          fontSize: 11.5,
          textAlign: 'center',
          // Still-writing names read muted; done names use the primary text color.
          color: done ? 'text.primary' : 'text.secondary',
        }}
      >
        {nickname}
      </Typography>
    </Stack>
  );
}

/** The client-side READ-ONLY review of this client's own submitted words (AC-04); no server round-trip. */
function ReviewMyWords({ myWords, onBack, onLeave }: { myWords: MyWord[]; onBack: () => void; onLeave: () => void }) {
  const theme = useTheme();

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Your words"
        leftAction={{ icon: 'arrow-left', label: 'Back to waiting', onClick: onBack }}
      />

      <Stack spacing={2.5} sx={{ px: 5.5, pt: 3 }}>
        <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.secondary', textAlign: 'center' }}>
          The words you carved this round. No changing them now - hang tight for the reveal!
        </Typography>

        {myWords.length === 0 ? (
          <Typography sx={{ fontSize: 15, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
            You had no blanks to fill this round.
          </Typography>
        ) : (
          <Stack spacing={1.5}>
            {myWords.map((entry, index) => (
              <Stack
                key={index}
                spacing={0.5}
                sx={{ px: 3.5, py: 2, borderRadius: '18px', bgcolor: 'card.main' }}
              >
                <Typography
                  sx={{ fontFamily: '"Nunito", sans-serif', fontWeight: 700, fontSize: 12.5, color: 'text.secondary' }}
                >
                  {entry.prompt}
                </Typography>
                <Typography
                  sx={{
                    fontFamily: '"Fredoka", sans-serif',
                    fontWeight: 600,
                    fontSize: 20,
                    color: entry.word.trim().length > 0 ? theme.palette.coral.main : 'text.secondary',
                  }}
                >
                  {entry.word.trim().length > 0 ? entry.word : '(skipped)'}
                </Typography>
              </Stack>
            ))}
          </Stack>
        )}

        <BottomActionBarSpacer />
      </Stack>

      <BottomActionBar>
        <Button variant="outlined" fullWidth onClick={onBack}>
          Back to waiting
        </Button>
        <Box sx={{ textAlign: 'center' }}>
          <Button
            variant="text"
            onClick={onLeave}
            sx={{ fontSize: 13.5, fontWeight: 700, color: 'primary.main' }}
          >
            Leave the game
          </Button>
        </Box>
      </BottomActionBar>
    </Box>
  );
}

export function Waiting({ progress, myWords, onLeave }: WaitingProps) {
  // Local, client-side toggle for the read-only "Review my words" view (AC-04) -
  // no server round-trip, no routing change; this screen owns it.
  const [reviewing, setReviewing] = useState(false);

  if (reviewing) {
    return <ReviewMyWords myWords={myWords} onBack={() => setReviewing(false)} onLeave={onLeave} />;
  }

  // Progress may not have arrived yet for this client (a brief beat after its own
  // submit) - fall back to sensible counts so the card never renders "undefined".
  const doneCount = progress?.doneCount ?? 0;
  const playerCount = progress?.playerCount ?? 0;
  const players = progress?.players ?? [];

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      {/* No gold CTA anywhere on this screen (AC-04) - the app bar carries only a
          leave affordance, and the ONE action below is the outlined-purple review. */}
      <AppBar
        title="Your words are in!"
        leftAction={{ icon: 'xmark', label: 'Leave round', onClick: onLeave }}
      />

      <Stack spacing={3} alignItems="center" sx={{ px: 5.5, pt: 2, textAlign: 'center' }}>
        <JuggledLetters />
        <HeroGuardian width={150} />

        <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 19 }}>
          Juggling letters while the others carve...
        </Typography>

        <StatusCard doneCount={doneCount} playerCount={playerCount} />

        {players.length > 0 && (
          <Stack
            direction="row"
            spacing={1}
            flexWrap="wrap"
            useFlexGap
            justifyContent="center"
            sx={{ width: '100%' }}
          >
            {players.map((player, index) => (
              <PlayerTile
                key={index}
                nickname={player.nickname}
                variant={player.variant}
                done={player.done}
              />
            ))}
          </Stack>
        )}

        <BottomActionBarSpacer />
      </Stack>

      <BottomActionBar>
        {/* The ONLY action: a secondary, outlined-purple "Review my words" (AC-04).
            Opens the client-side read-only view above - no gold CTA, intentionally
            passive. */}
        <Button
          variant="outlined"
          fullWidth
          onClick={() => setReviewing(true)}
          startIcon={<FontAwesomeIcon icon="pen-ruler" style={{ width: 18, height: 18 }} />}
        >
          Review my words
        </Button>
      </BottomActionBar>
    </Box>
  );
}
