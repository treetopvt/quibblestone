// ----------------------------------------------------------------------------
//  Join - enter a room code + display name + Guardian avatar to join a game
//  (session-engine/02 + session-engine/05, design screen 2).
//
//  Fully anonymous by design (README section 6, AC-02): the ONLY things this
//  screen asks for are a room code, a display name, and a Guardian avatar -
//  never an account, an email, a password, or any PII. A shield reassurance
//  line ("100% anonymous - no email, no account") says so out loud.
//  Faithfully recreates docs/design/Join.dc.html / docs/design/README.md
//  (Screens - screen 2):
//    - shared <AppBar> with a back arrow that returns Home
//    - a room-code card: "ROOM CODE" label + teal "from the host" chip, then
//      four carved code slots driven by a single controlled input
//    - a character card: the shared <PlayerIdentityFields> - a "Display name"
//      outlined field (person icon, live n/14 counter), then "Choose your
//      guardian" and a 3-column, 6-tile avatar grid (session-engine/05) -
//      single-select, teal pre-selected. The selected tile shows a gold ring +
//      a gold check badge that pops in. build/host-identity extracted this markup
//      into the shared component so HostSetup (the host naming itself) reuses the
//      SAME controls; here it is wired to this screen's react-hook-form Controllers.
//    - the shield reassurance line
//    - a pinned gold CTA (BottomActionBar) reading "Join [CODE] ->", with the
//      entered code interpolated (falls back to "Join ->" when empty)
//
//  Contracts reused (never re-specified here): the gold CTA is just
//  <Button variant="contained">; the AppBar, BottomActionBar, and the identity
//  controls (PlayerIdentityFields) come from the shared component barrel; the
//  Guardian avatar is the shared <Guardian> component (never rebuilt here); all
//  colors / radii / spacing come from the
//  MUI theme (theme.palette.stoneSlot / stoneEdge / primary / teal / card /
//  gold / guardianAccent, theme.spacing) - no hardcoded hex or raw-px spacing
//  here. Icons are FontAwesome only (web/src/fontawesome.ts).
//
//  Form state uses react-hook-form with controlled MUI inputs. The code input is
//  normalized (uppercased, restricted to the unambiguous code alphabet, capped
//  at 4 chars) so the four carved slots always reflect a valid, readable code.
//  The display name is validated server-side (length + the content-safety
//  filter) via the hub; the friendly error it returns is shown inline and the
//  player stays here to try again (AC-03, AC-04, AC-06). The selected Guardian
//  variant is sent alongside the name on submit (session-engine/05, AC-03) -
//  the server independently normalizes it to a known variant, so this is a
//  best-effort/UX-only guard, not the source of truth.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography } from '@mui/material';
import {
  AppBar,
  BottomActionBar,
  BottomActionBarSpacer,
  PlayerIdentityFields,
  DEFAULT_VARIANT,
} from '../components';
import type { GuardianVariant } from '../components';
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
  /**
   * Pre-fill the display name (build/host-identity): a returning player's
   * last-used name from device-local storage (App reads it from identity.ts), so
   * they do not retype. Defaults to '' (a fresh device / no prior play).
   */
  initialNickname?: string;
  /** Pre-fill the Guardian variant (build/host-identity), defaulting to 'teal'. */
  initialVariant?: GuardianVariant;
  /**
   * Pre-fill the room code from a `/join/:code` deep link (design-system/04 /
   * session-engine/06). Normalized through the same rule as typed input, so a
   * link can only seed a code the server could have issued. Defaults to '' (the
   * plain `/join` route or a manual "Join a game" tap).
   */
  initialCode?: string;
}

/** The number of carved code slots (story-01 codes are 4 chars). */
const CODE_LENGTH = 4;

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
  selectedVariant: GuardianVariant;
}

export function Join({
  onJoin,
  onBack,
  disabled = false,
  initialNickname = '',
  initialVariant = DEFAULT_VARIANT,
  initialCode = '',
}: JoinProps) {
  const theme = useTheme();
  const { control, handleSubmit, watch, formState } = useForm<JoinForm>({
    defaultValues: {
      code: normalizeCode(initialCode),
      displayName: initialNickname,
      selectedVariant: initialVariant,
    },
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
    // The chosen Guardian variant travels with the nickname (AC-03); the
    // server independently normalizes it to a known variant either way.
    const result = await onJoin(values.code, values.displayName, values.selectedVariant);
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

          {/* CHARACTER CARD: the shared identity controls (build/host-identity) -
              display name + "Choose your guardian" avatar grid. The same
              <PlayerIdentityFields> HostSetup uses, wired to this screen's
              react-hook-form Controllers so all Join behavior is unchanged. */}
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
              render={({ field: nameField }) => (
                <Controller
                  name="selectedVariant"
                  control={control}
                  render={({ field: variantField }) => (
                    <PlayerIdentityFields
                      nickname={nameField.value}
                      variant={variantField.value}
                      onNicknameChange={nameField.onChange}
                      onVariantChange={variantField.onChange}
                    />
                  )}
                />
              )}
            />
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
