// ----------------------------------------------------------------------------
//  RoundComplete - the round-complete recap + replay screen (group-play/04,
//  docs/design/README.md Screens screen 7, docs/design/screens/07-roundcomplete.png;
//  issue #33).
//
//  This is the screen that closes the replay loop that makes group play
//  self-sustaining: after the shared Reveal, the group sees a celebratory recap
//  of the round just carved and picks what happens next WITHOUT returning Home or
//  re-entering a code (AC-04/AC-05). It shows (AC-01/02/03):
//    - A teal "ROUND {n} CARVED" badge (the round number rides in from the hub's
//      RoundStarted broadcast via useGameHub's `round.roundNumber`, kept set through
//      the reveal so this badge has it), CSS-only confetti (8 pieces, palette
//      colors, mirroring Reveal.tsx's pattern - no canvas), and a "Round complete!"
//      header.
//    - A stone-tablet keepsake panel (reusing theme.palette.tablet.gradient like
//      Reveal / Lobby) with the story title, a favorite/star toggle
//      (story-selection/06, AC-01 - a PRIVATE per-device action any player may
//      do, deliberately NOT host-gated, unlike the two CTAs below which move
//      the whole group), a "{n} words" pill, and a "{n} carvers" pill.
//    - A "Carved by your crew" row: one 56px Guardian per crew member, with the
//      player's display name and a teal per-player word-count caption ("2 words" /
//      "1 word"). The per-player counts SUM to the total blanks in the template
//      (every blank counts, including skipped/empty-word blanks) - the attribution
//      is DERIVED CLIENT-SIDE in App from the reveal payload (the reveal already
//      carries each blank's owner nickname + variant), so there is no extra server
//      round-trip.
//
//  Host-driven, matching group-play/01 (Slice 1 keeps the host as the single
//  decision-maker): ONLY the host sees the gold "Play another round" CTA, the
//  outlined-purple "Back to lobby" button, and (replay-remix/01, issue #60) a
//  third, lower-emphasis text action "Carve it again" that replays the EXACT
//  template just finished (same room + players, brand-new blanks - no template
//  picker). Non-hosts see the same recap plus a calm passive note ("Waiting for
//  the host to pick what's next") and NO action buttons - they are moved when the
//  host acts (the server broadcasts RoundStarted for a new round, or a bare
//  "BackToLobby" to return everyone to the Lobby). "Play another round" and
//  "Carve it again" both reuse the SAME room + players (no re-join, AC-04) - the
//  only difference is whether App.tsx pins the just-finished templateId on the
//  startRound call; "Back to lobby" returns ALL players to the still-live Lobby
//  (same code, AC-05). Whether Back to lobby should stay host-only is a product
//  call - it moves everyone, so for Slice 1 the host owns it (see openQuestions);
//  non-hosts follow.
//
//  Child safety (AC-06): every crew name shown here was safety-filtered at join
//  (session-engine/02) and no PII is carried (nickname + Guardian variant only - the
//  connectionId stays server-side). This screen introduces NO new free-text surface.
//
//  Styling: every color / radius / spacing comes from the MUI theme (no hex / rgb /
//  raw-px literals). The stone-tablet keepsake uses literal px borderRadius strings
//  (the sx borderRadius gotcha: a bare number multiplies by theme.shape.borderRadius
//  = 20, which would corrupt an arched shape - see Reveal.tsx / Lobby.tsx). Icons
//  are FontAwesome only (registered in web/src/fontawesome.ts). Big tap targets,
//  kid-readable. No em dashes in any prose / strings.
// ----------------------------------------------------------------------------

import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, keyframes, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography } from '@mui/material';
import { AppBar, BottomActionBar, BottomActionBarSpacer, FavoriteStarButton, Guardian, TaleFeedback } from '../components';
import type { GuardianVariant } from '../components';

/** One crew member's recap tile data (already safety-filtered at join; no PII). */
export interface RoundCompleteCrewMember {
  /** In-session display name (filtered at join, AC-06). */
  nickname: string;
  /** The player's chosen Guardian variant, for the 56px avatar. */
  variant: GuardianVariant;
  /** How many of the template's blanks this player carved (sums across the crew to totalWords). */
  wordCount: number;
}

export interface RoundCompleteProps {
  /** 1-based round number for the "ROUND {n} CARVED" badge (from the hub's RoundStarted). */
  roundNumber: number;
  /** The story title shown on the keepsake panel (resolved from the template client-side). */
  title: string;
  /**
   * The just-played template's id (story-selection/05, AC-01): threaded from
   * App.tsx's `reveal.templateId` so the quiet <TaleFeedback> curation vote
   * below the crew row can attribute the vote to the right template. This is
   * group play's ONE vote surface for a round - the shared Reveal screen omits
   * it for group play (see Reveal.tsx's `taleFeedback` prop doc) so a round is
   * never asked about twice.
   */
  templateId: string;
  /** The crew recap, in reveal order (nickname + variant + per-player word count). */
  crew: RoundCompleteCrewMember[];
  /** Total blanks in the template (the "{n} words" pill); the per-player counts sum to this. */
  totalWords: number;
  /**
   * reveal-delight/03 (AC-04): the nickname wearing the Golden Guardian crown this
   * round (the previous round's funniest-word winner), or null when no crown applies.
   * The matching crew tile's Guardian shows the crown overlay.
   */
  crownedNickname?: string | null;
  /** Whether THIS client is the host - gates the two action buttons (Slice 1 host-driven). */
  isHost: boolean;
  /**
   * Whether a new round can start right now - true when the room still has at least
   * two carvers. If the other player left (a 2-player game dropping to one), the
   * server would reject startRound (it needs >=1 other player), so the gold CTA is
   * disabled with a hint steering the host to "Back to lobby" rather than being a
   * live button that silently does nothing.
   */
  canPlayAgain: boolean;
  /** A friendly message when a "Play another round" attempt was rejected server-side (else null). */
  playAgainError?: string | null;
  /** Host-only: begin a NEW round for the same group (same room + players, no re-join, AC-04). */
  onPlayAgain: () => void;
  /**
   * Host-only (replay-remix/01, AC-01/AC-05): replay the SAME tale just finished
   * (same room + players + template id, fresh blanks) - a faster "again!" reflex
   * than "Play another round"'s new-template pick. Deliberately the lower-emphasis
   * action beside the gold primary CTA (a text-variant tertiary action, matching
   * the "Leave the game" pattern in Waiting.tsx) so it never outranks "Play
   * another round".
   */
  onCarveItAgain: () => void;
  /** Host-only: return ALL players to the still-live Lobby (same code, AC-05). */
  onBackToLobby: () => void;
  /** The app-bar exit / leave action (drops the room and returns Home). */
  onLeave: () => void;
}

// CSS-only confetti fall+spin (mirrors Reveal.tsx's confettiFall - no canvas, no
// library). Each piece translates down and rotates over its own duration/delay.
const confettiFall = keyframes`
  0% { transform: translateY(-10px) rotate(0deg); }
  100% { transform: translateY(14px) rotate(220deg); }
`;

// The stone tablet's pulsing glow (mirrors Reveal.tsx's tabletGlow): alternates a
// purple-tinted and gold-tinted shadow over ~4s.
const tabletGlow = keyframes`
  0%, 100% { box-shadow: 0 24px 50px -22px var(--qs-glow-purple), 0 0 0 6px var(--qs-glow-rim), inset 0 3px 0 var(--qs-glow-inner), inset 0 -5px 14px var(--qs-glow-edge); }
  50% { box-shadow: 0 28px 56px -20px var(--qs-glow-gold), 0 0 0 6px var(--qs-glow-rim), inset 0 3px 0 var(--qs-glow-inner), inset 0 -5px 14px var(--qs-glow-edge); }
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
// (mirrors Reveal.tsx's CONFETTI_PIECES, AC-01 / out-of-scope: no canvas).
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

/** CSS-only confetti band: 8 pieces, palette colors, fall+spin (mirrors Reveal.tsx). */
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

/** The teal "ROUND {n} CARVED" badge (AC-01) - a chisel glyph + the round label. */
function RoundBadge({ roundNumber }: { roundNumber: number }) {
  const theme = useTheme();
  return (
    <Box
      component="span"
      sx={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 1,
        px: 2,
        py: 0.75,
        borderRadius: 999,
        bgcolor: alpha(theme.palette.teal.main, 0.16),
        color: theme.palette.teal.dark,
        fontFamily: '"Nunito", sans-serif',
        fontWeight: 800,
        fontSize: 12.5,
        letterSpacing: 0.6,
      }}
    >
      <FontAwesomeIcon icon="hammer" style={{ width: 14, height: 14 }} />
      ROUND {roundNumber} CARVED
    </Box>
  );
}

/** A rounded stat pill for the keepsake panel (word / carver counts, AC-02). */
function StatPill({ icon, label }: { icon: 'pen-nib' | 'users'; label: string }) {
  const theme = useTheme();
  return (
    <Box
      component="span"
      sx={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 0.75,
        px: 1.75,
        py: 0.75,
        borderRadius: 999,
        bgcolor: alpha(theme.palette.primary.main, 0.12),
        color: theme.palette.primary.main,
        fontFamily: '"Nunito", sans-serif',
        fontWeight: 800,
        fontSize: 13,
      }}
    >
      <FontAwesomeIcon icon={icon} style={{ width: 14, height: 14 }} />
      {label}
    </Box>
  );
}

/** One crew member's recap tile: 56px Guardian + name + teal word-count caption (AC-03). */
function CrewTile({ member, crowned }: { member: RoundCompleteCrewMember; crowned: boolean }) {
  const theme = useTheme();
  // "1 word" vs "N words" - a tiny plural so the caption reads naturally (AC-03).
  const wordLabel = `${member.wordCount} ${member.wordCount === 1 ? 'word' : 'words'}`;

  return (
    <Stack alignItems="center" spacing={1} sx={{ width: 84 }}>
      <Box
        sx={{
          width: 74,
          height: 74,
          borderRadius: '50%',
          bgcolor: 'rosterTile.fill',
          border: `2.5px solid ${theme.palette.rosterTile.border}`,
          boxShadow: `0 8px 16px -10px ${alpha(theme.palette.stoneEdge.main, 0.7)}`,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Guardian variant={member.variant} size={56} crowned={crowned} />
      </Box>
      <Typography
        sx={{
          fontFamily: '"Fredoka", sans-serif',
          fontWeight: 500,
          fontSize: 14,
          lineHeight: 1.1,
          color: 'text.primary',
          textAlign: 'center',
          maxWidth: '100%',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        }}
      >
        {member.nickname}
      </Typography>
      <Typography
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 800,
          fontSize: 12,
          color: theme.palette.teal.dark,
        }}
      >
        {wordLabel}
      </Typography>
    </Stack>
  );
}

export function RoundComplete({
  roundNumber,
  title,
  crew,
  totalWords,
  crownedNickname,
  isHost,
  canPlayAgain,
  playAgainError,
  onPlayAgain,
  onCarveItAgain,
  onBackToLobby,
  onLeave,
  templateId,
}: RoundCompleteProps) {
  const theme = useTheme();
  const crewCount = crew.length;
  const wordsLabel = `${totalWords} ${totalWords === 1 ? 'word' : 'words'}`;
  const carversLabel = `${crewCount} ${crewCount === 1 ? 'carver' : 'carvers'}`;

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto', overflow: 'hidden' }}>
      <Confetti />

      {/* Keep the app bar (and its exit icon) above the confetti - the confetti is
          absolutely positioned, so without this it would paint over the exit. */}
      <Box sx={{ position: 'relative', zIndex: 1 }}>
        <AppBar
          title="Round complete"
          leftAction={{ icon: 'xmark', label: 'Leave game', onClick: onLeave }}
        />
      </Box>

      {/* Celebratory header: badge + "Round complete!" (AC-01). */}
      <Stack alignItems="center" spacing={1.5} sx={{ position: 'relative', textAlign: 'center', px: 5.5, pt: 1, pb: 2 }}>
        <RoundBadge roundNumber={roundNumber} />
        <Typography
          component="h2"
          sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 700, fontSize: 28, color: 'text.primary' }}
        >
          Round complete!
        </Typography>
      </Stack>

      <Stack sx={{ px: 5.5 }} spacing={4}>
        {/* Stone-tablet keepsake panel (AC-02): title + words pill + carvers pill.
            Literal px borderRadius - a bare sx number multiplies by
            theme.shape.borderRadius (20), corrupting the carved-tablet shape. */}
        <Box
          sx={{
            position: 'relative',
            borderRadius: '30px',
            px: 5,
            py: 4.5,
            textAlign: 'center',
            background: theme.palette.tablet.gradient,
            '--qs-glow-purple': alpha(theme.palette.primary.main, 0.5),
            '--qs-glow-gold': alpha(theme.palette.gold.main, 0.55),
            '--qs-glow-rim': alpha(theme.palette.common.white, 0.3),
            '--qs-glow-inner': alpha(theme.palette.common.white, 0.5),
            '--qs-glow-edge': alpha(theme.palette.stoneEdge.main, 0.35),
            animation: `${tabletGlow} 4s ease-in-out infinite`,
          }}
        >
          <Typography
            variant="overline"
            sx={{ display: 'block', fontSize: 11, fontWeight: 800, color: 'text.secondary' }}
          >
            Your tale
          </Typography>
          <Typography
            component="h3"
            sx={{
              mt: 0.5,
              fontFamily: '"Fredoka", sans-serif',
              fontWeight: 700,
              fontSize: 22,
              lineHeight: 1.2,
              color: 'primary.main',
            }}
          >
            {title}
          </Typography>
          {/* Favorite/star toggle (story-selection/06, AC-01): a private,
              per-device action any player may do - NOT host-gated, unlike the
              Play/Back-to-lobby CTAs below (those move the whole group). */}
          <Box sx={{ display: 'flex', justifyContent: 'center', mt: 1 }}>
            <FavoriteStarButton templateId={templateId} title={title} />
          </Box>
          <Stack direction="row" justifyContent="center" spacing={1.25} sx={{ mt: 1.5 }}>
            <StatPill icon="pen-nib" label={wordsLabel} />
            <StatPill icon="users" label={carversLabel} />
          </Stack>
        </Box>

        {/* "Carved by your crew" row (AC-03): 56px Guardians + name + teal count. */}
        <Box>
          <Typography
            component="h3"
            sx={{
              mb: 2.5,
              fontFamily: '"Fredoka", sans-serif',
              fontWeight: 600,
              fontSize: 18,
              color: 'text.primary',
            }}
          >
            Carved by your crew
          </Typography>
          <Stack
            direction="row"
            useFlexGap
            flexWrap="wrap"
            justifyContent="center"
            rowGap={3}
            columnGap={1.5}
          >
            {crew.map((member) => (
              // ConnectionId is not on the wire (no PII), so a crew member is keyed
              // by nickname - unique within a room (enforced at join, AC-06).
              <CrewTile
                key={member.nickname}
                member={member}
                crowned={!!crownedNickname && member.nickname === crownedNickname}
              />
            ))}
          </Stack>
        </Box>

        {/* Quiet per-tale curation vote (story-selection/05, AC-01): sits below
            the crew row, visually subordinate to the host's CTAs pinned below -
            group play's ONE feedback surface for this round. */}
        <TaleFeedback templateId={templateId} mode="classic-blind" />

        <BottomActionBarSpacer />
      </Stack>

      {/* Host-driven actions (Slice 1, matching group-play/01's host-driven start).
          Only the host sees the two CTAs; non-hosts see a calm passive note and are
          moved when the host acts (AC-04/AC-05). */}
      {isHost ? (
        <BottomActionBar>
          <Button
            variant="contained"
            fullWidth
            disabled={!canPlayAgain}
            onClick={onPlayAgain}
            startIcon={<FontAwesomeIcon icon="arrow-rotate-right" style={{ width: 20, height: 20 }} />}
          >
            Play another round
          </Button>
          {(playAgainError || !canPlayAgain) && (
            <Typography
              sx={{
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 12.5,
                color: playAgainError ? 'coral.main' : 'text.secondary',
                textAlign: 'center',
              }}
            >
              {playAgainError ?? 'You need at least one more carver - head back to the lobby and wait for them.'}
            </Typography>
          )}
          <Button
            variant="outlined"
            fullWidth
            onClick={onBackToLobby}
            startIcon={<FontAwesomeIcon icon="users" style={{ width: 18, height: 18 }} />}
          >
            Back to lobby
          </Button>
          {/* Carve it again (replay-remix/01, AC-01): the lower-emphasis, tertiary
              replay action - same room + template, fresh blanks. A text-variant
              button (mirrors Waiting.tsx's "Leave the game" pattern) so it never
              outranks the gold "Play another round" CTA above. */}
          <Box sx={{ textAlign: 'center' }}>
            <Button
              variant="text"
              disabled={!canPlayAgain}
              onClick={onCarveItAgain}
              startIcon={<FontAwesomeIcon icon="pen-ruler" style={{ width: 15, height: 15 }} />}
              sx={{ fontSize: 14, fontWeight: 700, color: 'primary.main' }}
            >
              Carve "{title}" again
            </Button>
          </Box>
        </BottomActionBar>
      ) : (
        <BottomActionBar>
          <Stack direction="row" alignItems="center" justifyContent="center" spacing={1}>
            <Box sx={{ color: 'gold.main', fontSize: 14, display: 'flex' }}>
              <FontAwesomeIcon icon="crown" />
            </Box>
            <Typography
              sx={{
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 13,
                color: 'text.secondary',
                textAlign: 'center',
              }}
            >
              Waiting for the host to pick what's next
            </Typography>
          </Stack>
        </BottomActionBar>
      )}
    </Box>
  );
}
