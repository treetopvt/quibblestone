// ----------------------------------------------------------------------------
//  Lobby - the host's waiting room (session-engine/01 PLACEHOLDER).
//
//  IMPORTANT: this is a MINIMAL placeholder owned by story 01 so the host who
//  created a room lands somewhere real. Story 03 (player-roster) owns and will
//  REPLACE / EXPAND this file into the full styled Lobby (share tablet, 3-column
//  roster of Guardian tiles, "Carvers gathered" count, host crown + pulsing
//  ring, dashed empty slots, live join toasts, host-only "Start game" CTA). This
//  version deliberately stays small so story 03 can build over it cleanly.
//
//  For story 01 it must show (AC-02, AC-04):
//    - the room's short join code (the 4-slot carved code widget), so the host
//      can read it out to friends,
//    - that the caller is the host (a gold crown indicator),
//    - a clear "waiting for players" state.
//
//  App bar: a "leave" close button (left) that returns to Home; a settings
//  spacer (right) balances it. All look-and-feel comes from the theme
//  (theme.palette.*, the AppBar contract) - no hardcoded hex or px colors here.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Stack, Typography } from '@mui/material';
import { AppBar } from '../components';
import type { RoomState } from '../signalr/useGameHub';

export interface LobbyProps {
  /** The created room (code + roster). The host is room.players[0] in story 01. */
  room: RoomState;
  /** Leave the lobby and return Home (the app-bar close action). */
  onLeave: () => void;
}

/** Split a code into its individual carved slots (4 chars for the story-01 codes). */
function codeSlots(code: string): string[] {
  return code.split('');
}

export function Lobby({ room, onLeave }: LobbyProps) {
  const theme = useTheme();

  return (
    <Box sx={{ minHeight: '100vh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Waiting room"
        leftAction={{ icon: 'xmark', label: 'Leave room', onClick: onLeave }}
      />

      <Stack alignItems="center" spacing={5} sx={{ px: 5.5, pt: 3, pb: 6.5 }}>
        {/* ROOM CODE label + the 4-slot carved code widget (AC-02). */}
        <Stack spacing={2.5} alignItems="center" sx={{ width: '100%' }}>
          <Typography
            variant="overline"
            sx={{ fontSize: 13, fontWeight: 800, color: 'primary.main' }}
          >
            Room code
          </Typography>

          <Stack direction="row" spacing={1.75} justifyContent="center" sx={{ width: '100%' }}>
            {codeSlots(room.code).map((char, index) => (
              <Box
                // Positional key: the code is fixed for the room's lifetime.
                key={`slot-${index}`}
                sx={{
                  flex: 1,
                  maxWidth: 72,
                  height: 64,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  // Carved code-slot radius (design: 16px; docs/design/README.md
                  // "icon buttons: 14px / cards: 24px" - slots sit between).
                  borderRadius: '16px',
                  bgcolor: 'stoneSlot.alt',
                  boxShadow: `inset 0 3px 7px ${alpha(theme.palette.stoneEdge.main, 0.55)}`,
                  fontFamily: '"Fredoka", sans-serif',
                  fontWeight: 600,
                  fontSize: 32,
                  color: 'primary.main',
                }}
              >
                {char}
              </Box>
            ))}
          </Stack>

          <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary', textAlign: 'center' }}>
            Share this code so friends can gather round the stone.
          </Typography>
        </Stack>

        {/* Host indicator (AC-04): a gold crown marks the caller as the host. */}
        <Stack direction="row" spacing={1.75} alignItems="center">
          <Box sx={{ color: 'gold.main', fontSize: 18, display: 'flex' }}>
            <FontAwesomeIcon icon="crown" />
          </Box>
          <Typography sx={{ fontSize: 16, fontWeight: 700, color: 'text.primary' }}>
            You are the host
          </Typography>
        </Stack>

        {/* Waiting-for-players state (AC-04). */}
        <Stack
          spacing={1}
          alignItems="center"
          sx={{
            width: '100%',
            px: 4,
            py: 5,
            borderRadius: `${theme.shape.borderRadius}px`,
            border: `2px dashed ${alpha(theme.palette.primary.main, 0.35)}`,
            bgcolor: alpha(theme.palette.primary.main, 0.04),
          }}
        >
          <Typography sx={{ fontSize: 18, fontWeight: 700, color: 'primary.main' }}>
            Waiting for players...
          </Typography>
          <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary', textAlign: 'center' }}>
            Your crew will appear here as they join. The full roster arrives in the next update.
          </Typography>
        </Stack>
      </Stack>
    </Box>
  );
}
