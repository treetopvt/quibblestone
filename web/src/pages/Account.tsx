// ----------------------------------------------------------------------------
//  Account - the PURCHASER-only sign-in / restore surface (accounts-identity/03,
//  issue #69). A returning purchaser (who bought the family plan on another
//  device) enters their email, follows the emailed magic link, and their EXISTING
//  lightweight account (accounts-identity/02) is recognized on this device - the
//  read-side trigger for billing-entitlements/05's restore of what they own.
//
//  WHERE IT LIVES (AC-04, NON-NEGOTIABLE): this screen is reachable ONLY from the
//  Home "Account" entry link - a purchaser-facing, adult area. It is NEVER placed
//  in the join-code, lobby, word-entry (GroupRound), or reveal flow a child uses.
//  It reuses the SHARED <AppBar> (a left "back to home" action) - it does not fork
//  a second app-bar. App wires it at the '/account' route (an ordinary user-driven
//  entry screen, alongside '/favorites' and '/gallery' - not a live-game route).
//
//  FREE PLAY IS UNTOUCHED (AC-03): nothing here is required to play. A player who
//  never opens this screen (or opens it and leaves) plays the full free tier -
//  single-player or joining a group by code - with no prompt and no effect. This
//  surface talks ONLY to the purchaser sign-in client (../account/signInClient),
//  never to the SignalR hub or any room/player state.
//
//  DAY ONE / EMPTY (AC-06): with zero purchases anywhere, a sign-in attempt simply
//  resolves to the friendly "no purchase found - buy the family plan" state (a
//  guide, not an error, not a dead end). The screen loads and behaves fine with
//  nothing to restore.
//
//  NO ENUMERATION (AC-05): the request step shows the SAME neutral "check your
//  inbox" confirmation whether or not an account exists (the server does not
//  branch on existence). Only after following a real link does a genuine
//  purchaser get "signed in" or a non-purchaser get "guided to purchase".
//
//  DEV WALKABILITY: in the Development environment the API echoes the magic-link
//  token in the request response; when present, this screen offers a "Continue"
//  affordance so the whole flow is walkable locally with no email provider. In a
//  deployed environment there is no token in the response, so that affordance
//  never appears and the user follows the emailed link.
//
//  Styling: theme tokens ONLY (web/src/theme.ts) - no hex/raw-px in this file.
//  Stone-tablet / Guardian visual language, big tap targets, kid-readable (the
//  surface is adult-facing, but the app is ONE visual language). FontAwesome
//  icons only (registered in web/src/fontawesome.ts).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, TextField, Typography } from '@mui/material';
import { AppBar } from '../components';
import {
  requestSignInLink,
  verifySignIn,
  type SignInOutcome,
} from '../account/signInClient';

export interface AccountProps {
  /** Return to Home (the shared app-bar back action). */
  onBack: () => void;
}

/** The screen's phase: the email form, the "check your email" confirmation, or a finished outcome. */
type Phase = 'form' | 'sent' | SignInOutcome;

interface AccountForm {
  email: string;
}

/** A basic, forgiving email shape check (client-side friendliness only - the server is authoritative). */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Props for {@link OutcomePanel}. */
interface OutcomePanelProps {
  icon: 'envelope' | 'circle-check' | 'shield-heart';
  tint: string;
  title: string;
  body: string;
  children?: React.ReactNode;
}

/** A soft, tablet-style info panel used for the confirmation / outcome states. */
function OutcomePanel({ icon, tint, title, body, children }: OutcomePanelProps) {
  const theme = useTheme();
  return (
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
          bgcolor: alpha(tint, 0.14),
          color: tint,
          fontSize: 26,
        }}
      >
        <FontAwesomeIcon icon={icon} />
      </Box>
      <Typography sx={{ fontWeight: 800, fontSize: 18, color: 'text.primary' }}>{title}</Typography>
      <Typography sx={{ fontWeight: 600, fontSize: 14.5, color: 'text.secondary', maxWidth: 300 }}>
        {body}
      </Typography>
      {children}
    </Stack>
  );
}

export function Account({ onBack }: AccountProps) {
  const theme = useTheme();
  const { control, handleSubmit, watch, formState } = useForm<AccountForm>({
    defaultValues: { email: '' },
    mode: 'onChange',
  });

  const [phase, setPhase] = useState<Phase>('form');
  // The friendly message from the latest step (neutral confirmation or an outcome).
  const [message, setMessage] = useState<string>('');
  // The signed-in purchaser email, shown on the success state.
  const [signedInEmail, setSignedInEmail] = useState<string | null>(null);
  // DEV ONLY: the echoed magic-link token, enabling the local "Continue" affordance.
  const [devToken, setDevToken] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);

  const email = watch('email');
  const canSubmit = !formState.isSubmitting && EMAIL_PATTERN.test(email.trim());

  const onSubmit = handleSubmit(async (values) => {
    const result = await requestSignInLink(values.email.trim());
    setMessage(result.message);
    setDevToken(result.devToken ?? null);
    // Always advance to the neutral "check your email" state on an accepted
    // request (AC-05: no existence tell). A transport failure keeps us on the
    // form with the friendly message shown inline.
    setPhase(result.ok ? 'sent' : 'form');
  });

  // DEV walkability only: follow the echoed token to complete sign-in locally.
  const handleContinueWithDevToken = async () => {
    if (!devToken || verifying) return;
    setVerifying(true);
    try {
      const result = await verifySignIn(devToken);
      setMessage(result.message);
      setSignedInEmail(result.email ?? null);
      setPhase(result.outcome);
    } finally {
      setVerifying(false);
    }
  };

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar title="Account" leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onBack }} />

      <Stack spacing={4} sx={{ px: 5.5, pt: 3, pb: 6 }}>
        {phase === 'form' && (
          <>
            <Stack spacing={1.5} sx={{ textAlign: 'center' }}>
              <Typography sx={{ fontWeight: 800, fontSize: 18, color: 'text.primary' }}>
                Restore your purchase
              </Typography>
              <Typography sx={{ fontWeight: 600, fontSize: 14.5, color: 'text.secondary' }}>
                Bought the family plan on another device? Enter your email and we will send a
                one-tap sign-in link to restore it here.
              </Typography>
            </Stack>

            <Box component="form" onSubmit={onSubmit} noValidate>
              <Stack spacing={3}>
                <Controller
                  name="email"
                  control={control}
                  rules={{ pattern: EMAIL_PATTERN }}
                  render={({ field }) => (
                    <TextField
                      {...field}
                      type="email"
                      fullWidth
                      label="Email"
                      placeholder="you@example.com"
                      autoComplete="email"
                      inputMode="email"
                    />
                  )}
                />
                <Button
                  type="submit"
                  variant="contained"
                  fullWidth
                  disabled={!canSubmit}
                  startIcon={<FontAwesomeIcon icon="envelope" style={{ width: 18, height: 18 }} />}
                >
                  {formState.isSubmitting ? 'Sending...' : 'Email me a sign-in link'}
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

            {/* Free-play reassurance (AC-03): signing in is only for purchasers. */}
            <Stack direction="row" spacing={1.5} alignItems="center" justifyContent="center">
              <Box sx={{ color: 'teal.main', fontSize: 15, display: 'flex' }}>
                <FontAwesomeIcon icon="shield-heart" />
              </Box>
              <Typography sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary' }}>
                Playing is always free - no account needed
              </Typography>
            </Stack>
          </>
        )}

        {phase === 'sent' && (
          <OutcomePanel
            icon="envelope"
            tint={theme.palette.primary.main}
            title="Check your email"
            body={message}
          >
            {/* DEV ONLY: walk the flow locally without an email provider. */}
            {devToken && (
              <Button
                variant="outlined"
                fullWidth
                onClick={() => void handleContinueWithDevToken()}
                disabled={verifying}
                startIcon={<FontAwesomeIcon icon="arrow-right" style={{ width: 16, height: 16 }} />}
              >
                {verifying ? 'Signing in...' : 'Continue (dev link)'}
              </Button>
            )}
          </OutcomePanel>
        )}

        {phase === 'signed-in' && (
          <OutcomePanel
            icon="circle-check"
            tint={theme.palette.teal.main}
            title="You're signed in"
            body={message}
          >
            {signedInEmail && (
              <Typography sx={{ fontSize: 13.5, fontWeight: 800, color: 'text.primary' }}>
                {signedInEmail}
              </Typography>
            )}
          </OutcomePanel>
        )}

        {(phase === 'no-account' || phase === 'link-invalid' || phase === 'error') && (
          <OutcomePanel
            icon="shield-heart"
            tint={theme.palette.gold.main}
            title={phase === 'no-account' ? 'No purchase found yet' : 'That link did not work'}
            body={message}
          >
            <Button
              variant="outlined"
              fullWidth
              onClick={() => {
                setPhase('form');
                setMessage('');
                setDevToken(null);
              }}
              startIcon={<FontAwesomeIcon icon="arrow-left" style={{ width: 16, height: 16 }} />}
            >
              Try another email
            </Button>
          </OutcomePanel>
        )}
      </Stack>
    </Box>
  );
}
