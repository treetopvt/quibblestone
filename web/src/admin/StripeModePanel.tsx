// ----------------------------------------------------------------------------
//  StripeModePanel - the OPERATOR-CONSOLE screen for viewing and switching which
//  Stripe mode (Test or Live) is currently active (sysadmin-console/04, one console,
//  one auth). This is the RE-HOMED successor to the deleted kid-bundle
//  AdminBillingMode.tsx: same job, now living NATIVELY in the separate admin bundle
//  behind the real "Operator" policy, authenticated the SAME way every other admin
//  screen is (the in-memory operator credential, presented as a bearer by
//  stripeModeClient) - never a re-entered shared secret.
//
//  SEPARATE ADMIN BUNDLE / NO KID-APP EDGE (from story 01): this file lives in the
//  admin bundle and imports NOTHING from the kid app (pages / signalr / gallery /
//  engine / components). It opens NO SignalR connection. It shares ONLY the MUI theme
//  (via main.tsx's ThemeProvider - the ONE visual language, explicitly allowed for
//  this bundle) and its own FontAwesome registration. It does NOT reintroduce
//  AdminBillingMode's separate adminTheme nesting: that theme was a workaround for
//  living in the kid bundle's route tree and is no longer needed now that the panel
//  is native to the already-separate admin bundle.
//
//  OPERATOR-ONLY DATA, NO PII (AC-06): the panel displays ONLY the active mode and its
//  last-changed timestamp - no player, room, session, or purchaser data of any kind.
//
//  ASYMMETRIC FRICTION (AC-05): switching mode always goes through a confirmation
//  dialog naming both the current and target mode. Switching TO Live carries a
//  materially stronger warning (real cards will be charged) than switching to Test -
//  "go live" is the deliberate direction, never the accidental one. This is
//  AdminBillingMode's proven ConfirmSwitchDialog behavior, ported forward, not diluted.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import {
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  Typography,
} from '@mui/material';
import {
  fetchStripeMode,
  setStripeMode,
  type StripeMode,
  type StripeModeStatusResult,
} from './stripeModeClient';

/** Props for {@link StripeModePanel}. */
interface StripeModePanelProps {
  /** The signed-in operator email (from the session check), shown in the header. */
  operatorEmail: string;
  /**
   * The operator credential, presented as a bearer on every admin call (the cross-
   * origin path). Null on a same-site deployment, where the cookie carries the session.
   */
  credential: string | null;
}

/** Friendly label for a mode value - never render the raw 'test' | 'live' string to the operator. */
function modeLabel(mode: StripeMode): string {
  return mode === 'live' ? 'Live' : 'Test';
}

/** A calm, readable rendering of an ISO timestamp for the "last changed" display. */
function formatChangedAt(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleString();
}

/**
 * The confirmation dialog (AC-05): always names both the current and target mode
 * explicitly, and shows a materially stronger warning when the target is Live -
 * never equal friction both ways. Ported from AdminBillingMode's ConfirmSwitchDialog,
 * now reading the SHARED theme (coral for the go-live warning, matching the console).
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
      <DialogTitle sx={{ fontWeight: 800, fontSize: 20 }}>
        Switch from {modeLabel(currentMode)} to {modeLabel(targetMode)}?
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          {goingLive ? (
            // AC-05: the stronger, explicit warning for the deliberate "go live" direction.
            <Stack
              direction="row"
              spacing={1.5}
              sx={{
                p: 2,
                borderRadius: '16px',
                bgcolor: alpha(theme.palette.coral.main, 0.12),
                border: `1px solid ${alpha(theme.palette.coral.main, 0.35)}`,
                alignItems: 'flex-start',
              }}
            >
              <Box sx={{ color: 'coral.main', fontSize: 18, display: 'flex', mt: 0.25 }}>
                <FontAwesomeIcon icon="triangle-exclamation" />
              </Box>
              <Typography sx={{ fontSize: 14, fontWeight: 700, color: 'text.primary' }}>
                Real cards will be charged. Every checkout from this moment uses live Stripe
                credentials - this is not a drill.
              </Typography>
            </Stack>
          ) : (
            <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary' }}>
              Test mode uses Stripe&apos;s test credentials - no real card is ever charged.
            </Typography>
          )}
          <Typography sx={{ fontSize: 13.5, fontWeight: 600, color: 'text.secondary' }}>
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

export function StripeModePanel({ operatorEmail, credential }: StripeModePanelProps) {
  const theme = useTheme();

  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<StripeModeStatusResult | null>(null);

  // The confirmation dialog's target mode, or null when closed.
  const [confirmTarget, setConfirmTarget] = useState<StripeMode | null>(null);
  const [switching, setSwitching] = useState(false);
  const [switchError, setSwitchError] = useState<string | null>(null);

  // Load the current mode on mount, presenting the operator credential (no secret
  // prompt: the operator is already signed in when this panel renders).
  const loadStatus = useCallback(async () => {
    setLoading(true);
    const result = await fetchStripeMode(credential);
    setStatus(result);
    setLoading(false);
  }, [credential]);

  useEffect(() => {
    void loadStatus();
  }, [loadStatus]);

  const onConfirmSwitch = useCallback(async () => {
    if (!confirmTarget) return;
    setSwitching(true);
    setSwitchError(null);
    const result = await setStripeMode(credential, confirmTarget);
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
  }, [confirmTarget, credential, status]);

  return (
    <Box sx={{ maxWidth: 620, mx: 'auto', width: '100%', px: { xs: 2, md: 3 }, py: { xs: 3, md: 4 } }}>
      <Stack spacing={3.5}>
        <Stack spacing={0.5}>
          <Typography component="h2" sx={{ fontWeight: 800, fontSize: 24, color: 'text.primary' }}>
            Stripe mode
          </Typography>
          <Typography sx={{ fontWeight: 600, fontSize: 13.5, color: 'text.secondary' }}>
            Signed in as {operatorEmail}
          </Typography>
        </Stack>

        {loading && (
          <Stack alignItems="center" sx={{ py: 4 }}>
            <CircularProgress color="primary" />
          </Stack>
        )}

        {/* A failed read is an explicit error state - never a blank or a guessed mode. */}
        {!loading && status?.outcome !== 'ok' && (
          <Stack spacing={2} alignItems="flex-start">
            <Box sx={{ color: 'coral.main', fontSize: 26, display: 'flex' }}>
              <FontAwesomeIcon icon="triangle-exclamation" />
            </Box>
            <Typography sx={{ fontWeight: 800, fontSize: 16, color: 'text.primary' }}>
              {status?.outcome === 'unauthorized'
                ? 'Your operator session was not accepted'
                : 'Could not load the current mode'}
            </Typography>
            {status?.message && (
              <Typography sx={{ fontSize: 13.5, fontWeight: 600, color: 'text.secondary' }}>
                {status.message}
              </Typography>
            )}
            <Button variant="outlined" onClick={() => void loadStatus()}>
              Try again
            </Button>
          </Stack>
        )}

        {!loading && status?.outcome === 'ok' && status.activeMode && (
          <Stack spacing={3.5}>
            {/* The active mode, displayed prominently and unambiguously. */}
            <Box
              sx={{
                p: 3,
                borderRadius: '24px',
                bgcolor: 'card.main',
                boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
              }}
            >
              <Stack direction="row" spacing={1.5} alignItems="center">
                <Box sx={{ color: 'text.secondary', fontSize: 18, display: 'flex' }}>
                  <FontAwesomeIcon icon="credit-card" />
                </Box>
                <Typography sx={{ fontSize: 12, fontWeight: 800, color: 'text.secondary', letterSpacing: 1 }}>
                  CURRENTLY ACTIVE
                </Typography>
              </Stack>
              <Typography
                sx={{
                  fontWeight: 800,
                  fontSize: 34,
                  mt: 0.5,
                  color: status.activeMode === 'live' ? 'coral.main' : 'success.main',
                }}
              >
                {modeLabel(status.activeMode)}
              </Typography>
              <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.secondary', mt: 0.5 }}>
                {status.lastChangedUtc
                  ? `Last changed ${formatChangedAt(status.lastChangedUtc)}`
                  : 'Never changed since this environment was set up'}
              </Typography>
              {status.enabled === false && (
                <Typography sx={{ fontSize: 12.5, fontWeight: 700, color: 'text.secondary', mt: 1 }}>
                  Billing is not configured server-side yet.
                </Typography>
              )}
            </Box>

            {/* Initiating a switch always opens the confirmation dialog - there is no
                single-click, no-confirmation path (AC-05). */}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <Button
                variant="outlined"
                fullWidth
                disabled={status.activeMode === 'test'}
                onClick={() => setConfirmTarget('test')}
              >
                Switch to Test
              </Button>
              <Button
                variant="contained"
                color="error"
                fullWidth
                disabled={status.activeMode === 'live'}
                onClick={() => setConfirmTarget('live')}
              >
                Switch to Live
              </Button>
            </Stack>
            {switchError && (
              <Typography role="status" sx={{ fontSize: 13, fontWeight: 700, color: 'coral.main' }}>
                {switchError}
              </Typography>
            )}
          </Stack>
        )}
      </Stack>

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
