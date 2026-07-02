// ----------------------------------------------------------------------------
//  Lobby - the live player roster / waiting room (session-engine/03).
//
//  This is the full roster screen story 03 owns, replacing the story-01
//  placeholder. It renders entirely from the hook's LIVE room state
//  (room.players): the server broadcasts "RosterChanged" on every join AND on
//  every leave (OnDisconnectedAsync), useGameHub feeds that into `room`, and this
//  screen simply reacts - so newcomers appear and departed players' tiles revert
//  to empty slots in near-real-time (AC-02, AC-04) without any polling here.
//
//  What it shows (design: docs/design/Lobby.dc.html, docs/design/README.md
//  "Screens" screen 3):
//    - The stone-tablet share widget (session-engine/04): the room code in big
//      purple Fredoka type, an outlined-purple "Copy" button (flips to a
//      teal-check "Copied!" for ~1.8s on tap, no server round-trip - the code
//      is already local client state) and a filled-purple "Share" button that
//      invokes the Web Share API when available (hidden otherwise - AC-04),
//      plus the "share this code" hint line.
//    - "Carvers gathered" with a live teal count chip "{n} of {MAX_PLAYERS}" (AC-01).
//    - A 3-column grid, up to MAX_PLAYERS (6). Each present player is a carved
//      stone tile (theme rosterTile tokens) with their Guardian, name, and a
//      role chip: host = gold "HOST" chip + crown badge + a pulsing gold RING;
//      everyone else = teal "READY" chip (AC-01). New tiles scale-pop in (AC-02).
//    - Every remaining slot is a dashed circle with 3 pulsing dots + "waiting..."
//      whose border pulses purple (AC-03).
//    - A transient dark bottom-center toast "[Name] pulled up a stone" when a new
//      player appears (not on the initial roster, not for yourself) (AC-02).
//    - ONLY when this client is the host: the host-only family-safe toggle
//      (group-play/01), the host-only story-length choice (story-selection/02,
//      placed the SAME way - host-only, right beside the family-safe toggle),
//      the pinned gold "Start game" CTA, and the crown note "You're the host -
//      start whenever your crew's ready" (AC-05). Non-hosts see none of these.
//      Tapping "Start game" calls onStart with the host's family-safe toggle
//      value AND the host's length choice (story-selection/02 AC-02, ONE more
//      parameter on the SAME onStart/startRound seam, not a new hub method) -
//      App wires that to the hub's host-only startRound, which the SERVER
//      enforces + filters by both (AC-03/AC-04, story-selection/02 AC-03).
//    - ALSO host-only (story-selection/06, AC-03): a "Play a favorite" toggle
//      that reveals an INLINE favorites picker (the shared <FavoritesList>
//      from ./Favorites.tsx, reused as-is - no second list implementation) so
//      the host can start a round on one of THEIR favorited templates without
//      leaving the lobby or losing room state. Picking one calls onPlayFavorite
//      with that templateId; App wires it to the SAME startRound seam with the
//      templateId as its 4th argument (the server plays that exact template,
//      still family-safe-gated first, AC-06) - a rejected pick (e.g. the
//      favorite is no longer family-safe under the current toggle) shows a
//      friendly inline message, mirroring RoundComplete's playAgainError.
//
//  Child safety (AC-06): names arrive already vetted by the join-time safety
//  filter (session-engine/02); this screen renders them verbatim and never takes
//  free text of its own, so no unfiltered name is ever shown. The length choice
//  never weakens safety (story-selection/02 AC-05): the family-safe gate still
//  runs first server-side regardless of the length pick.
//
//  Styling: all colors / radii / spacing come from the MUI theme (no hex/px
//  literals here). Animations use transform (scale / box-shadow) - NEVER opacity
//  on a list item - so a re-render mid-animation can never strand a tile
//  invisible (design-pack gotcha).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useEffect, useRef, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, keyframes, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography } from '@mui/material';
import { AppBar, BottomActionBar, BottomActionBarSpacer, FamilySafeToggle, Guardian, StoryLengthChoice } from '../components';
import { toGuardianVariant } from '../components';
import { FAMILY_SAFE_DEFAULT } from '../content/familySafe';
import type { FavoriteEntry } from '../content/favorites';
import type { LengthPreference } from '../content/length';
import type { Player, RoomState, StartRoundResult } from '../signalr/useGameHub';
import { FavoritesList } from './Favorites';

// Room capacity for Slice 1: the roster tops out at six carvers (AC-01). The
// grid fills the remaining seats with dashed "waiting..." slots up to this.
const MAX_PLAYERS = 6;

// How long the "[Name] pulled up a stone" toast stays on screen (AC-02): matches
// the qsToastIn animation duration so it slides out exactly as it is removed.
const TOAST_DURATION_MS = 2600;

// How long the "Copy" button shows its teal-check "Copied!" confirmation
// before reverting (session-engine/04 AC-02) - matches the design spec's ~1.8s.
const COPIED_CONFIRMATION_MS = 1800;

export interface LobbyProps {
  /** The current room (code + live roster). Roster updates flow in via the hook's RosterChanged handler. */
  room: RoomState;
  /** Whether THIS client is the host - gates the Start CTA + host note (AC-05). */
  isHost: boolean;
  /**
   * reveal-delight/03 (AC-04): the nickname wearing the Golden Guardian crown this
   * round (the previous round's funniest-word winner), or null when no crown applies.
   * The matching roster tile's Guardian shows the crown overlay.
   */
  crownedSessionId?: string | null;
  /** Leave the lobby and return Home (the app-bar close action). */
  onLeave: () => void;
  /**
   * Start the game (host only, group-play/01). Called with the host's current
   * family-safe toggle position AND the host's story-length choice
   * (story-selection/02 AC-02 - one more parameter on the SAME seam, not a new
   * hub method); App wires this to the hub's host-only startRound (the SERVER
   * enforces the host check and filters templates by both, authoritative -
   * AC-03/AC-04, story-selection/02 AC-03). Only ever invoked from the
   * host-only Start CTA below.
   */
  onStart: (familySafe: boolean, lengthPref: LengthPreference) => void;
  /**
   * Host-only: start a round on an EXACT favorited template (story-selection/06,
   * AC-03). App wires this to the hub's startRound with the picked template id
   * as its 4th argument - the SERVER plays that exact template (bypassing
   * length + freshness, still family-safe-gated first, AC-04/AC-06). Resolves
   * with the same StartRoundResult envelope as a normal start; on a rejection
   * the inline picker shows the friendly error rather than silently doing
   * nothing (mirroring RoundComplete's playAgainError pattern). On success the
   * server's RoundStarted broadcast routes everyone into the round as usual.
   */
  onPlayFavorite: (templateId: string, familySafe: boolean) => Promise<StartRoundResult>;
  /**
   * Optional notice shown at the top of the lobby - e.g. "a carver left, so the
   * round was reset" when the hub aborts a round mid-collection (group-play
   * recovery). Omitted/null renders nothing.
   */
  notice?: string | null;
  /** Dismiss the notice banner (optional; only wired when a notice can appear). */
  onDismissNotice?: () => void;
}

// A tile scales in from nothing when a new player fills a slot (AC-02). Transform
// ONLY - never opacity on a list item - so a re-render cannot strand it hidden.
const arrive = keyframes`
  0% { transform: scale(0); }
  60% { transform: scale(1.1); }
  100% { transform: scale(1); }
`;

// The host tile's gold presence ring pulses outward (AC-01). box-shadow only, so
// it never touches the tile's own opacity/layout.
const ring = keyframes`
  0%, 100% { box-shadow: 0 0 0 0 var(--qs-ring-from); }
  50% { box-shadow: 0 0 0 6px var(--qs-ring-to); }
`;

// An empty slot's dashed border pulses from warm-stone toward purple (AC-03).
const borderPulse = keyframes`
  0%, 100% { border-color: var(--qs-pulse-from); }
  50% { border-color: var(--qs-pulse-to); }
`;

// The three "waiting" dots fade in sequence (AC-03). Opacity here is fine: these
// dots are decorative chrome inside a static placeholder, NOT list items whose
// presence a re-render toggles.
const dots = keyframes`
  0%, 100% { opacity: .3; }
  50% { opacity: 1; }
`;

// The toast slides up in and back out (AC-02). It is a short-lived element that
// is fully removed after TOAST_DURATION_MS, so its opacity keyframe cannot
// strand anything - it is not a persistent list item.
const toastIn = keyframes`
  0% { transform: translateY(28px); opacity: 0; }
  18% { transform: translateY(0); opacity: 1; }
  82% { transform: translateY(0); opacity: 1; }
  100% { transform: translateY(-8px); opacity: 0; }
`;

/** One present player's tile: Guardian in a carved stone circle, name, role chip. */
function PlayerTile({ player, crowned }: { player: Player; crowned: boolean }) {
  const theme = useTheme();
  // The variant is a free string on the wire; the server normalizes it to one of
  // the six known values, so treat it as a GuardianVariant for the avatar.
  const variant = toGuardianVariant(player.variant);

  return (
    <Stack
      alignItems="center"
      spacing={1}
      sx={{
        // Scale-pop entrance (AC-02) - transform only.
        animation: `${arrive} 0.45s ease both`,
      }}
    >
      <Box
        sx={{
          position: 'relative',
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
        <Guardian variant={variant} size={52} crowned={crowned} />

        {player.isHost && (
          <>
            {/* Pulsing gold presence ring (AC-01) - box-shadow, not opacity. */}
            <Box
              aria-hidden
              sx={{
                position: 'absolute',
                inset: '-2.5px',
                borderRadius: '50%',
                border: `2.5px solid ${theme.palette.gold.main}`,
                pointerEvents: 'none',
                '--qs-ring-from': alpha(theme.palette.gold.main, 0.45),
                '--qs-ring-to': alpha(theme.palette.gold.main, 0),
                animation: `${ring} 2.4s ease-in-out infinite`,
              }}
            />
            {/* Crown badge above the avatar (AC-01). */}
            <Box
              aria-hidden
              sx={{
                position: 'absolute',
                top: -13,
                left: '50%',
                transform: 'translateX(-50%)',
                color: 'gold.main',
                fontSize: 20,
                display: 'flex',
              }}
            >
              <FontAwesomeIcon icon="crown" />
            </Box>
          </>
        )}
      </Box>

      <Box sx={{ textAlign: 'center' }}>
        <Typography
          sx={{
            fontFamily: '"Fredoka", sans-serif',
            fontWeight: 500,
            fontSize: 15,
            lineHeight: 1.1,
            color: 'text.primary',
          }}
        >
          {player.nickname}
        </Typography>
        {player.isHost ? <HostChip /> : <ReadyChip />}
      </Box>
    </Stack>
  );
}

/** Gold "HOST" role chip (AC-01). */
function HostChip() {
  const theme = useTheme();
  return (
    <Box
      component="span"
      sx={{
        display: 'inline-block',
        mt: 1,
        px: 1.25,
        py: 0.25,
        bgcolor: alpha(theme.palette.gold.main, 0.22),
        borderRadius: 999,
        fontFamily: '"Nunito", sans-serif',
        fontSize: 10.5,
        fontWeight: 800,
        letterSpacing: 0.4,
        color: theme.palette.gold.dark,
      }}
    >
      HOST
    </Box>
  );
}

/** Teal "READY" role chip with a filled dot (AC-01). */
function ReadyChip() {
  const theme = useTheme();
  return (
    <Box
      component="span"
      sx={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 0.75,
        mt: 1,
        px: 1.25,
        py: 0.25,
        bgcolor: alpha(theme.palette.teal.main, 0.18),
        borderRadius: 999,
        fontFamily: '"Nunito", sans-serif',
        fontSize: 10.5,
        fontWeight: 800,
        letterSpacing: 0.3,
        color: theme.palette.teal.dark,
      }}
    >
      <Box
        component="span"
        sx={{ width: 5, height: 5, borderRadius: '50%', bgcolor: 'teal.main' }}
      />
      READY
    </Box>
  );
}

/** An unfilled seat: a dashed circle with 3 pulsing dots + "waiting..." (AC-03). */
function EmptySlot() {
  const theme = useTheme();
  return (
    <Stack alignItems="center" spacing={1}>
      <Box
        sx={{
          width: 74,
          height: 74,
          borderRadius: '50%',
          bgcolor: alpha(theme.palette.sandstone.main, 0.35),
          border: '2.5px dashed',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          '--qs-pulse-from': alpha(theme.palette.stoneEdge.main, 0.4),
          '--qs-pulse-to': alpha(theme.palette.primary.main, 0.5),
          animation: `${borderPulse} 2.6s ease-in-out infinite`,
        }}
      >
        <Stack direction="row" spacing={0.5}>
          {[0, 0.2, 0.4].map((delay) => (
            <Box
              key={delay}
              sx={{
                width: 6,
                height: 6,
                borderRadius: '50%',
                bgcolor: alpha(theme.palette.stoneEdge.main, 0.85),
                animation: `${dots} 1.4s ease-in-out ${delay}s infinite`,
              }}
            />
          ))}
        </Stack>
      </Box>
      <Typography
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 700,
          fontSize: 12.5,
          color: 'text.secondary',
        }}
      >
        waiting...
      </Typography>
    </Stack>
  );
}

/**
 * The stone-tablet share widget (session-engine/04, docs/design/Lobby.dc.html):
 * the room code in big purple type plus Copy + Share actions. The code is
 * already local client state (useGameHub's `room.code`) so both actions are
 * pure client-side - no server round-trip.
 */
function ShareWidget({ code }: { code: string }) {
  const theme = useTheme();

  // "Copied!" confirmation (AC-02): local state only, reverts after
  // COPIED_CONFIRMATION_MS. The timer is cleared on unmount so we never call
  // setState after the component is gone.
  const [copied, setCopied] = useState(false);
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (copiedTimer.current) clearTimeout(copiedTimer.current);
    };
  }, []);

  const handleCopy = async () => {
    // Guard clipboard availability gracefully (e.g. insecure context / an
    // older browser) - never throw, just skip the confirmation.
    if (typeof navigator === 'undefined' || !navigator.clipboard) return;
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
      if (copiedTimer.current) clearTimeout(copiedTimer.current);
      copiedTimer.current = setTimeout(() => setCopied(false), COPIED_CONFIRMATION_MS);
    } catch {
      // Clipboard permission denied or unavailable - fail silently, no error surfaced.
    }
  };

  // Feature-detect the Web Share API once (it does not change over the
  // component's lifetime) - AC-04: hide the Share button entirely when it is
  // not available (e.g. desktop Chrome) rather than showing a dead button.
  const [canShare] = useState(
    () => typeof navigator !== 'undefined' && typeof navigator.share === 'function',
  );

  const handleShare = async () => {
    if (!canShare) return;
    try {
      await navigator.share({
        title: 'QuibbleStone',
        text: `Join my QuibbleStone game! Room code: ${code}`,
      });
    } catch {
      // A user-cancelled share (AbortError) or any other rejection should
      // never surface as an unhandled error or noisy console log.
    }
  };

  return (
    <Box
      sx={{
        position: 'relative',
        px: 6,
        py: 5,
        // Fixed px radius, NOT a bare number: MUI's sx `borderRadius` multiplies
        // by theme.shape.borderRadius (20), so 6.5 ballooned to 130px - a pill
        // whose huge corners cut under the full-width label + hint (they read as
        // "outside the bubble"). A literal keeps a proper carved-tablet shape.
        borderRadius: '26px',
        textAlign: 'center',
        // Resolve the theme gradient explicitly: MUI's sx only maps dotted theme
        // paths for color-family props (color/bgcolor/borderColor), NOT the
        // `background` shorthand, so a string 'tablet.gradient' would ship as
        // invalid CSS. Home.tsx uses the same theme.palette.tablet.gradient.
        background: theme.palette.tablet.gradient,
        boxShadow: `0 18px 36px -22px ${alpha(theme.palette.primary.main, 0.5)}, inset 0 2px 0 ${alpha(theme.palette.common.white, 0.5)}, inset 0 -4px 12px ${alpha(theme.palette.stoneEdge.main, 0.35)}`,
        mb: 4,
      }}
    >
      {/* Code hero on its own row so it can breathe, then a full-width action
          row below (design mock stacked the buttons in a cramped right column;
          on a real phone that squeezed two uneven pills - the product owner
          asked to rework this). Copy + Share are equal-width and chunky (big tap
          targets); when Web Share is unavailable, Copy spans the full width. */}
      <Typography
        variant="overline"
        sx={{ display: 'block', fontSize: 11, fontWeight: 800, color: 'text.secondary' }}
      >
        Room code
      </Typography>
      <Typography
        sx={{
          fontFamily: '"Fredoka", sans-serif',
          fontWeight: 700,
          fontSize: 40,
          lineHeight: 1,
          // Spaced out + centered. letter-spacing adds a trailing gap after the
          // last glyph that nudges centered text left; textIndent adds the same
          // gap on the leading side to keep the code optically centered.
          letterSpacing: '12px',
          textIndent: '12px',
          color: 'primary.main',
          mt: 0.5,
        }}
      >
        {code}
      </Typography>

      <Stack direction="row" spacing={1.25} sx={{ mt: 3 }}>
        <Button
          variant="outlined"
          onClick={handleCopy}
          sx={{ flex: 1, height: 48, fontSize: 15, gap: 1 }}
        >
          {copied ? (
            <Box sx={{ color: 'teal.main', display: 'flex' }}>
              <FontAwesomeIcon icon="check" style={{ width: 16, height: 16 }} />
            </Box>
          ) : (
            <FontAwesomeIcon icon="copy" style={{ width: 16, height: 16 }} />
          )}
          {copied ? 'Copied!' : 'Copy'}
        </Button>
        {canShare && (
          <Button
            variant="sharePurple"
            onClick={handleShare}
            sx={{ flex: 1, height: 48, fontSize: 15, gap: 1 }}
          >
            <FontAwesomeIcon icon="share-nodes" style={{ width: 16, height: 16 }} />
            Share
          </Button>
        )}
      </Stack>

      <Stack
        direction="row"
        alignItems="center"
        justifyContent="center"
        spacing={1}
        sx={{
          mt: 3,
          pt: 2,
          borderTop: `1.5px dashed ${alpha(theme.palette.stoneEdge.main, 0.3)}`,
        }}
      >
        <Box sx={{ color: 'coral.main', display: 'flex' }}>
          <FontAwesomeIcon icon="users" style={{ width: 15, height: 15 }} />
        </Box>
        <Typography
          sx={{
            fontFamily: '"Nunito", sans-serif',
            fontWeight: 700,
            fontSize: 12.5,
            color: 'text.secondary',
          }}
        >
          Share this code so friends can gather round the stone
        </Typography>
      </Stack>
    </Box>
  );
}

export function Lobby({
  room,
  isHost,
  crownedSessionId,
  onLeave,
  onStart,
  onPlayFavorite,
  notice,
  onDismissNotice,
}: LobbyProps) {
  const theme = useTheme();
  const players = room.players;
  const emptyCount = Math.max(0, MAX_PLAYERS - players.length);

  // Host-only family-safe toggle (group-play/01). Safe by default (AC-04): seeded
  // from FAMILY_SAFE_DEFAULT rather than a hardcoded true, so the safe-by-default
  // posture is one shared token. Its value is passed to onStart -> the hub's
  // startRound, where the SERVER filters the template catalog by it. Non-hosts
  // never render or hold this (the toggle + CTA are inside the isHost block).
  const [familySafe, setFamilySafe] = useState(FAMILY_SAFE_DEFAULT);

  // Host-only story-length choice (story-selection/02, AC-02/AC-06): placed the
  // SAME way as the family-safe toggle - host-only, defaulting to 'full' so a
  // lobby that never touches this control starts a round identically to before
  // this story existed. Its value travels out through onStart alongside
  // familySafe, as one more parameter on the existing startRound seam.
  const [lengthPref, setLengthPref] = useState<LengthPreference>('full');

  // Host-only "Play a favorite" inline picker (story-selection/06, AC-03):
  // whether the picker is currently expanded, plus a friendly error message
  // when a pick is rejected server-side. Local to the lobby - it never
  // navigates away or touches room/roster state.
  const [showFavoritePicker, setShowFavoritePicker] = useState(false);
  const [favoriteError, setFavoriteError] = useState<string | null>(null);

  const handlePickFavoriteForRound = async (entry: FavoriteEntry) => {
    setFavoriteError(null);
    // Gate the favorite on the host's CURRENT toggle (the one rendered below),
    // NOT any sticky value: a non-family-safe favorite must never be playable in
    // a session the host has visibly set to family-safe (AC-06). The server
    // re-enforces this authoritatively; we send the boolean the host can see.
    const result = await onPlayFavorite(entry.templateId, familySafe);
    // On success the server's RoundStarted broadcast routes everyone into the
    // round (App's real-time effect navigates away from the lobby), so there
    // is nothing further to do here. On a rejection, surface the friendly
    // reason inline rather than leaving the tap a silent no-op.
    if (!result.ok) {
      setFavoriteError(result.error ?? 'Could not start that tale - please try again.');
    }
  };

  // Join toast (AC-02): a short-lived message shown when a NEW name appears in
  // the roster. We diff the previous player-name set against the current one in
  // an effect; the first render seeds the baseline WITHOUT toasting (so the
  // initial roster - which includes yourself - never fires a toast).
  const [toast, setToast] = useState<string | null>(null);
  const seenNames = useRef<Set<string> | null>(null);
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const current = new Set(players.map((p) => p.nickname));

    // First run for this lobby: seed the baseline, do not toast (AC-02).
    if (seenNames.current === null) {
      seenNames.current = current;
      return;
    }

    // Announce the newest arrival that was not present last render. Departures
    // (a shrinking set) simply update the baseline - no toast for leaves.
    const previous = seenNames.current;
    const arrivals = players.filter((p) => !previous.has(p.nickname));
    if (arrivals.length > 0) {
      const newest = arrivals[arrivals.length - 1];
      setToast(`${newest.nickname} pulled up a stone`);
      if (toastTimer.current) clearTimeout(toastTimer.current);
      toastTimer.current = setTimeout(() => setToast(null), TOAST_DURATION_MS);
    }

    seenNames.current = current;
  }, [players]);

  // Clear a pending toast timer on unmount so it never fires after leaving.
  useEffect(() => {
    return () => {
      if (toastTimer.current) clearTimeout(toastTimer.current);
    };
  }, []);

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Waiting room"
        leftAction={{ icon: 'xmark', label: 'Leave room', onClick: onLeave }}
      />

      <Stack sx={{ px: 5.5, pt: 1 }} spacing={0}>
        {/* Group-play recovery notice: shown when a round was reset because a carver
            left mid-collection (the hub's "RoundAborted"). Dismissible; theme-driven. */}
        {notice && (
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 1.25,
              px: 2,
              py: 1.5,
              mb: 3,
              borderRadius: '14px',
              bgcolor: alpha(theme.palette.coral.main, 0.14),
              border: `1.5px solid ${alpha(theme.palette.coral.main, 0.35)}`,
            }}
          >
            <Typography
              sx={{
                flex: 1,
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 13,
                color: 'text.primary',
              }}
            >
              {notice}
            </Typography>
            {onDismissNotice && (
              <Box
                component="button"
                type="button"
                onClick={onDismissNotice}
                aria-label="Dismiss notice"
                sx={{
                  border: 'none',
                  bgcolor: 'transparent',
                  cursor: 'pointer',
                  color: 'text.secondary',
                  display: 'flex',
                  p: 0.5,
                }}
              >
                <FontAwesomeIcon icon="xmark" style={{ width: 16, height: 16 }} />
              </Box>
            )}
          </Box>
        )}

        {/* Stone-tablet share widget (session-engine/04): room code + Copy/Share. */}
        <ShareWidget code={room.code} />

        {/* "Carvers gathered" header with the live teal count chip (AC-01). */}
        <Stack
          direction="row"
          alignItems="center"
          justifyContent="space-between"
          sx={{ mb: 3.5 }}
        >
          <Typography
            variant="h6"
            component="h2"
            sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 18 }}
          >
            Carvers gathered
          </Typography>
          <Box
            component="span"
            sx={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: 0.75,
              px: 1.5,
              py: 0.5,
              bgcolor: alpha(theme.palette.teal.main, 0.16),
              borderRadius: 999,
              fontFamily: '"Nunito", sans-serif',
              fontSize: 13,
              fontWeight: 800,
              color: theme.palette.teal.dark,
            }}
          >
            <FontAwesomeIcon icon="users" style={{ width: 14, height: 14 }} />
            {players.length} of {MAX_PLAYERS}
          </Box>
        </Stack>

        {/* The roster grid: present players then dashed waiting-slots (AC-01/03). */}
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: 'repeat(3, 1fr)',
            columnGap: 1,
            rowGap: 2.5,
            alignContent: 'start',
          }}
        >
          {players.map((player) => (
            // ConnectionId is not on the wire (no PII), so a present player is
            // keyed by nickname - unique within a room (enforced at join, AC-06).
            <PlayerTile
              key={player.nickname}
              player={player}
              crowned={!!crownedSessionId && player.nickname === crownedSessionId}
            />
          ))}
          {Array.from({ length: emptyCount }, (_, index) => (
            <EmptySlot key={`empty-${index}`} />
          ))}
        </Box>

        {/* Host-only "Play a favorite" inline picker (story-selection/06,
            AC-03): reveals the SAME <FavoritesList> the standalone Favorites
            screen uses (no second list implementation), so the host can start
            a round on one of their own favorited templates without leaving
            the lobby. Non-hosts never see this panel. */}
        {isHost && (
          <Box sx={{ mt: 4 }}>
            <Button
              variant="outlined"
              fullWidth
              onClick={() => {
                // Clear any stale rejection so it does not linger on reopen (S-001).
                setFavoriteError(null);
                setShowFavoritePicker((expanded) => !expanded);
              }}
              startIcon={<FontAwesomeIcon icon="star" style={{ width: 18, height: 18 }} />}
            >
              {showFavoritePicker ? 'Hide favorites' : 'Play a favorite'}
            </Button>
            {showFavoritePicker && (
              <Box sx={{ mt: 2.5 }}>
                {favoriteError && (
                  <Typography
                    sx={{
                      fontFamily: '"Nunito", sans-serif',
                      fontWeight: 700,
                      fontSize: 12.5,
                      color: 'coral.main',
                      textAlign: 'center',
                      mb: 1.5,
                    }}
                  >
                    {favoriteError}
                  </Typography>
                )}
                <FavoritesList onPick={(entry) => void handlePickFavoriteForRound(entry)} />
              </Box>
            )}
          </Box>
        )}

        {isHost && <BottomActionBarSpacer />}
      </Stack>

      {/* Join toast (AC-02): transient, removed after TOAST_DURATION_MS. */}
      {toast && (
        <Box
          aria-live="polite"
          sx={{
            position: 'fixed',
            left: 0,
            right: 0,
            bottom: isHost ? 168 : 40,
            display: 'flex',
            justifyContent: 'center',
            pointerEvents: 'none',
            zIndex: (t) => t.zIndex.snackbar,
          }}
        >
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 1.25,
              px: 2.25,
              py: 1.25,
              bgcolor: 'text.primary',
              borderRadius: 999,
              boxShadow: `0 14px 28px -10px ${alpha(theme.palette.text.primary, 0.7)}`,
              animation: `${toastIn} ${TOAST_DURATION_MS}ms ease both`,
            }}
          >
            <Box
              component="span"
              sx={{
                width: 9,
                height: 9,
                borderRadius: '50%',
                bgcolor: 'teal.main',
                boxShadow: `0 0 8px ${theme.palette.teal.main}`,
              }}
            />
            <Typography
              sx={{
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 800,
                fontSize: 14,
                color: 'parchment.mid',
              }}
            >
              {toast}
            </Typography>
          </Box>
        </Box>
      )}

      {/* Host-only family-safe toggle + story-length choice + Start CTA + crown
          note (AC-05, AC-04, story-selection/02 AC-02). Non-hosts see none of
          these. Both controls sit directly above the CTA so the host sets the
          family-safe + length posture right where they start; their values are
          handed to onStart -> the hub's startRound (server-authoritative). */}
      {isHost && (
        <BottomActionBar>
          <FamilySafeToggle checked={familySafe} onChange={setFamilySafe} />
          <StoryLengthChoice value={lengthPref} onChange={setLengthPref} />
          <Button variant="contained" fullWidth onClick={() => onStart(familySafe, lengthPref)}>
            <FontAwesomeIcon icon="play" style={{ width: 22, height: 22 }} />
            Start game
          </Button>
          <Stack direction="row" alignItems="center" justifyContent="center" spacing={0.75}>
            <Box sx={{ color: 'gold.main', fontSize: 14, display: 'flex' }}>
              <FontAwesomeIcon icon="crown" />
            </Box>
            <Typography
              sx={{
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 12.5,
                color: 'text.secondary',
              }}
            >
              You're the host - start whenever your crew's ready
            </Typography>
          </Stack>
        </BottomActionBar>
      )}
    </Box>
  );
}
