// ----------------------------------------------------------------------------
//  RedeemDevice - the "link this device" screen (accounts-identity/09, AC-02).
//
//  Reachable WITHOUT being signed in: a kid's device is never signed in
//  (README section 6 - kids stay anonymous forever), so this screen sends NO
//  purchaser credential - only the code a parent read off the Account page's
//  "Link a device" card (accounts-identity/09, AC-01). Registered at App.tsx's
//  '/link-device' route.
//
//  On a successful redeem (ok:true) the returned token is persisted device-
//  locally via ../account/familyDeviceToken.ts (deliberately localStorage, not
//  the in-memory PurchaserSession - see that module's header for why), and a
//  friendly confirmation shows the device's own short, non-identifying label
//  (e.g. "quiet fox") so the parent can visually confirm the right device
//  linked. On ok:false the server's friendly message is shown inline and the
//  parent/kid can try again - a mistyped or expired code is never a dead end.
//
//  This screen does NOT itself unlock anything: a freshly redeemed device
//  starts with `IsAdultConfirmedDevice = false` (family-safe by default,
//  AC-02/AC-07) until an adult explicitly opts it into teen-plus content from
//  the Account page's linked-devices list (AC-04/AC-07) - nothing here implies
//  otherwise.
//
//  Reuses the shared <AppBar> + theme tokens (web/src/theme.ts) - no second
//  visual language, no hex/raw-px spacing. Icons are FontAwesome only
//  (web/src/fontawesome.ts). Form state uses react-hook-form, matching
//  Account.tsx's posture for the app's other free-text-adjacent form.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, TextField, Typography } from '@mui/material';
import { AppBar } from '../components';
import { redeemDeviceLinkCode } from '../account/deviceRedeemClient';
import { saveFamilyDeviceToken } from '../account/familyDeviceToken';

export interface RedeemDeviceProps {
  /** Return to Home (the shared app-bar back action). */
  onBack: () => void;
}

interface RedeemForm {
  code: string;
}

type Phase = 'form' | 'linked';

export function RedeemDevice({ onBack }: RedeemDeviceProps) {
  const theme = useTheme();
  const { control, handleSubmit, watch, formState, reset } = useForm<RedeemForm>({
    defaultValues: { code: '' },
    mode: 'onChange',
  });

  const [phase, setPhase] = useState<Phase>('form');
  const [message, setMessage] = useState<string>('');
  const [label, setLabel] = useState<string | null>(null);

  const code = watch('code');
  const canSubmit = !formState.isSubmitting && code.trim().length > 0;

  const onSubmit = handleSubmit(async (values) => {
    const result = await redeemDeviceLinkCode(values.code.trim());
    if (result.ok && result.token) {
      saveFamilyDeviceToken(result.token);
      setLabel(result.label ?? null);
      setMessage(result.message);
      setPhase('linked');
    } else {
      setMessage(result.message);
    }
  });

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar title="Link a device" leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onBack }} />

      <Stack spacing={4} sx={{ px: 5.5, pt: 3, pb: 6 }}>
        {phase === 'form' && (
          <>
            <Stack spacing={1.5} sx={{ textAlign: 'center' }}>
              <Typography sx={{ fontWeight: 800, fontSize: 18, color: 'text.primary' }}>
                Link this device
              </Typography>
              <Typography sx={{ fontWeight: 600, fontSize: 14.5, color: 'text.secondary' }}>
                Ask a grown-up to read you the code from their Account page, then type it
                in here - just this once, this device stays linked.
              </Typography>
            </Stack>

            <Box component="form" onSubmit={onSubmit} noValidate>
              <Stack spacing={3}>
                <Controller
                  name="code"
                  control={control}
                  rules={{ required: true }}
                  render={({ field }) => (
                    <TextField
                      {...field}
                      fullWidth
                      label="Link code"
                      placeholder="Enter the code"
                      autoComplete="off"
                      autoCapitalize="characters"
                      inputProps={{ style: { textTransform: 'uppercase', letterSpacing: '0.08em' } }}
                    />
                  )}
                />
                <Button
                  type="submit"
                  variant="contained"
                  fullWidth
                  disabled={!canSubmit}
                  startIcon={<FontAwesomeIcon icon="link" style={{ width: 18, height: 18 }} />}
                >
                  {formState.isSubmitting ? 'Linking...' : 'Link this device'}
                </Button>
                {message && (
                  <Typography
                    role="status"
                    sx={{ fontSize: 13, fontWeight: 700, color: 'error.main', textAlign: 'center' }}
                  >
                    {message}
                  </Typography>
                )}
              </Stack>
            </Box>

            <Stack direction="row" spacing={1.5} alignItems="center" justifyContent="center">
              <Box sx={{ color: 'teal.main', fontSize: 15, display: 'flex' }}>
                <FontAwesomeIcon icon="shield-heart" />
              </Box>
              <Typography sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary' }}>
                Linking stays family-safe until a grown-up says otherwise
              </Typography>
            </Stack>
          </>
        )}

        {phase === 'linked' && (
          <Stack
            spacing={2.5}
            alignItems="center"
            sx={{
              p: 5,
              borderRadius: '24px',
              bgcolor: 'card.main',
              textAlign: 'center',
              boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
            }}
          >
            <Box
              aria-hidden
              sx={{
                width: 64,
                height: 64,
                borderRadius: '50%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                bgcolor: alpha(theme.palette.teal.main, 0.14),
                color: theme.palette.teal.main,
                fontSize: 26,
              }}
            >
              <FontAwesomeIcon icon="circle-check" />
            </Box>
            <Typography sx={{ fontWeight: 800, fontSize: 18, color: 'text.primary' }}>
              This device is linked
            </Typography>
            <Typography sx={{ fontWeight: 600, fontSize: 14.5, color: 'text.secondary', maxWidth: 300 }}>
              {message || 'This device now carries the family’s unlocks. Play away!'}
            </Typography>
            {label && (
              <Typography sx={{ fontSize: 13.5, fontWeight: 800, color: 'text.primary' }}>
                Device name: {label}
              </Typography>
            )}
            <Button
              variant="outlined"
              fullWidth
              onClick={() => {
                reset({ code: '' });
                setMessage('');
                setLabel(null);
                setPhase('form');
              }}
              startIcon={<FontAwesomeIcon icon="arrow-left" style={{ width: 16, height: 16 }} />}
            >
              Link another device
            </Button>
            <Button
              variant="text"
              onClick={() => {
                // accounts-identity/09 (Copilot review): the family-device token is
                // resolved into the session's capabilities + adult-unlock signal at hub
                // CONNECT time (GameHub.OnConnectedAsync). The single SignalR connection
                // was already established (as anonymous) BEFORE this device redeemed, so a
                // client-side navigation home would keep playing on that stale connection
                // and the freshly linked grants would not apply until the next app launch.
                // A hard navigation to Home reboots the SPA so useGameHub rebuilds the
                // connection and its accessTokenFactory picks up the just-stored token -
                // "Start playing" then genuinely carries the family's unlocks this session.
                window.location.assign('/');
              }}
              sx={{ fontWeight: 800, fontSize: 13 }}
            >
              Start playing
            </Button>
          </Stack>
        )}
      </Stack>
    </Box>
  );
}
