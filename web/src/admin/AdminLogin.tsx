// ----------------------------------------------------------------------------
//  AdminLogin - the ONLY screen of the SEPARATE operator back office
//  (sysadmin-console/01, issue #135). An operator enters their email, follows the
//  magic link, and - only if their email is on the SERVER-side operator allowlist -
//  an operator session is established. This is the login gate for the whole back
//  office; actual admin capability (grant / revoke / takedown) is stories 02/03.
//
//  SEPARATE BUNDLE / NO KID-APP EDGE (AC-04, load-bearing): this file lives in the
//  admin bundle (its own entry web/admin.html -> web/src/admin/main.tsx) and imports
//  NOTHING from the kid app - not web/src/pages, web/src/signalr, web/src/gallery,
//  web/src/engine, or web/src/components. It may share the visual language via
//  web/src/theme.ts (imported by main.tsx's ThemeProvider) and registers its own
//  minimal FontAwesome set. There is NO nav / deep-link / service-worker path from
//  Home / Join / Lobby / FillBlank / Reveal that reaches this surface.
//
//  UNAUTHENTICATED SEES ONLY LOGIN (AC-06): the back office has no admin/room/
//  player/purchaser data on this screen - it renders the email form and nothing
//  else until an operator session exists. It fetches no admin data before login.
//
//  ALLOWLIST AT VERIFY (AC-02): the request step shows the SAME neutral "check your
//  inbox" confirmation whether or not the email is an operator (the server never
//  consults the allowlist at issue time). Only after following a real link does an
//  allowlisted operator get "signed in", or a non-operator get "not authorized".
//
//  COLLECTS ONLY THE EMAIL (AC-07): the form asks for nothing beyond the operator
//  email used to issue / verify the link - no name, no player / session reference.
//
//  Styling: theme tokens ONLY (web/src/theme.ts) - no hex / raw-px literals. The
//  form uses react-hook-form with controlled MUI inputs, big tap targets. Adult-
//  facing and minimal, but the ONE visual language (theme-driven, not a bespoke
//  design system). FontAwesome icons only (registered in ./fontawesome).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useEffect, useRef, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, CircularProgress, Stack, TextField, Typography } from '@mui/material';
import {
  requestOperatorLink,
  verifyOperatorLink,
  type OperatorOutcome,
} from './operatorClient';

/** The screen's phase: the email form, the "check your inbox" confirmation, or a finished outcome. */
type Phase = 'form' | 'sent' | OperatorOutcome;

interface AdminLoginForm {
  email: string;
}

/** A basic, forgiving email shape check (client-side friendliness only - the server is authoritative). */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Props for {@link OutcomePanel}. */
interface OutcomePanelProps {
  icon: 'envelope' | 'circle-check' | 'triangle-exclamation';
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

/** Props for {@link AdminLogin}. */
interface AdminLoginProps {
  /**
   * Called after a magic link verifies as an ALLOWLISTED operator (the server set
   * the session cookie). The shell re-checks the session and switches to the
   * console, so a followed link lands the operator IN the back office rather than
   * on a dead-end "signed in" panel.
   */
  onAuthenticated?: () => void;
}

export function AdminLogin({ onAuthenticated }: AdminLoginProps = {}) {
  const theme = useTheme();
  const { control, handleSubmit, watch, formState } = useForm<AdminLoginForm>({
    defaultValues: { email: '' },
    mode: 'onChange',
  });

  // The magic-link token carried by a followed email link. The admin bundle has no
  // router, so read it straight from the query. Present => auto-verify on mount.
  const urlToken = new URLSearchParams(window.location.search).get('token');
  // Guards the one-time URL-token verify against StrictMode's dev double-invoke.
  const verifiedFromUrl = useRef(false);

  const [phase, setPhase] = useState<Phase>('form');
  // The friendly message from the latest step (neutral confirmation or an outcome).
  const [message, setMessage] = useState<string>('');
  // The signed-in operator email, shown on the success state.
  const [operatorEmail, setOperatorEmail] = useState<string | null>(null);
  // DEV ONLY: the echoed magic-link token, enabling the local "Continue" affordance.
  const [devToken, setDevToken] = useState<string | null>(null);
  // True while a verify is in flight - initialized true when we arrive with a URL
  // token so the first paint shows "signing you in", not a flash of the email form.
  const [verifying, setVerifying] = useState<boolean>(Boolean(urlToken));

  const email = watch('email');
  const canSubmit = !formState.isSubmitting && EMAIL_PATTERN.test(email.trim());

  const onSubmit = handleSubmit(async (values) => {
    const result = await requestOperatorLink(values.email.trim());
    setMessage(result.message);
    setDevToken(result.devToken ?? null);
    // Always advance to the neutral "check your inbox" state on an accepted request
    // (AC-02: no operator-status tell). A transport failure keeps us on the form
    // with the friendly message shown inline.
    setPhase(result.ok ? 'sent' : 'form');
  });

  // Complete login from a magic-link token (the ONE verify path, shared by the
  // followed-email-link flow below and the Development dev-token button). On an
  // allowlisted 'signed-in' outcome the server set the operator session cookie, so
  // notify the shell to re-check and switch to the console.
  const verifyToken = async (token: string) => {
    setVerifying(true);
    let signedIn = false;
    try {
      const result = await verifyOperatorLink(token);
      setMessage(result.message);
      setOperatorEmail(result.email ?? null);
      setPhase(result.outcome);
      signedIn = result.outcome === 'signed-in';
    } finally {
      setVerifying(false);
    }
    // Notify the shell LAST, after this component's own state is settled: it may
    // unmount this component to switch to the console, so nothing must setState after.
    if (signedIn) {
      onAuthenticated?.();
    }
  };

  // DEPLOYED path: an operator who followed the emailed link lands here with a
  // `?token=` in the URL. Verify it once on mount, then strip the one-time token
  // from the address bar. The dev-token echo below is the local-only equivalent.
  useEffect(() => {
    if (verifiedFromUrl.current || !urlToken) return;
    verifiedFromUrl.current = true;
    const params = new URLSearchParams(window.location.search);
    params.delete('token');
    const query = params.toString();
    window.history.replaceState(
      null,
      '',
      `${window.location.pathname}${query ? `?${query}` : ''}${window.location.hash}`,
    );
    void verifyToken(urlToken);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlToken]);

  // DEV walkability only: follow the echoed token to complete login locally.
  const handleContinueWithDevToken = async () => {
    if (!devToken || verifying) return;
    await verifyToken(devToken);
  };

  const resetToForm = () => {
    setPhase('form');
    setMessage('');
    setDevToken(null);
    setOperatorEmail(null);
  };

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <Stack spacing={4} sx={{ px: 5.5, pt: 6, pb: 6 }}>
        {/* Header: reads as a restricted operator area, not the kid app. */}
        <Stack spacing={1.5} alignItems="center" sx={{ textAlign: 'center' }}>
          <Box
            aria-hidden
            sx={{
              width: 56,
              height: 56,
              borderRadius: '50%',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              bgcolor: alpha(theme.palette.primary.main, 0.12),
              color: 'primary.main',
              fontSize: 22,
            }}
          >
            <FontAwesomeIcon icon="lock" />
          </Box>
          <Typography sx={{ fontWeight: 800, fontSize: 20, color: 'text.primary' }}>
            Operator console
          </Typography>
          <Typography sx={{ fontWeight: 600, fontSize: 14, color: 'text.secondary' }}>
            Restricted back office - operators only.
          </Typography>
        </Stack>

        {phase === 'form' && verifying && (
          <Stack spacing={2} alignItems="center" sx={{ py: 4 }}>
            <CircularProgress color="primary" />
            <Typography sx={{ fontWeight: 700, fontSize: 14, color: 'text.secondary' }}>
              Signing you in...
            </Typography>
          </Stack>
        )}

        {phase === 'form' && !verifying && (
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
                    label="Operator email"
                    placeholder="you@quibblestone.com"
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
        )}

        {phase === 'sent' && (
          <OutcomePanel
            icon="envelope"
            tint={theme.palette.primary.main}
            title="Check your inbox"
            body={message}
          >
            {/* DEV ONLY: walk the flow locally without an email provider. */}
            {devToken && (
              <Button
                variant="outlined"
                fullWidth
                onClick={() => void handleContinueWithDevToken()}
                disabled={verifying}
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
            {operatorEmail && (
              <Typography sx={{ fontSize: 13.5, fontWeight: 800, color: 'text.primary' }}>
                {operatorEmail}
              </Typography>
            )}
          </OutcomePanel>
        )}

        {(phase === 'not-authorized' || phase === 'link-invalid' || phase === 'error') && (
          <OutcomePanel
            icon="triangle-exclamation"
            tint={theme.palette.gold.main}
            title={phase === 'not-authorized' ? 'Not authorized' : 'That link did not work'}
            body={message}
          >
            <Button variant="outlined" fullWidth onClick={resetToForm}>
              Try again
            </Button>
          </OutcomePanel>
        )}
      </Stack>
    </Box>
  );
}
