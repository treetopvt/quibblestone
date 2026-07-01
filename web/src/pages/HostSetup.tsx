// ----------------------------------------------------------------------------
//  HostSetup - the HOST names themselves + picks a Guardian before the room is
//  minted (build/host-identity).
//
//  The gap this closes: "Create a game" used to mint the room immediately with an
//  EMPTY host nickname + the default "teal" variant (only JOINERS got a name +
//  avatar step), so the host showed blank in the lobby, the reveal attribution,
//  and the Round Complete recap - and buildCrew() even dropped the host's words
//  from the recap tally. Now the host goes through this screen first, exactly like
//  a joiner names themselves on Join, and the chosen name is safety-filtered
//  SERVER-SIDE on CreateRoom (same gate as joiners) before the room exists.
//
//  Reuses the SAME shared pieces as Join (CLAUDE.md section 4 - one consistent
//  system, not per-screen styling): the shared <AppBar> + <BottomActionBar>, the
//  same card chrome (rounded card surface + soft shadow), and the shared
//  <PlayerIdentityFields> for the name field + guardian grid. The only differences
//  from Join are the copy ("Create game" instead of "Join [CODE]") and that there
//  is no room-code card (the host is minting the code, not entering one).
//
//  Form state is react-hook-form with controlled MUI inputs (like Join). On submit
//  it calls onCreate(displayName, variant), which resolves { ok, error } mirroring
//  Join's onJoin: on ok the app's room-effect lands the host in the lobby; on !ok
//  the friendly server error (a blocked / empty / too-long name) shows inline and
//  the host stays here to fix it. The gold "Create game" CTA is disabled until the
//  name is non-empty. All colors / radii / spacing come from the MUI theme (no
//  hardcoded hex or raw-px spacing); icons are FontAwesome only.
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
import type { CreateRoomResult } from '../signalr/useGameHub';

export interface HostSetupProps {
  /**
   * Create the room as the host with the chosen name + variant. Resolves with the
   * CreateRoomResult envelope - on ok the app flips to the lobby (the hook's room
   * is set); on failure the returned error is shown inline and the host stays here
   * (mirrors Join's onJoin, AC-03).
   */
  onCreate: (displayName: string, variant: string) => Promise<CreateRoomResult>;
  /** Return to Home (the app-bar back action). */
  onBack: () => void;
  /** True until the real-time connection is ready - the CTA needs the hub to act. */
  disabled?: boolean;
  /**
   * Pre-fill the display name (build/host-identity): a returning player's last-used
   * name from device-local storage (App reads it from identity.ts). Defaults to ''.
   */
  initialNickname?: string;
  /** Pre-fill the Guardian variant (build/host-identity), defaulting to 'teal'. */
  initialVariant?: GuardianVariant;
}

interface HostSetupForm {
  displayName: string;
  selectedVariant: GuardianVariant;
}

export function HostSetup({
  onCreate,
  onBack,
  disabled = false,
  initialNickname = '',
  initialVariant = DEFAULT_VARIANT,
}: HostSetupProps) {
  const theme = useTheme();
  const { control, handleSubmit, watch, formState } = useForm<HostSetupForm>({
    defaultValues: { displayName: initialNickname, selectedVariant: initialVariant },
    mode: 'onChange',
  });

  // Inline friendly error from the server-side create (a blocked / empty /
  // too-long name). Cleared whenever a new submit starts.
  const [createError, setCreateError] = useState<string | null>(null);

  const displayName = watch('displayName');
  const canSubmit = !disabled && !formState.isSubmitting && displayName.trim().length > 0;

  const onSubmit = handleSubmit(async (values) => {
    setCreateError(null);
    // The chosen Guardian variant travels with the name; the server independently
    // normalizes it to a known variant and safety-filters the name either way.
    const result = await onCreate(values.displayName, values.selectedVariant);
    if (!result.ok) {
      setCreateError(result.error ?? 'That did not work - please try again.');
    }
    // On success the app flips to the lobby (the hook's room is now set); no local
    // navigation needed here.
  });

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Create a game"
        leftAction={{ icon: 'arrow-left', label: 'Back', onClick: onBack }}
      />

      <Box component="form" onSubmit={onSubmit} noValidate>
        <Stack spacing={4} sx={{ px: 5.5, pt: 3 }}>
          {/* CHARACTER CARD: the shared identity controls (build/host-identity) -
              display name + "Choose your guardian" avatar grid. The same
              <PlayerIdentityFields> Join uses, wired to this screen's
              react-hook-form Controllers. */}
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

          {/* Inline server-side error (a blocked / empty / too-long name). */}
          {createError && (
            <Typography
              role="alert"
              sx={{ textAlign: 'center', fontSize: 14, fontWeight: 700, color: 'error.main' }}
            >
              {createError}
            </Typography>
          )}

          {/* Reassurance: shield + "100% anonymous - no email, no account". */}
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

        {/* Pinned gold CTA: "Create game". */}
        <BottomActionBar>
          <Button
            type="submit"
            variant="contained"
            fullWidth
            disabled={!canSubmit}
            endIcon={<FontAwesomeIcon icon="arrow-right" />}
          >
            Create game
          </Button>
        </BottomActionBar>
      </Box>
    </Box>
  );
}
