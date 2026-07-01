// ----------------------------------------------------------------------------
//  Join - enter a room code + display name to join a game (session-engine/02,
//  design screen 2).
//
//  Fully anonymous by design (README section 6, AC-02): the ONLY things this
//  screen asks for are a room code and a display name - never an account, an
//  email, a password, or any PII. A shield reassurance line ("100% anonymous -
//  no email, no account") says so out loud. Faithfully recreates
//  docs/design/Join.dc.html / docs/design/README.md (Screens - screen 2):
//    - shared <AppBar> with a back arrow that returns Home
//    - a room-code card: "ROOM CODE" label + teal "from the host" chip, then
//      four carved code slots driven by a single controlled input
//    - a character card: a "Display name" outlined field (person icon, live
//      n/14 counter). The avatar grid is story 05 - this card is deliberately
//      laid out so story 05 can drop the "Choose your guardian" grid in below
//      the name field without restructuring.
//    - the shield reassurance line
//    - a pinned gold CTA (BottomActionBar) reading "Join [CODE] ->", with the
//      entered code interpolated (falls back to "Join ->" when empty)
//
//  Contracts reused (never re-specified here): the gold CTA is just
//  <Button variant="contained">; the AppBar and BottomActionBar come from the
//  shared component barrel; all colors / radii / spacing come from the MUI theme
//  (theme.palette.stoneSlot / stoneEdge / primary / teal / card, theme.spacing)
//  - no hardcoded hex or raw-px spacing here. Icons are FontAwesome only
//  (web/src/fontawesome.ts).
//
//  Form state uses react-hook-form with controlled MUI inputs. The code input is
//  normalized (uppercased, restricted to the unambiguous code alphabet, capped
//  at 4 chars) so the four carved slots always reflect a valid, readable code.
//  The display name is validated server-side (length + the content-safety
//  filter) via the hub; the friendly error it returns is shown inline and the
//  player stays here to try again (AC-03, AC-04, AC-06).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import {
  Box,
  Button,
  InputAdornment,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { AppBar, BottomActionBar, BottomActionBarSpacer } from '../components';
import type { JoinResult } from '../signalr/useGameHub';

export interface JoinProps {
  /**
   * Attempt to join a room. Resolves with the JoinResult envelope - on ok the
   * app flips to the lobby (the hook's room is set); on failure the returned
   * error is shown inline and the player stays here (AC-03, AC-04, AC-06).
   */
  onJoin: (code: string, displayName: string, variant: string) => Promise<JoinResult>;
  /** Return to Home (the app-bar back action). */
  onBack: () => void;
  /** True until the real-time connection is ready - the CTA needs the hub to act. */
  disabled?: boolean;
}

/** The number of carved code slots (story-01 codes are 4 chars). */
const CODE_LENGTH = 4;

/** Max display-name length (AC-03) - kept in sync with the hub's server check. */
const MAX_NAME_LENGTH = 14;

// The unambiguous code alphabet the server mints codes from (RoomRegistry): A-Z
// and 2-9 with the look-alike glyphs O, 0, I, 1, l removed. We restrict the code
// input to exactly these characters so a player cannot type a code the server
// could never have issued.
const CODE_ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';

/** Normalize raw code input: uppercase, keep only alphabet chars, cap at 4. */
function normalizeCode(raw: string): string {
  return raw
    .toUpperCase()
    .split('')
    .filter((ch) => CODE_ALPHABET.includes(ch))
    .join('')
    .slice(0, CODE_LENGTH);
}

interface JoinForm {
  code: string;
  displayName: string;
}

export function Join({ onJoin, onBack, disabled = false }: JoinProps) {
  const theme = useTheme();
  const { control, handleSubmit, watch, formState } = useForm<JoinForm>({
    defaultValues: { code: '', displayName: '' },
    mode: 'onChange',
  });

  // Inline friendly error from the server-side join (unknown code, blocked or
  // duplicate name, ...). Cleared whenever a new submit starts (AC-03/04/06).
  const [joinError, setJoinError] = useState<string | null>(null);

  const code = watch('code');
  const displayName = watch('displayName');

  const codeChars = code.split('');
  const canSubmit =
    !disabled &&
    !formState.isSubmitting &&
    code.length === CODE_LENGTH &&
    displayName.trim().length > 0;

  const onSubmit = handleSubmit(async (values) => {
    setJoinError(null);
    // Guardian variant defaults to "teal" until story 05 adds the avatar grid.
    const result = await onJoin(values.code, values.displayName, 'teal');
    if (!result.ok) {
      setJoinError(result.error ?? 'That did not work - please try again.');
    }
    // On success the app flips to the lobby (the hook's room is now set); no
    // local navigation needed here.
  });

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Join a game"
        leftAction={{ icon: 'arrow-left', label: 'Back', onClick: onBack }}
      />

      <Box component="form" onSubmit={onSubmit} noValidate>
        <Stack spacing={4} sx={{ px: 5.5, pt: 3 }}>
          {/* ROOM-CODE CARD: label + "from the host" chip, then 4 carved slots. */}
          <Stack
            spacing={3}
            sx={{
              p: 5,
              borderRadius: '24px',
              bgcolor: 'card.main',
              boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
            }}
          >
            <Stack direction="row" alignItems="center" justifyContent="space-between">
              <Typography
                variant="overline"
                sx={{ fontSize: 13, fontWeight: 800, color: 'primary.main' }}
              >
                Room code
              </Typography>
              <Box
                sx={{
                  px: 2.5,
                  py: 0.75,
                  borderRadius: 999,
                  bgcolor: alpha(theme.palette.teal.main, 0.14),
                  color: 'teal.dark',
                  fontSize: 12.5,
                  fontWeight: 800,
                }}
              >
                from the host
              </Box>
            </Stack>

            {/* A single controlled input drives all four carved slots. The real
                <input> is transparent and overlaid across the slot row so taps
                anywhere focus it and the device keyboard (uppercase) appears. */}
            <Controller
              name="code"
              control={control}
              render={({ field }) => (
                <Box sx={{ position: 'relative' }}>
                  <Stack
                    direction="row"
                    spacing={1.75}
                    justifyContent="center"
                    aria-hidden
                  >
                    {Array.from({ length: CODE_LENGTH }).map((_, index) => (
                      <Box
                        // Positional key: a fixed-length row of slots.
                        key={`slot-${index}`}
                        sx={{
                          flex: 1,
                          maxWidth: 72,
                          height: 64,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          borderRadius: '16px',
                          bgcolor: 'stoneSlot.alt',
                          boxShadow: `inset 0 3px 7px ${alpha(theme.palette.stoneEdge.main, 0.55)}`,
                          fontFamily: '"Fredoka", sans-serif',
                          fontWeight: 600,
                          fontSize: 32,
                          color: 'primary.main',
                          // The currently-focused (next-to-type) slot glows.
                          outline:
                            index === codeChars.length && code.length < CODE_LENGTH
                              ? `2px solid ${alpha(theme.palette.primary.main, 0.55)}`
                              : 'none',
                          outlineOffset: '-2px',
                        }}
                      >
                        {codeChars[index] ?? ''}
                      </Box>
                    ))}
                  </Stack>

                  <Box
                    component="input"
                    value={field.value}
                    onChange={(event) =>
                      field.onChange(normalizeCode(event.currentTarget.value))
                    }
                    onBlur={field.onBlur}
                    inputMode="text"
                    autoCapitalize="characters"
                    autoCorrect="off"
                    spellCheck={false}
                    maxLength={CODE_LENGTH}
                    aria-label="Room code"
                    sx={{
                      position: 'absolute',
                      inset: 0,
                      width: '100%',
                      height: '100%',
                      border: 'none',
                      background: 'transparent',
                      // Invisible glyphs: the carved slots render the value.
                      color: 'transparent',
                      caretColor: 'transparent',
                      textAlign: 'center',
                      letterSpacing: '2em',
                      cursor: 'pointer',
                      '&:focus': { outline: 'none' },
                    }}
                  />
                </Box>
              )}
            />
          </Stack>

          {/* CHARACTER CARD: display name only (story 05 adds the avatar grid
              below the field). */}
          <Stack
            spacing={2.5}
            sx={{
              p: 5,
              borderRadius: '24px',
              bgcolor: 'card.main',
              boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
            }}
          >
            <Controller
              name="displayName"
              control={control}
              rules={{ maxLength: MAX_NAME_LENGTH }}
              render={({ field }) => (
                <TextField
                  {...field}
                  onChange={(event) =>
                    field.onChange(event.currentTarget.value.slice(0, MAX_NAME_LENGTH))
                  }
                  label="Display name"
                  variant="outlined"
                  fullWidth
                  slotProps={{
                    htmlInput: { maxLength: MAX_NAME_LENGTH, 'aria-label': 'Display name' },
                    input: {
                      startAdornment: (
                        <InputAdornment position="start">
                          <Box sx={{ color: 'primary.main', display: 'flex' }}>
                            <FontAwesomeIcon icon="user" />
                          </Box>
                        </InputAdornment>
                      ),
                    },
                  }}
                  helperText={`${displayName.length}/${MAX_NAME_LENGTH}`}
                  sx={{ '& .MuiFormHelperText-root': { textAlign: 'right', fontWeight: 700 } }}
                />
              )}
            />

            {/* Story-05 seam: the "Choose your guardian" avatar grid drops in
                here, below the display-name field, inside this same card. */}
          </Stack>

          {/* Inline server-side error (unknown code, blocked/duplicate name). */}
          {joinError && (
            <Typography
              role="alert"
              sx={{ textAlign: 'center', fontSize: 14, fontWeight: 700, color: 'error.main' }}
            >
              {joinError}
            </Typography>
          )}

          {/* Reassurance: shield + "100% anonymous - no email, no account" (AC-02). */}
          <Stack direction="row" spacing={1.75} alignItems="center" justifyContent="center">
            <Box sx={{ color: 'teal.main', fontSize: 16, display: 'flex' }}>
              <FontAwesomeIcon icon="shield" />
            </Box>
            <Typography sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.secondary' }}>
              100% anonymous - no email, no account
            </Typography>
          </Stack>

          {/* Reserve room so the pinned CTA never covers the reassurance line. */}
          <BottomActionBarSpacer />
        </Stack>

        {/* Pinned gold CTA: "Join [CODE] ->" with the entered code interpolated. */}
        <BottomActionBar>
          <Button
            type="submit"
            variant="contained"
            fullWidth
            disabled={!canSubmit}
            endIcon={<FontAwesomeIcon icon="arrow-right" />}
          >
            {code ? `Join ${code}` : 'Join'}
          </Button>
        </BottomActionBar>
      </Box>
    </Box>
  );
}
