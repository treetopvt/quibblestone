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

import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, CircularProgress, Stack, TextField, Typography } from '@mui/material';
import { AppBar } from '../components';
import {
  requestSignInLink,
  verifySignIn,
  type SignInOutcome,
} from '../account/signInClient';
import { fetchEntitlements, type OwnedEntitlement } from '../account/entitlementsClient';
import { usePurchaserSession } from '../account/PurchaserSession';
import { CloudGallery } from './CloudGallery';

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

/**
 * The restore/manage list (billing-entitlements/05): fetches the signed-in purchaser's
 * active entitlements with their credential and renders a plain-language list, a
 * friendly empty state (AC-03), or a "sign in again" note (AC-06 / expired). Read-only -
 * no plan management here. Shows what the purchaser OWNS, never a play history (AC-05).
 */
function PurchaseList({ credential }: { credential: string }) {
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<'ok' | 'signed-out' | 'error'>('ok');
  const [entitlements, setEntitlements] = useState<OwnedEntitlement[]>([]);

  useEffect(() => {
    let active = true;
    void fetchEntitlements(credential).then((result) => {
      if (!active) return;
      setStatus(result.status);
      setEntitlements(result.entitlements);
      setLoading(false);
    });
    return () => {
      active = false;
    };
  }, [credential]);

  if (loading) {
    return <CircularProgress size={22} sx={{ mt: 1 }} />;
  }

  if (status !== 'ok') {
    // Signed-out (expired credential) or a transport hiccup - a calm note, no data.
    return (
      <Typography sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
        {status === 'signed-out'
          ? 'Your sign-in expired - request a fresh link to see what is unlocked.'
          : 'We could not load your unlocks just now - please try again in a moment.'}
      </Typography>
    );
  }

  if (entitlements.length === 0) {
    // AC-03: friendly empty state - a tip-only or never-purchased account is not an error.
    return (
      <Typography sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
        Nothing unlocked yet - and free play is always on. When you buy something, it shows up here.
      </Typography>
    );
  }

  return (
    <Stack spacing={1.5} sx={{ width: '100%' }}>
      <Typography sx={{ fontSize: 13, fontWeight: 800, color: 'text.secondary', textAlign: 'center' }}>
        What you have unlocked
      </Typography>
      {entitlements.map((entitlement) => (
        <Stack
          key={entitlement.key}
          direction="row"
          spacing={1.5}
          alignItems="center"
          sx={{ px: 2.5, py: 1.5, borderRadius: '14px', bgcolor: 'background.default' }}
        >
          <Box sx={{ color: 'teal.main', fontSize: 16, display: 'flex' }}>
            <FontAwesomeIcon icon="circle-check" />
          </Box>
          <Typography sx={{ flex: 1, fontSize: 14, fontWeight: 800, color: 'text.primary' }}>
            {entitlement.label}
          </Typography>
          <Typography sx={{ fontSize: 12, fontWeight: 700, color: 'text.secondary' }}>
            {entitlement.source === 'Subscription' ? 'Active' : 'Unlocked'}
          </Typography>
        </Stack>
      ))}
    </Stack>
  );
}

export function Account({ onBack }: AccountProps) {
  const theme = useTheme();
  const { control, handleSubmit, watch, formState } = useForm<AccountForm>({
    defaultValues: { email: '' },
    mode: 'onChange',
  });

  // The signed-in purchaser credential + email now live in the app-wide, in-memory
  // PurchaserSession (../account/PurchaserSession), so sign-in survives navigation
  // between screens within a session - a return to Account no longer forces a fresh
  // sign-in. Still in-memory only (never persisted), and consumed only by this
  // purchaser surface (auth boundary).
  const session = usePurchaserSession();
  // The magic-link token carried by a followed email link (`/account?token=...`).
  // When present we auto-complete sign-in on mount (below) - this is the DEPLOYED
  // path (the dev-token echo only exists locally). Read from the router's query.
  const [searchParams, setSearchParams] = useSearchParams();
  const urlToken = searchParams.get('token');
  // Guards the one-time URL-token verify against StrictMode's dev double-invoke
  // (and the re-render after we strip the token from the URL).
  const verifiedFromUrl = useRef(false);
  // If already signed in this session, land straight on the signed-in state rather
  // than the email form (the fix for re-entering the email on every visit).
  const [phase, setPhase] = useState<Phase>(session.isSignedIn ? 'signed-in' : 'form');
  // The friendly message from the latest step (neutral confirmation or an outcome).
  const [message, setMessage] = useState<string>('');
  // DEV ONLY: the echoed magic-link token, enabling the local "Continue" affordance.
  const [devToken, setDevToken] = useState<string | null>(null);
  // True while a verify is in flight - initialized true when we arrive with a URL
  // token so the first paint shows "signing you in", not a flash of the email form.
  const [verifying, setVerifying] = useState<boolean>(Boolean(urlToken) && !session.isSignedIn);
  // keepsake-gallery/05: whether the signed-in purchaser has opened their cloud
  // gallery. Deliberately behind a tap (not shown by default) so opening it is a
  // purchaser-consented action, and so it is impossible for an anonymous player
  // to ever reach it (it only renders in the 'signed-in' phase, where a
  // credential exists - AC-02). The credential lives in this component's memory,
  // so the cloud gallery MUST render here, not on a separate route.
  const [cloudGalleryOpen, setCloudGalleryOpen] = useState(false);

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

  // Complete sign-in from a magic-link token (the ONE verify path, shared by the
  // followed-email-link flow below and the Development dev-token button).
  const verifyToken = async (token: string) => {
    setVerifying(true);
    try {
      const result = await verifySignIn(token);
      if (result.outcome === 'signed-in' && result.credential) {
        // Record the sign-in in the app-wide session so it survives navigation.
        session.signIn(result.credential, result.email ?? '');
        setMessage(result.message);
        setPhase('signed-in');
      } else if (result.outcome === 'signed-in') {
        // A 'signed-in' outcome with no credential is a malformed response - do NOT
        // enter the signed-in state without a session credential (Copilot review),
        // which would show "You're signed in" with no gallery and no way to act.
        setMessage('We could not complete sign-in just now - please request a fresh link and try again.');
        setPhase('error');
      } else {
        setMessage(result.message);
        setPhase(result.outcome);
      }
    } finally {
      setVerifying(false);
    }
  };

  // DEPLOYED path: a purchaser who followed the emailed link lands here with a
  // `?token=` in the URL. Verify it once on mount to complete sign-in, then strip
  // the one-time token from the address bar (so it is not left visible or re-run on
  // refresh). The dev-token echo below is the local-only equivalent.
  useEffect(() => {
    // Skip when already signed in this session, so landing with a stale token cannot
    // flip a signed-in purchaser to a "link invalid" state.
    if (verifiedFromUrl.current || !urlToken || session.isSignedIn) return;
    verifiedFromUrl.current = true;
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.delete('token');
        return next;
      },
      { replace: true },
    );
    void verifyToken(urlToken);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlToken]);

  // DEV walkability only: follow the echoed token to complete sign-in locally.
  const handleContinueWithDevToken = async () => {
    if (!devToken || verifying) return;
    await verifyToken(devToken);
  };

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar title="Account" leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onBack }} />

      <Stack spacing={4} sx={{ px: 5.5, pt: 3, pb: 6 }}>
        {phase === 'form' && verifying && (
          <Stack spacing={2} alignItems="center" sx={{ py: 6 }}>
            <CircularProgress />
            <Typography sx={{ fontWeight: 700, fontSize: 14, color: 'text.secondary' }}>
              Signing you in...
            </Typography>
          </Stack>
        )}

        {phase === 'form' && !verifying && (
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
            // On a fresh verify `message` carries the server's note; on a return to
            // this screen (already signed in, no new verify) fall back to a default.
            body={message || 'You are signed in - your saved tales can follow you across your devices.'}
          >
            {session.email && (
              <Typography sx={{ fontSize: 13.5, fontWeight: 800, color: 'text.primary' }}>
                {session.email}
              </Typography>
            )}
            {session.credential && <PurchaseList credential={session.credential} />}
            {/* keepsake-gallery/05: the cloud-gallery affordance lives HERE, in
                the signed-in state, where the purchaser `credential` is in scope
                (AC-02: anonymous players can never reach it). Behind a tap so
                opening it is an explicit purchaser action. */}
            {session.credential &&
              (cloudGalleryOpen ? (
                <CloudGallery credential={session.credential} />
              ) : (
                <Button
                  variant="outlined"
                  fullWidth
                  onClick={() => setCloudGalleryOpen(true)}
                  startIcon={<FontAwesomeIcon icon="images" style={{ width: 18, height: 18 }} />}
                >
                  Open my cloud gallery
                </Button>
              ))}
            <Button
              variant="text"
              onClick={() => {
                session.signOut();
                setCloudGalleryOpen(false);
                setDevToken(null);
                setMessage('');
                setPhase('form');
              }}
              sx={{ fontWeight: 800, fontSize: 13 }}
            >
              Sign out
            </Button>
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
