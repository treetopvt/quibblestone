// ----------------------------------------------------------------------------
//  AdminBillingMode - the OPERATOR-ONLY screen for viewing and switching which
//  Stripe mode (Test or Live) is currently active (billing-entitlements/07,
//  the UI half of story 06's server-side toggle). This is NOT a player-facing
//  surface: it shows only the active mode value and when it last changed - no
//  player, room, session, or purchaser data of any kind (AC-07).
//
//  TEMPORARY INTERIM GATE (pending sysadmin-console/01 / #135): there is no
//  operator login yet. Story 06 documents a thin server-side shared secret
//  required as the `X-Operator-Secret` header on both admin endpoints. This
//  screen asks the operator to type that secret into a password field FIRST -
//  nothing is fetched until they submit it - and holds it ONLY in component
//  state for the lifetime of this screen. It is never written to localStorage,
//  a cookie, or a `VITE_*` var (CLAUDE.md section 4), so it does not survive a
//  refresh or navigation away; the operator re-enters it next visit. Once the
//  real operator-auth boundary (#135) ships, this secret prompt is replaced by
//  the real operator session and the API calls swap their auth header - a
//  relocation into the real back office, not a rewrite (per story 06/07's
//  Technical Notes).
//
//  WHERE IT LIVES (AC-05, NON-NEGOTIABLE): reachable ONLY via the dedicated
//  '/admin/billing-mode' route, wired directly in App.tsx alongside the
//  player-facing routes but with NO link to it anywhere in the app - not from
//  Home, Join, Lobby, FillBlank, Reveal, or any other player-facing screen. A
//  person reaches it only by knowing the URL directly.
//
//  ASYMMETRIC FRICTION (AC-02/AC-03): switching mode always goes through a
//  confirmation dialog naming both the current and target mode. Switching TO
//  Live carries a materially stronger warning (real cards will be charged)
//  than switching to Test - "go live" is the deliberate direction, never the
//  accidental one.
//
//  STYLING - DELIBERATELY DECOUPLED FROM THE PLAYER APP (operator direction,
//  2026-07): this is a DESKTOP-FIRST operator console, not a family word game,
//  so it nests its OWN MUI theme (../admin/adminTheme) rather than the bright,
//  chunky, parchment-warm player theme. That neutral theme owns the palette
//  (slate/blue, standard error/success/warning), a system font stack, tighter
//  radii and flat buttons; this screen only uses standard MUI slots (no
//  parchment / gold / teal / coral / stoneEdge / tablet / card tokens). It also
//  drops the shared player AppBar for a plain operator header. Its own
//  full-viewport background paints over the app's parchment body. The two design
//  languages now evolve independently - a change here can never regress a player
//  screen, and vice versa. (This reverses the story-06 note that mandated
//  player-theme reuse; see adminTheme's header for the rationale.)
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useState, type FormEvent } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme, ThemeProvider } from '@mui/material/styles';
import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { adminTheme } from '../admin/adminTheme';
import {
  fetchStripeMode,
  setStripeMode,
  type StripeMode,
  type StripeModeStatusResult,
} from '../billing/stripeModeClient';

export interface AdminBillingModeProps {
  /** Return wherever the operator came from (the plain operator header back action). */
  onBack: () => void;
}

/** Friendly label for a mode value - never render the raw 'test' | 'live' string to the operator. */
function modeLabel(mode: StripeMode): string {
  return mode === 'live' ? 'Live' : 'Test';
}

/** A calm, readable rendering of an ISO timestamp for AC-04's "last changed" display. */
function formatChangedAt(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleString();
}

/**
 * The confirmation dialog (AC-02/AC-03): always names both the current and
 * target mode explicitly, and shows a materially stronger warning when the
 * target is Live - never equal friction both ways. Rendered inside the admin
 * ThemeProvider, so it reads from adminTheme (standard error/warning slots), not
 * the player palette.
 */
interface ConfirmSwitchDialogProps {
  open: boolean;
  currentMode: StripeMode;
  targetMode: StripeMode;
  busy: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}

function ConfirmSwitchDialog({ open, currentMode, targetMode, busy, onCancel, onConfirm }: ConfirmSwitchDialogProps) {
  const theme = useTheme();
  const goingLive = targetMode === 'live';

  return (
    <Dialog open={open} onClose={busy ? undefined : onCancel} maxWidth="xs" fullWidth>
      <DialogTitle sx={{ fontWeight: 700, fontSize: 20 }}>
        Switch from {modeLabel(currentMode)} to {modeLabel(targetMode)}?
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          {goingLive ? (
            // AC-03: the stronger, explicit warning for the deliberate "go live" direction.
            <Stack
              direction="row"
              spacing={1.5}
              sx={{
                p: 2,
                borderRadius: 1,
                bgcolor: alpha(theme.palette.error.main, 0.1),
                border: `1px solid ${alpha(theme.palette.error.main, 0.3)}`,
                alignItems: 'flex-start',
              }}
            >
              <Box sx={{ color: 'error.main', fontSize: 18, display: 'flex', mt: 0.25 }}>
                <FontAwesomeIcon icon="circle-info" />
              </Box>
              <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.primary' }}>
                Real cards will be charged. Every checkout from this moment uses live Stripe
                credentials - this is not a drill.
              </Typography>
            </Stack>
          ) : (
            <Typography sx={{ fontSize: 14, fontWeight: 500, color: 'text.secondary' }}>
              Test mode uses Stripe&apos;s test credentials - no real card is ever charged.
            </Typography>
          )}
          <Typography sx={{ fontSize: 13.5, fontWeight: 500, color: 'text.secondary' }}>
            This affects the whole app immediately - every player on quibblestone.com.
          </Typography>
        </Stack>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 3, gap: 1.5 }}>
        <Button variant="outlined" onClick={onCancel} disabled={busy} fullWidth>
          Cancel
        </Button>
        <Button
          variant="contained"
          color={goingLive ? 'error' : 'primary'}
          onClick={onConfirm}
          disabled={busy}
          fullWidth
        >
          {busy ? 'Switching...' : `Yes, go ${modeLabel(targetMode)}`}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/**
 * The operator console body. Split out from the exported wrapper so it renders
 * INSIDE the admin ThemeProvider (below), meaning every `useTheme()` here - and
 * in ConfirmSwitchDialog - resolves to adminTheme, not the player theme.
 */
function AdminBillingModeInner({ onBack }: AdminBillingModeProps) {
  const theme = useTheme();

  // The operator secret: held ONLY in this component's memory for the session,
  // typed fresh each visit (never persisted - see header comment).
  const [secret, setSecret] = useState('');
  const [secretSubmitted, setSecretSubmitted] = useState(false);

  const [loading, setLoading] = useState(false);
  const [status, setStatus] = useState<StripeModeStatusResult | null>(null);

  // The confirmation dialog's target mode, or null when closed.
  const [confirmTarget, setConfirmTarget] = useState<StripeMode | null>(null);
  const [switching, setSwitching] = useState(false);
  const [switchError, setSwitchError] = useState<string | null>(null);

  const loadStatus = useCallback(async (secretToUse: string) => {
    setLoading(true);
    const result = await fetchStripeMode(secretToUse);
    setStatus(result);
    setLoading(false);
  }, []);

  const onSubmitSecret = (event: FormEvent) => {
    event.preventDefault();
    if (!secret.trim()) return;
    setSecretSubmitted(true);
    void loadStatus(secret);
  };

  const onConfirmSwitch = useCallback(async () => {
    if (!confirmTarget) return;
    setSwitching(true);
    setSwitchError(null);
    const result = await setStripeMode(secret, confirmTarget);
    setSwitching(false);
    // Success is a confirmed flip (outcome ok + a mode) - do NOT require a timestamp: the
    // mode DID change server-side, so a missing/unexpected timestamp must not read as a
    // failure and prompt a confused operator to flip again on this footgun screen.
    if (result.outcome === 'ok' && result.activeMode) {
      setStatus({
        outcome: 'ok',
        activeMode: result.activeMode,
        lastChangedUtc: result.lastChangedUtc ?? null,
        enabled: status?.enabled ?? true,
      });
      setConfirmTarget(null);
    } else {
      setSwitchError(result.message ?? 'The switch did not go through - please try again.');
    }
  }, [confirmTarget, secret, status]);

  return (
    <Box sx={{ minHeight: '100dvh', bgcolor: 'background.default', color: 'text.primary' }}>
      {/* Plain operator header (replaces the playful player AppBar). */}
      <Box
        component="header"
        sx={{
          display: 'flex',
          alignItems: 'center',
          gap: 2,
          px: { xs: 2, md: 4 },
          py: 1.5,
          bgcolor: 'background.paper',
          borderBottom: `1px solid ${theme.palette.divider}`,
        }}
      >
        <Button
          onClick={onBack}
          startIcon={<FontAwesomeIcon icon="arrow-left" />}
          sx={{ color: 'text.secondary' }}
        >
          Back
        </Button>
        <Typography component="h1" sx={{ fontWeight: 700, fontSize: 16 }}>
          Billing mode
        </Typography>
        <Box sx={{ flexGrow: 1 }} />
        <Typography sx={{ fontSize: 12, fontWeight: 600, color: 'text.secondary', letterSpacing: 0.5 }}>
          OPERATOR CONSOLE
        </Typography>
      </Box>

      {/* Desktop-first content column: centered, roomy, capped for readability. */}
      <Box sx={{ maxWidth: 760, mx: 'auto', px: { xs: 2, md: 4 }, py: { xs: 3, md: 5 } }}>
        <Stack spacing={4}>
          {/* Marked plainly as a temporary operator surface (interim gate). */}
          <Stack
            direction="row"
            spacing={1.5}
            sx={{
              p: 2,
              borderRadius: 1,
              bgcolor: alpha(theme.palette.warning.main, 0.12),
              border: `1px solid ${alpha(theme.palette.warning.main, 0.3)}`,
              alignItems: 'flex-start',
            }}
          >
            <Box sx={{ color: 'warning.dark', fontSize: 16, display: 'flex', mt: 0.25 }}>
              <FontAwesomeIcon icon="gear" />
            </Box>
            <Typography sx={{ fontSize: 13, fontWeight: 500, color: 'text.secondary' }}>
              Operator-only, interim surface: a shared secret gates this screen until real operator
              sign-in ships. Nothing here is reachable from player screens.
            </Typography>
          </Stack>

          {!secretSubmitted && (
            <Box component="form" onSubmit={onSubmitSecret} noValidate>
              <Stack spacing={3} sx={{ maxWidth: 420 }}>
                <Stack spacing={1}>
                  <Typography sx={{ fontWeight: 700, fontSize: 18 }}>Enter the operator secret</Typography>
                  <Typography sx={{ fontWeight: 500, fontSize: 13.5, color: 'text.secondary' }}>
                    Nothing loads until you submit it. Not saved anywhere - you will enter it again
                    next visit.
                  </Typography>
                </Stack>
                <TextField
                  type="password"
                  fullWidth
                  label="Operator secret"
                  value={secret}
                  onChange={(event) => setSecret(event.target.value)}
                  autoComplete="off"
                />
                <Button type="submit" variant="contained" disabled={!secret.trim()} sx={{ alignSelf: 'flex-start' }}>
                  Continue
                </Button>
              </Stack>
            </Box>
          )}

          {secretSubmitted && loading && (
            <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary' }}>
              Loading the current mode...
            </Typography>
          )}

          {secretSubmitted && !loading && status?.outcome === 'unauthorized' && (
            <Stack spacing={2} alignItems="flex-start">
              <Box sx={{ color: 'error.main', fontSize: 26, display: 'flex' }}>
                <FontAwesomeIcon icon="circle-xmark" />
              </Box>
              <Typography sx={{ fontWeight: 700, fontSize: 16 }}>That secret was not accepted</Typography>
              <Button
                variant="outlined"
                onClick={() => {
                  setSecretSubmitted(false);
                  setStatus(null);
                }}
              >
                Try a different secret
              </Button>
            </Stack>
          )}

          {/* AC-01: a failed read is an explicit error state - never a blank or a
              guessed mode. */}
          {secretSubmitted && !loading && status?.outcome === 'error' && (
            <Stack spacing={2} alignItems="flex-start">
              <Box sx={{ color: 'error.main', fontSize: 26, display: 'flex' }}>
                <FontAwesomeIcon icon="circle-xmark" />
              </Box>
              <Typography sx={{ fontWeight: 700, fontSize: 16 }}>Could not load the current mode</Typography>
              <Typography sx={{ fontSize: 13.5, fontWeight: 500, color: 'text.secondary' }}>
                {status.message}
              </Typography>
              <Button variant="outlined" onClick={() => void loadStatus(secret)}>
                Try again
              </Button>
            </Stack>
          )}

          {secretSubmitted && !loading && status?.outcome === 'ok' && status.activeMode && (
            <Stack spacing={4}>
              {/* AC-01: the active mode, displayed prominently and unambiguously. */}
              <Box
                sx={{
                  p: 3,
                  borderRadius: 2,
                  bgcolor: 'background.paper',
                  border: `1px solid ${theme.palette.divider}`,
                  boxShadow: '0 1px 3px rgba(15, 23, 42, 0.08)',
                  maxWidth: 420,
                }}
              >
                <Typography sx={{ fontSize: 12, fontWeight: 700, color: 'text.secondary', letterSpacing: 1 }}>
                  CURRENTLY ACTIVE
                </Typography>
                <Typography
                  sx={{
                    fontWeight: 700,
                    fontSize: 34,
                    mt: 0.5,
                    color: status.activeMode === 'live' ? 'error.main' : 'success.main',
                  }}
                >
                  {modeLabel(status.activeMode)}
                </Typography>
                {/* AC-04: when the mode last changed. */}
                <Typography sx={{ fontSize: 13, fontWeight: 500, color: 'text.secondary', mt: 0.5 }}>
                  {status.lastChangedUtc
                    ? `Last changed ${formatChangedAt(status.lastChangedUtc)}`
                    : 'Never changed since this environment was set up'}
                </Typography>
                {status.enabled === false && (
                  <Typography sx={{ fontSize: 12.5, fontWeight: 600, color: 'text.secondary', mt: 1 }}>
                    Billing is not configured server-side yet.
                  </Typography>
                )}
              </Box>

              {/* AC-02: initiating a switch always opens the confirmation dialog -
                  there is no single-click, no-confirmation path. */}
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ maxWidth: 420 }}>
                <Button
                  variant="outlined"
                  disabled={status.activeMode === 'test'}
                  onClick={() => setConfirmTarget('test')}
                >
                  Switch to Test
                </Button>
                <Button
                  variant="contained"
                  color="error"
                  disabled={status.activeMode === 'live'}
                  onClick={() => setConfirmTarget('live')}
                >
                  Switch to Live
                </Button>
              </Stack>
              {switchError && (
                <Typography role="status" sx={{ fontSize: 13, fontWeight: 600, color: 'error.main' }}>
                  {switchError}
                </Typography>
              )}
            </Stack>
          )}
        </Stack>
      </Box>

      {status?.outcome === 'ok' && status.activeMode && confirmTarget && (
        <ConfirmSwitchDialog
          open
          currentMode={status.activeMode}
          targetMode={confirmTarget}
          busy={switching}
          onCancel={() => setConfirmTarget(null)}
          onConfirm={() => void onConfirmSwitch()}
        />
      )}
    </Box>
  );
}

/**
 * Exported wrapper: nests the operator-only adminTheme so this whole subtree -
 * including its Dialog portal - is decoupled from the player theme (see the file
 * header). Everything below reads adminTheme via useTheme().
 */
export function AdminBillingMode({ onBack }: AdminBillingModeProps) {
  return (
    <ThemeProvider theme={adminTheme}>
      <AdminBillingModeInner onBack={onBack} />
    </ThemeProvider>
  );
}
