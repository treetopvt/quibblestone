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
//  session-engine/11: the trailing "+ invite" roster slot (`InviteSlot`, below)
//  is no longer decorative - tapping it triggers the SAME invite action as the
//  stone-tablet `ShareWidget`'s own Copy/Share buttons, via the shared
//  `useRoomInvite` hook (./useRoomInvite.ts). Any player may tap it (not
//  host-gated - the room code is already visible to everyone here).
//
//  FIT-TO-VIEWPORT REDESIGN (screen de-clutter, 2026-07): the previous layout
//  stacked the family-safe toggle, story-length choice, and mode picker inline
//  in a scrollable area, and drew all MAX_PLAYERS seats as a grid (present
//  tiles plus empty "waiting..." placeholders). On a real phone that produced a
//  long page scroll before the Start CTA was even visible. This version fits
//  ONE phone viewport (~390x844) with NO page scroll:
//    - The root is a FIXED-HEIGHT flex column (`height: 100dvh`,
//      `overflow: hidden`) - AppBar pinned at top, a `flex: 1; minHeight: 0`
//      middle region holding the code card + roster + settings row, and the
//      host's <BottomActionBar> pinned at the bottom. The page itself can never
//      scroll; only the middle region may, and in practice it does not need to
//      once the tall host controls are moved out (below).
//    - The roster no longer pre-draws empty seats: it is a SINGLE horizontal
//      row of compact avatars for players actually PRESENT, plus one dashed
//      "+ invite" slot, with its OWN horizontal scroll (`overflowX: auto`) if
//      more players join than fit on screen - the page still never scrolls.
//    - The host's round-setup controls (family-safe toggle, story-length
//      choice, mode picker) and the "Play a favorite" panel MOVED into a
//      slide-up bottom sheet (<GameSettingsSheet>, ../components), opened by
//      tapping a single collapsed "Game settings" row that summarizes the
//      CURRENT values (e.g. "Full tale - Classic - Family-safe on"). This is
//      the key fix: the tall controls still have all the room they need, just
//      inside a sheet that only opens on demand instead of always being laid
//      out inline. Lobby still owns every bit of that state (familySafe,
//      lengthPref, mode, showFavoritePicker, favoriteError) - the sheet is
//      pure chrome around the SAME existing controls, unchanged.
//
//  What it shows (design: docs/design/Lobby.dc.html, docs/design/README.md
//  "Screens" screen 3, as adapted by the fit-to-viewport redesign above):
//    - The stone-tablet share widget (session-engine/04): a centered ROOM CODE
//      label, the room code as the hero in big purple Fredoka type, then an
//      outlined-purple "Copy" button (flips to a teal-check "Copied!" for ~1.8s
//      on tap, no server round-trip - the code is already local client state)
//      and a filled-purple "Share" button side by side (Web Share, hidden when
//      unavailable - AC-04), with the "share this code so friends can gather
//      round the stone" helper line at the foot. This is the original centered
//      card layout, kept per product-owner preference (an earlier pass tried a
//      compact left/right row without the helper; the centered look was
//      preferred and still fits now that round setup lives in the sheet).
//    - "Carvers gathered" with a live teal count chip "{n} of {MAX_PLAYERS}" (AC-01).
//    - A single horizontal row of compact (~60px) avatar circles for players
//      PRESENT ONLY - the empty-seat grid from the original design is gone (it
//      previously drew up to MAX_PLAYERS placeholders whether or not anyone
//      would ever fill them). Each present player is a carved stone tile
//      (theme rosterTile tokens) with their Guardian, name, and a role chip:
//      host = gold "HOST" chip + crown badge + a pulsing gold RING; everyone
//      else = teal "READY" chip (AC-01). New tiles scale-pop in (AC-02).
//      session-engine/10 (AC-04): a seat held through a disconnect grace window
//      (`player.connected === false`, mirroring the hub's `PlayerDto.Connected`
//      from session-engine/07) renders instead as a DIMMED tile with a pulsing
//      DASHED ring (reusing the SAME `borderPulse` keyframe the invite slot
//      uses - not a new visual system) and a muted "reconnecting..." chip in
//      place of READY/HOST, so the room understands why the round is paused on
//      them rather than the tile silently vanishing and reappearing. One
//      dashed "+ invite" slot always trails the row. If more players join than
//      fit on screen, the ROW scrolls horizontally on its own axis - the page
//      itself never scrolls (AC-03 preserved via the invite slot's identical
//      dashed/pulsing affordance, just singular now instead of one-per-empty-seat).
//    - A transient dark bottom-center toast "[Name] pulled up a stone" when a new
//      player appears (not on the initial roster, not for yourself) (AC-02).
//    - ONLY when this client is the host: a collapsed "Game settings" row
//      (tappable, summarizing the current family-safe / length / mode values)
//      that opens <GameSettingsSheet>, a slide-up bottom sheet holding - in
//      order - the host-only family-safe toggle (group-play/01), the host-only
//      story-length choice (story-selection/02), the host-only mode picker
//      (group-play/05, the shared ModePicker over GROUP_MODES), and the
//      host-only "Play a favorite" panel (story-selection/06, AC-03, moved
//      into the sheet from its old inline spot below the mode picker). Only
//      the pinned gold "Start game" CTA (whose tap carries the chosen mode id)
//      plus the crown note "You're the host - start whenever your crew's
//      ready" (AC-05) stay in the main flow, just above the pinned bar. Non-
//      hosts see none of these - the settings row itself is host-only, exactly
//      as the inline controls were before this redesign.
//      Tapping "Start game" calls onStart with the host's family-safe toggle
//      value, length choice, AND chosen mode id (story-selection/02 AC-02 +
//      group-play/05, more parameters on the SAME onStart/startRound seam, not new
//      hub methods) -
//      App wires that to the hub's host-only startRound, which the SERVER
//      enforces + filters by both (AC-03/AC-04, story-selection/02 AC-03).
//    - ALSO host-only, now living INSIDE the settings sheet (story-selection/06,
//      AC-03): a "Play a favorite" toggle that reveals an INLINE favorites
//      picker (the shared <FavoritesList> from ./Favorites.tsx, reused as-is -
//      no second list implementation) so the host can start a round on one of
//      THEIR favorited templates without leaving the lobby or losing room
//      state. Picking one calls onPlayFavorite with that templateId; App wires
//      it to the SAME startRound seam with the templateId as its 4th argument
//      (the server plays that exact template, still family-safe-gated first,
//      AC-06) - a rejected pick (e.g. the favorite is no longer family-safe
//      under the current toggle) shows a friendly inline message, mirroring
//      RoundComplete's playAgainError.
//
//  replay-remix/03 ("Pass the chisel", AC-01/AC-02/AC-04/AC-05): the Lobby is
//  always "between rounds" (there is no live round here), so a host-only "Pass"
//  pill (PassHostPill, below) appears on every OTHER present player's tile
//  whenever this client is host - tapping it calls onPassHost with that tile's
//  nickname. The SERVER re-enforces the host check + the between-rounds phase
//  gate authoritatively (AC-04/AC-05); a rejection (a rare race) surfaces as a
//  transient inline message. On success the reused "RosterChanged" broadcast
//  moves the crown for everyone live (AC-02/AC-03) - this screen adds no new
//  broadcast handling of its own.
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
import {
  AppBar,
  BottomActionBar,
  BottomActionBarSpacer,
  FamilySafeToggle,
  GameSettingsSheet,
  Guardian,
  StoryLengthChoice,
} from '../components';
import { toGuardianVariant } from '../components';
import { FAMILY_SAFE_DEFAULT } from '../content/familySafe';
import type { FavoriteEntry } from '../content/favorites';
import type { LengthPreference } from '../content/length';
import { seedLibrary } from '../content/seedLibrary';
import type { Player, RoomState, StartRoundResult } from '../signalr/useGameHub';
import { FavoritesList } from './Favorites';
import { ModePicker } from './ModePicker';
import { DEFAULT_GROUP_MODE, GROUP_MODES, type GameMode } from './modeRegistry';
import { useRoomInvite } from './useRoomInvite';

// Room capacity for Slice 1: the roster tops out at six carvers (AC-01). The
// live count chip still reads "{n} of {MAX_PLAYERS}" even though the redesign
// no longer pre-draws the remaining empty seats as placeholder tiles.
const MAX_PLAYERS = 6;

// How long the "[Name] pulled up a stone" toast stays on screen (AC-02): matches
// the qsToastIn animation duration so it slides out exactly as it is removed.
const TOAST_DURATION_MS = 2600;

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
  crownedNickname?: string | null;
  /** Leave the lobby and return Home (the app-bar close action). */
  onLeave: () => void;
  /**
   * Start the game (host only, group-play/01). Called with the host's current
   * family-safe toggle position, the host's story-length choice
   * (story-selection/02 AC-02), AND the host's chosen mode id (group-play/05
   * AC-01/AC-02) - all on the SAME seam, not new hub methods; App wires this to
   * the hub's host-only startRound (the SERVER enforces the host check, validates
   * the mode against the offered set, and filters templates by family-safe then
   * the mode's eligibility, authoritative - AC-03/AC-04/AC-06, story-selection/02
   * AC-03, group-play/05 AC-02). Only ever invoked from the host-only Start CTA below.
   */
  onStart: (familySafe: boolean, lengthPref: LengthPreference, modeId: string) => void;
  /**
   * Host-only: start a round on an EXACT favorited template (story-selection/06,
   * AC-03). App wires this to the hub's startRound with the picked template id -
   * the SERVER plays that exact template (bypassing length + freshness, still
   * family-safe-gated first, AC-04/AC-06) in the host's CURRENTLY selected mode
   * (group-play/05): the server enforces per-mode eligibility for explicit picks,
   * so a favorite that is not eligible for the mode (e.g. a bank-less tale under
   * Word Bank) is rejected with the friendly error the inline picker shows, rather
   * than silently downgrading to Classic Blind. Resolves with the same
   * StartRoundResult envelope as a normal start; on success the server's
   * RoundStarted broadcast routes everyone into the round as usual.
   */
  onPlayFavorite: (templateId: string, familySafe: boolean, modeId: string) => Promise<StartRoundResult>;
  /**
   * replay-remix/03 (AC-01/AC-02): host-only "Pass the chisel" - hand the host
   * role to another present roster player, by nickname. The Lobby is always
   * "between rounds" (there is no live round here), so the action shows on
   * every OTHER present player's tile whenever THIS client is host (AC-01);
   * App wires this to the hub's host-only passHost invoke - the SERVER
   * re-enforces the host check (AC-04). Omitted renders no handoff affordance
   * (defensive default - e.g. a screen reused before this story wires it up).
   */
  onPassHost?: (targetNickname: string) => Promise<{ ok: boolean; error: string | null }>;
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

/**
 * One present player's tile: Guardian in a carved stone circle, name, role chip.
 * Sized compactly (~60px) for the redesigned single-row layout (fit-to-viewport,
 * 2026-07) - previously 74px in the 3-column grid, shrunk so a full row of
 * carvers plus the trailing invite slot reads comfortably on one line without
 * forcing the page to scroll.
 * session-engine/10 (AC-04): `player.connected === false` (a seat held through
 * a disconnect grace window) swaps the solid border + boxShadow for a dimmed,
 * pulsing DASHED ring - reusing the trailing invite slot's `borderPulse`
 * keyframe below, the SAME "held seat" language already established on this
 * screen rather than a new visual system - and the role chip for a muted
 * "reconnecting..." one. The host's gold presence ring is suppressed while
 * disconnected (the dashed pulse already signals motion; two competing rings
 * read as noisy) but the crown badge stays - identity ("who this seat belongs
 * to") persists, only presence dims.
 *
 * replay-remix/03 (AC-01): `onPassHost`, when supplied, renders a small "Pass"
 * pill (reusing the app's carving/chisel glyph, faHammer - already registered
 * for the Waiting screen's progress row and RoundBadge's "ROUND N CARVED" - not
 * a new icon) beneath the name so the HOST can hand this OTHER player the role
 * with one tap. The caller (Lobby) only ever supplies this to tiles that are
 * NOT the current host's own.
 */
function PlayerTile({
  player,
  crowned,
  onPassHost,
}: {
  player: Player;
  crowned: boolean;
  /** replay-remix/03 (AC-01): pass the host role to THIS tile's player. Present only for host-viewable, non-host tiles. */
  onPassHost?: () => void;
}) {
  const theme = useTheme();
  // The variant is a free string on the wire; the server normalizes it to one of
  // the six known values, so treat it as a GuardianVariant for the avatar.
  const variant = toGuardianVariant(player.variant);
  const connected = player.connected;

  return (
    <Stack
      alignItems="center"
      spacing={0.75}
      sx={{
        flexShrink: 0,
        // Scale-pop entrance (AC-02) - transform only.
        animation: `${arrive} 0.45s ease both`,
      }}
    >
      <Box
        sx={{
          position: 'relative',
          width: 60,
          height: 60,
          borderRadius: '50%',
          bgcolor: 'rosterTile.fill',
          // A steady dimmed opacity while disconnected (not an entrance/exit
          // animation on a list item - the Waiting screen's own done/writing
          // dim uses the same static-opacity posture, Waiting.tsx).
          opacity: connected ? 1 : 0.55,
          boxShadow: connected
            ? `0 8px 16px -10px ${alpha(theme.palette.stoneEdge.main, 0.7)}`
            : 'none',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          ...(connected
            ? { border: `2.5px solid ${theme.palette.rosterTile.border}` }
            : {
                border: '2.5px dashed',
                '--qs-pulse-from': alpha(theme.palette.stoneEdge.main, 0.4),
                '--qs-pulse-to': alpha(theme.palette.primary.main, 0.5),
                animation: `${borderPulse} 2.6s ease-in-out infinite`,
              }),
        }}
      >
        <Guardian variant={variant} size={42} crowned={crowned} />

        {player.isHost && connected && (
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
          </>
        )}
        {player.isHost && (
          // Crown badge above the avatar (AC-01) - persists through a
          // disconnect: identity stays, only the presence ring dims (AC-04).
          <Box
            aria-hidden
            sx={{
              position: 'absolute',
              top: -11,
              left: '50%',
              transform: 'translateX(-50%)',
              color: 'gold.main',
              fontSize: 16,
              display: 'flex',
              opacity: connected ? 1 : 0.55,
            }}
          >
            <FontAwesomeIcon icon="crown" />
          </Box>
        )}
      </Box>

      <Typography
        noWrap
        sx={{
          fontFamily: '"Fredoka", sans-serif',
          fontWeight: 500,
          fontSize: 12.5,
          lineHeight: 1.1,
          color: 'text.primary',
          maxWidth: 68,
          textAlign: 'center',
        }}
      >
        {player.nickname}
      </Typography>
      {/* HOST/READY are now conveyed by the gold ring + crown badge above (the
          compact 60px tile has no room for a role chip AND a name on separate
          lines without the row growing tall enough to threaten the one-
          viewport budget). The "reconnecting..." state is the one exception
          (session-engine/10, AC-04/AC-06): a held seat's dashed pulse alone
          could read as "still empty", so the plain-language chip stays to
          explain WHY the round is paused on this seat. */}
      {!connected && <ReconnectingChip />}
      {/* replay-remix/03 (AC-01): host-only "Pass the chisel" - a small tappable
          pill so the host can hand this OTHER player the role with one tap. The
          caller only supplies onPassHost for a non-host tile when THIS client is
          host, so no extra gating is needed here. */}
      {onPassHost && <PassHostPill onPassHost={onPassHost} nickname={player.nickname} />}
    </Stack>
  );
}

/**
 * replay-remix/03 (AC-01): the "Pass the chisel" pill - a small, tappable,
 * host-only affordance rendered beneath a roster tile's name, reusing the
 * app's carving/chisel glyph (faHammer, already registered for the Waiting
 * screen's progress row and RoundBadge). Kept compact to match the tile's own
 * ~60px footprint (the reconnecting chip beside it uses the same scale).
 */
function PassHostPill({ onPassHost, nickname }: { onPassHost: () => void; nickname: string }) {
  const theme = useTheme();
  return (
    <Box
      component="button"
      type="button"
      onClick={onPassHost}
      aria-label={`Pass the chisel to ${nickname}`}
      sx={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 0.5,
        mt: 0.75,
        px: 1.25,
        py: 0.5,
        border: 'none',
        borderRadius: 999,
        bgcolor: alpha(theme.palette.primary.main, 0.14),
        color: theme.palette.primary.main,
        fontFamily: '"Nunito", sans-serif',
        fontWeight: 800,
        fontSize: 10.5,
        cursor: 'pointer',
        '&:focus-visible': { outline: `2px solid ${theme.palette.primary.main}`, outlineOffset: 2 },
      }}
    >
      <FontAwesomeIcon icon="hammer" style={{ width: 10, height: 10 }} />
      Pass
    </Box>
  );
}

/**
 * Muted "reconnecting..." chip (session-engine/10, AC-04, AC-06): a calm,
 * plain-language stand-in for READY/HOST while a seat's `connected` flag is
 * false. Reuses the SAME pulsing-dots keyframe (`dots`, below) EmptySlot's
 * "waiting..." caption already uses, rather than a new affordance - and the
 * SAME muted stone tone, so it visually reads as "this seat's dashed pulse
 * above" rather than a competing alarm color (never coral/red - AC-06, this is
 * "hang tight," not an error).
 */
function ReconnectingChip() {
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
        bgcolor: alpha(theme.palette.stoneEdge.main, 0.22),
        borderRadius: 999,
        fontFamily: '"Nunito", sans-serif',
        fontSize: 10.5,
        fontWeight: 800,
        letterSpacing: 0.3,
        color: 'text.secondary',
      }}
    >
      <Box
        component="span"
        sx={{
          width: 5,
          height: 5,
          borderRadius: '50%',
          bgcolor: alpha(theme.palette.stoneEdge.main, 0.85),
          animation: `${dots} 1.4s ease-in-out infinite`,
        }}
      />
      reconnecting...
    </Box>
  );
}

/**
 * The trailing "+ invite" slot (session-engine/11: wired to the SAME invite
 * action `ShareWidget`'s Copy/Share buttons trigger, via the shared
 * `useRoomInvite` hook - see ./useRoomInvite.ts). Previously (design-system/05)
 * this was a purely decorative dashed circle with no `onClick`: the roster grid
 * used to pre-draw one dashed "waiting..." placeholder PER remaining seat (up
 * to MAX_PLAYERS), and the fit-to-viewport redesign collapsed that down to
 * exactly ONE trailing slot, but left it inert. Now tapping it DOES the thing
 * its label promises (AC-01/AC-02): Share-first when the Web Share API is
 * available (the single highest-value tap for a big "+ invite" affordance),
 * falling back to Copy-plus-a-brief-local-confirmation otherwise - mirroring
 * the widget's own Share-first, Copy-fallback posture rather than a third UX
 * pattern. AC-03: this slot owns NO copy/share logic of its own - it only
 * calls the one shared hook, so there is exactly one invite code path in the
 * codebase.
 *
 * Not host-gated (AC-04): every player in the room already sees the room code
 * on this same screen, so there is nothing host-only being exposed here - any
 * player may tap invite.
 *
 * A real `<button type="button">` (not the old non-interactive `Stack`/`Box`)
 * so it gets proper button semantics: keyboard-focusable, a visible
 * `:focus-visible` outline, and an `aria-label` (AC-05) - matching the pattern
 * already used by the "Game settings" row below in this same file. The
 * dashed-circle + "+" glyph + "invite" caption visual (design-system/05) is
 * unchanged; only behavior was added.
 */
function InviteSlot({ code }: { code: string }) {
  const theme = useTheme();
  const { canShare, copied, copy, share } = useRoomInvite(code);

  const handleTap = () => {
    // Share-first when available (AC-01), Copy-fallback otherwise (AC-02) -
    // the SAME behavior split as ShareWidget's two buttons, just collapsed
    // onto this slot's single tap target.
    if (canShare) {
      void share();
    } else {
      void copy();
    }
  };

  return (
    <Stack alignItems="center" spacing={0.75} sx={{ flexShrink: 0 }}>
      <Box
        component="button"
        type="button"
        onClick={handleTap}
        aria-label="Invite another player"
        sx={{
          width: 60,
          height: 60,
          borderRadius: '50%',
          bgcolor: alpha(theme.palette.sandstone.main, 0.35),
          border: '2.5px dashed',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: theme.palette.stoneEdge.main,
          cursor: 'pointer',
          p: 0,
          fontFamily: 'inherit',
          '--qs-pulse-from': alpha(theme.palette.stoneEdge.main, 0.4),
          '--qs-pulse-to': alpha(theme.palette.primary.main, 0.5),
          animation: `${borderPulse} 2.6s ease-in-out infinite`,
          '&:focus-visible': { outline: `2px solid ${theme.palette.primary.main}`, outlineOffset: 2 },
        }}
      >
        {/* A light local confirmation (a brief check-glyph swap) when Share is
            unavailable and the fallback Copy just ran - this slot is a small
            circle, so a check icon reads better here than swapping the caption
            text, but it mirrors the SAME `copied` timer ShareWidget's own
            "Copied!" confirmation uses (AC-02). */}
        {copied ? (
          <FontAwesomeIcon icon="check" style={{ width: 18, height: 18, color: theme.palette.teal.main }} />
        ) : (
          <FontAwesomeIcon icon="plus" style={{ width: 18, height: 18 }} />
        )}
      </Box>
      <Typography
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 700,
          fontSize: 11.5,
          color: 'text.secondary',
        }}
      >
        {copied ? 'copied!' : 'invite'}
      </Typography>
    </Stack>
  );
}

/**
 * The stone-tablet share widget (session-engine/04, docs/design/Lobby.dc.html):
 * the room code in big purple type plus Copy + Share actions. The code is
 * already local client state (useGameHub's `room.code`) so both actions are
 * pure client-side - no server round-trip.
 *
 * session-engine/06: both actions now carry a tappable `/join/:code` deep
 * link (built by ./joinLink.ts) instead of the bare code, so a recipient on
 * another device lands straight on the Join screen with the code pre-filled
 * (AC-01, AC-04). The on-screen carved-stone code display below is unchanged -
 * only the Copy/Share payloads change.
 *
 * Layout (product-owner preference, 2026-07): the original stacked-and-centered
 * card - a centered ROOM CODE label, the big code as the hero on its own line,
 * Copy + Share side by side below, and the "share this code so friends can
 * gather round the stone" helper line at the foot. (An earlier fit-to-viewport
 * pass tried a compact left/right row without the helper line; the product owner
 * preferred this original look, and it still fits the one-viewport budget now
 * that the round-setup controls live in the settings sheet.) All Copy/Share
 * behavior (the deep-link payload, the "Copied!" confirmation timer, the Web
 * Share feature-detect) is unchanged.
 *
 * session-engine/11: the Copy/Share closures, the `joinLink` build, the
 * `canShare` feature-detect, and the `copied` confirmation timer all now live
 * in the shared `useRoomInvite` hook (./useRoomInvite.ts) - this widget and the
 * roster's "+ invite" slot (`InviteSlot`, above) both call it, so there is
 * exactly one invite code path (AC-03). Nothing about the widget's OWN
 * behavior changed: same payload, same ~1.8s confirmation, same hidden-when-
 * unavailable Share button.
 */
function ShareWidget({ code }: { code: string }) {
  const theme = useTheme();
  const { canShare, copied, copy, share } = useRoomInvite(code);

  return (
    <Box
      sx={{
        position: 'relative',
        px: 6,
        py: 4.5,
        // Fixed px radius, NOT a bare number: MUI's sx `borderRadius` multiplies
        // by theme.shape.borderRadius (20), which would corrupt the carved-tablet
        // shape. A literal keeps proper corners.
        borderRadius: '26px',
        textAlign: 'center',
        // Resolve the theme gradient explicitly: MUI's sx only maps dotted theme
        // paths for color-family props (color/bgcolor/borderColor), NOT the
        // `background` shorthand, so a string 'tablet.gradient' would ship as
        // invalid CSS. Home.tsx uses the same theme.palette.tablet.gradient.
        background: theme.palette.tablet.gradient,
        boxShadow: `0 18px 36px -22px ${alpha(theme.palette.primary.main, 0.5)}, inset 0 2px 0 ${alpha(theme.palette.common.white, 0.5)}, inset 0 -4px 12px ${alpha(theme.palette.stoneEdge.main, 0.35)}`,
      }}
    >
      {/* Centered ROOM CODE label + big code (the product-owner-preferred original
          layout): the code reads as the hero, centered on its own line, with the
          actions below and the "share this code" helper line at the foot. */}
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

      {/* Copy + Share side by side (chunky, equal-width). Share is feature-
          detected and hidden entirely when unavailable, so Copy spans the full
          width then (AC-04). */}
      <Stack direction="row" spacing={1.25} sx={{ mt: 3 }}>
        <Button
          variant="outlined"
          onClick={() => void copy()}
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
            onClick={() => void share()}
            sx={{ flex: 1, height: 48, fontSize: 15, gap: 1 }}
          >
            <FontAwesomeIcon icon="share-nodes" style={{ width: 16, height: 16 }} />
            Share
          </Button>
        )}
      </Stack>

      {/* Helper line (product-owner-preferred): a coral people glyph + the
          "gather round the stone" invitation, set off by a dashed rule. */}
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
  crownedNickname,
  onLeave,
  onStart,
  onPlayFavorite,
  onPassHost,
  notice,
  onDismissNotice,
}: LobbyProps) {
  const theme = useTheme();
  const players = room.players;

  // replay-remix/03: a friendly, transient message when a "Pass the chisel"
  // attempt is rejected server-side (a race with the room dropping out of
  // "between rounds", or the target having just left). Mirrors the join
  // toast's own transient-timer pattern below.
  const [passHostError, setPassHostError] = useState<string | null>(null);
  const passHostErrorTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handlePassHost = async (targetNickname: string) => {
    if (!onPassHost) return;
    setPassHostError(null);
    const result = await onPassHost(targetNickname);
    if (!result.ok) {
      setPassHostError(result.error ?? "Couldn't pass the chisel - please try again.");
      if (passHostErrorTimer.current) clearTimeout(passHostErrorTimer.current);
      passHostErrorTimer.current = setTimeout(() => setPassHostError(null), TOAST_DURATION_MS);
    }
  };

  // Host-only "Game settings" bottom sheet (fit-to-viewport redesign): whether
  // the sheet holding the family-safe toggle / length choice / mode picker /
  // favorites panel is open. Purely local UI state - it never affects what
  // gets sent to onStart, only whether those controls are currently visible.
  const [settingsSheetOpen, setSettingsSheetOpen] = useState(false);

  // Host-only family-safe toggle (group-play/01). Safe by default (AC-04): seeded
  // from FAMILY_SAFE_DEFAULT rather than a hardcoded true, so the safe-by-default
  // posture is one shared token. Its value is passed to onStart -> the hub's
  // startRound, where the SERVER filters the template catalog by it. The hook
  // runs for every client, but non-hosts never render or use this value (the
  // toggle + CTA are inside the isHost block), so it stays inert for them.
  const [familySafe, setFamilySafe] = useState(FAMILY_SAFE_DEFAULT);

  // Host-only mode choice (group-play/05, AC-01): the host picks the mode for the
  // WHOLE room from the shared GROUP_MODES (Classic Blind, Word Bank, Progressive
  // Reveal - Progressive Story deferred, AC-04/AC-05). Defaults to Classic Blind so
  // a lobby that never touches the picker starts a round exactly as before this
  // story. Its id travels out through onStart alongside familySafe + lengthPref.
  const [mode, setMode] = useState<GameMode>(DEFAULT_GROUP_MODE);

  // Keep the picker on a startable mode when the family-safe toggle flips: if the
  // current mode has no eligible template under the new position (e.g. Word Bank
  // when no family-safe template carries a bank), fall back to Classic Blind
  // (always eligible), mirroring Solo's handleFamilySafeChange (AC-04).
  const handleFamilySafeChange = (checked: boolean) => {
    setFamilySafe(checked);
    if (mode.eligibleTemplates(seedLibrary, checked).length === 0) {
      setMode(DEFAULT_GROUP_MODE);
    }
  };

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
    // Gate the favorite on the host's CURRENT toggle + selected mode (the ones
    // rendered on this screen), NOT any sticky value: a non-family-safe favorite
    // must never be playable in a session visibly set to family-safe (AC-06), and
    // the favorite plays in the mode the host picked (group-play/05). The server
    // re-enforces both authoritatively; we send what the host can see.
    const result = await onPlayFavorite(entry.templateId, familySafe, mode.config.id);
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

  // Clear pending toast / pass-host-error timers on unmount so neither fires
  // after leaving.
  useEffect(() => {
    return () => {
      if (toastTimer.current) clearTimeout(toastTimer.current);
      if (passHostErrorTimer.current) clearTimeout(passHostErrorTimer.current);
    };
  }, []);

  // The collapsed settings row's summary subtitle (fit-to-viewport redesign):
  // reflects the host's CURRENT values so the row is informative even closed,
  // e.g. "Full tale - Classic - Family-safe on". Recomputed on every render
  // (cheap string join) rather than memoized - these three values change
  // rarely and only via direct user taps.
  const lengthLabel = lengthPref === 'quick' ? 'Quick tale' : 'Full tale';
  const settingsSummary = `${lengthLabel} - ${mode.config.label} - Family-safe ${familySafe ? 'on' : 'off'}`;

  return (
    <Box
      sx={{
        position: 'relative',
        height: '100dvh',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        maxWidth: 430,
        mx: 'auto',
      }}
    >
      <AppBar
        title="Waiting room"
        leftAction={{ icon: 'xmark', label: 'Leave room', onClick: onLeave }}
      />

      {/* The one region that may ever scroll (and in practice should not need
          to, on the target ~390x844 viewport) - everything tall (the round-
          setup controls, the favorites panel) lives in the settings sheet
          instead, per the file header. */}
      <Stack sx={{ px: 5.5, pt: 1, flex: 1, minHeight: 0, overflowY: 'auto' }} spacing={3}>
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
        <Box>
          <Stack
            direction="row"
            alignItems="center"
            justifyContent="space-between"
            sx={{ mb: 2 }}
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

          {/* A single horizontal row of present-player avatars + one trailing
              "+ invite" slot (AC-01/03, redesigned - see the file header for
              why the old per-empty-seat grid is gone). The row scrolls on its
              OWN horizontal axis if more players join than fit - the page
              itself never scrolls. */}
          <Stack
            direction="row"
            spacing={2}
            sx={{
              overflowX: 'auto',
              overflowY: 'hidden',
              pb: 0.5,
              // A little breathing room so the host crown/ring never clips
              // against the scroll container's edge.
              pt: 0.5,
            }}
          >
            {players.map((player) => (
              // ConnectionId is not on the wire (no PII), so a present player is
              // keyed by nickname - unique within a room (enforced at join, AC-06).
              <PlayerTile
                key={player.nickname}
                player={player}
                crowned={!!crownedNickname && player.nickname === crownedNickname}
                // replay-remix/03 (AC-01/AC-04/AC-05): the Lobby is always
                // "between rounds", so the only gate here is "I am host" and
                // "this tile is not my own" - the SERVER re-enforces both the
                // host check and the phase gate authoritatively either way.
                onPassHost={
                  isHost && !player.isHost && onPassHost
                    ? () => void handlePassHost(player.nickname)
                    : undefined
                }
              />
            ))}
            <InviteSlot code={room.code} />
          </Stack>
          {/* replay-remix/03: a transient, friendly rejection message (mirrors
              favoriteError's inline posture below) - most taps succeed instantly
              via the reused RosterChanged broadcast, so this only shows on the
              rare server-side reject (a race with the phase gate, or a target
              that just left). */}
          {passHostError && (
            <Typography
              sx={{
                mt: 1,
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 12,
                color: 'coral.main',
                textAlign: 'center',
              }}
            >
              {passHostError}
            </Typography>
          )}
        </Box>

        {/* Host-only collapsed "Game settings" row (fit-to-viewport redesign):
            the key fix that keeps the round-setup controls (family-safe,
            length, mode, favorites) from stealing the whole viewport. Tapping
            it opens <GameSettingsSheet> below, which holds the SAME controls
            that used to live inline here. Non-hosts never saw those controls,
            so this row is host-only too (unchanged gating). */}
        {isHost && (
          <Box
            component="button"
            type="button"
            onClick={() => setSettingsSheetOpen(true)}
            aria-haspopup="dialog"
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 2,
              width: '100%',
              textAlign: 'left',
              cursor: 'pointer',
              px: 3,
              py: 2.5,
              bgcolor: 'card.main',
              borderRadius: '20px',
              border: `1.5px solid ${alpha(theme.palette.stoneEdge.main, 0.22)}`,
              fontFamily: 'inherit',
              '&:focus-visible': { outline: `2px solid ${theme.palette.primary.main}`, outlineOffset: 2 },
            }}
          >
            <Box
              sx={{
                flexShrink: 0,
                width: 44,
                height: 44,
                borderRadius: '14px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                bgcolor: alpha(theme.palette.primary.main, 0.14),
                color: theme.palette.primary.main,
              }}
            >
              <FontAwesomeIcon icon="sliders" style={{ width: 19, height: 19 }} />
            </Box>
            <Stack spacing={0.25} sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography
                sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 16.5, color: 'text.primary' }}
              >
                Game settings
              </Typography>
              <Typography
                noWrap
                sx={{ fontFamily: '"Nunito", sans-serif', fontWeight: 700, fontSize: 13, color: 'text.secondary' }}
              >
                {settingsSummary}
              </Typography>
            </Stack>
            <Box sx={{ flexShrink: 0, color: 'text.secondary', display: 'flex' }}>
              <FontAwesomeIcon icon="chevron-right" style={{ width: 16, height: 16 }} />
            </Box>
          </Box>
        )}

        {/* Host reassurance, right above the pinned Start CTA. */}
        {isHost && (
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
            // Sit just above the pinned Start CTA for the host (now a short,
            // single-button bar); a joiner has no bar, so it hugs the bottom.
            bottom: isHost ? 112 : 40,
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

      {/* Pinned gold Start CTA (group-play/01, AC-05): host-only, always visible.
          The round-setup controls (family-safe, length, mode) now live in the
          settings sheet (opened via the collapsed row above) so the bar holds
          only the action it is designed for - the fixed spacer reserves exactly
          this button's height. onStart carries the host's family-safe + length +
          mode to the hub's startRound (server-authoritative). */}
      {isHost && (
        <BottomActionBar>
          <Button variant="contained" fullWidth onClick={() => onStart(familySafe, lengthPref, mode.config.id)}>
            <FontAwesomeIcon icon="play" style={{ width: 22, height: 22 }} />
            Start game
          </Button>
        </BottomActionBar>
      )}

      {/* Host-only settings sheet (fit-to-viewport redesign): holds the SAME
          family-safe / length / mode / favorites controls that used to be
          laid out inline above, reused as-is. Lobby owns every bit of their
          state; the sheet is pure slide-up chrome. */}
      {isHost && (
        <GameSettingsSheet open={settingsSheetOpen} onClose={() => setSettingsSheetOpen(false)}>
          {/* Safe by default (AC-04); its value + the length + mode travel out
              through onStart -> the hub's startRound (server-authoritative). */}
          <FamilySafeToggle checked={familySafe} onChange={handleFamilySafeChange} />
          <StoryLengthChoice value={lengthPref} onChange={setLengthPref} />
          {/* The host chooses the mode for the WHOLE room from the SHARED registry
              (the same cards Solo uses). Disabled cards + the family-safe fallback
              keep it on a startable mode (AC-04). */}
          <ModePicker
            modes={GROUP_MODES}
            selectedId={mode.config.id}
            onSelect={setMode}
            familySafe={familySafe}
            label="Choose a mode"
          />

          {/* Host-only "Play a favorite" inline picker (story-selection/06,
              AC-03), moved into the sheet from its old inline spot on the main
              screen: reveals the SAME <FavoritesList> the standalone Favorites
              screen uses (no second list implementation), so the host can
              start a round on one of their own favorited templates without
              leaving the lobby. */}
          <Box>
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
        </GameSettingsSheet>
      )}
    </Box>
  );
}
