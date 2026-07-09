// ----------------------------------------------------------------------------
//  VaultClaimPanel - the Gallery screen's claim + recovery section
//  (keepsake-vault/03, "Claim and recovery", issue #230, AC-02 + the story's
//  Web Technical Note). Factored out of Gallery.tsx to keep that file focused
//  on the tale list.
//
//  Three affordances, gated per the story:
//    - CLAIM: shown ONLY when signed into a family account (isSignedIn) AND
//      this device holds a vault id AND that vault is not yet claimed. A big
//      "Claim this vault" button calls claimVault, then shows the returned
//      code. This is a family/purchaser-facing surface only (mirrors the
//      auth-boundary invariant keepsake-gallery/05 established: the child-
//      facing reveal never touches a family credential) - it is never
//      rendered for a session with no family credential in scope.
//    - CODE DISPLAY: shown to ANY device holding a CLAIMED vault id (signed in
//      or not) - the live grouped claim code, a plain-English expiry hint, and
//      a "Regenerate code" (explicit revoke, AC-07) button.
//    - RECOVER: shown to ANY device (signed in or not, vault id or not) - a
//      labeled code field + "Recover" button that redeems a typed code onto
//      THIS device (redeemClaimCode mints a device vault id if this device has
//      none yet). On success the caller's `onRecovered` reloads the merged
//      tale list so recovered tales appear immediately.
//
//  The device's own vault id is never rendered anywhere in this component
//  (AC-06's no-PII / bearer-secret posture extends to this UI - the vault id
//  stays inside vaultClaimClient's X-Vault-Id header).
//
//  Styling: MUI theme tokens only (no hex/raw-px), FontAwesome icons only, big
//  tap targets - matches Gallery.tsx's existing card language (teal-bordered
//  rounded cards on a parchment background).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, TextField, Typography } from '@mui/material';
import {
  claimVault,
  getVaultClaim,
  redeemClaimCode,
  regenerateClaimCode,
  type VaultClaimCode,
  type VaultClaimStatus,
} from './vaultClaimClient';

export interface VaultClaimPanelProps {
  /** This device's stored vault id, or null when the device holds none (a read-only surface never mints one). */
  vaultId: string | null;
  /** True when a family account is currently signed in (usePurchaserSession().isSignedIn). */
  isSignedIn: boolean;
  /** The signed-in family credential, or null when signed out. Only ever sent by the claim call. */
  credential: string | null;
  /** Called after a successful code redemption, so the caller can reload the merged tale list. */
  onRecovered: () => void;
}

/** A short, friendly "works until ..." hint from an ISO expiry instant. Falls back to a plain phrase on an odd value. */
function formatExpiryHint(expiresUtc: string): string {
  try {
    const when = new Date(expiresUtc).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
    return `This code works until ${when}.`;
  } catch {
    return 'This code will refresh automatically after a while.';
  }
}

/** One card wrapper matching Gallery's existing teal-bordered card language. */
function PanelCard({ children }: { children: React.ReactNode }) {
  const theme = useTheme();
  return (
    <Stack
      spacing={2}
      sx={{
        p: 3,
        borderRadius: 3,
        bgcolor: 'background.paper',
        border: `2px solid ${alpha(theme.palette.teal.main, 0.24)}`,
      }}
    >
      {children}
    </Stack>
  );
}

export function VaultClaimPanel({ vaultId, isSignedIn, credential, onRecovered }: VaultClaimPanelProps) {
  const theme = useTheme();
  const [status, setStatus] = useState<VaultClaimStatus | null>(null);
  const [claiming, setClaiming] = useState(false);
  const [regenerating, setRegenerating] = useState(false);
  const [claimError, setClaimError] = useState<string | null>(null);

  const [recoveryCode, setRecoveryCode] = useState('');
  const [redeeming, setRedeeming] = useState(false);
  const [redeemMessage, setRedeemMessage] = useState<string | null>(null);

  // Learn this device's claim state (claimed? live code?) whenever it holds a
  // vault id. A device with no vault id yet has nothing to be claimed, so we
  // skip the call entirely rather than minting one just to check.
  useEffect(() => {
    let cancelled = false;
    if (vaultId === null) {
      setStatus(null);
      return;
    }
    void getVaultClaim(vaultId).then((result) => {
      if (!cancelled) setStatus(result);
    });
    return () => {
      cancelled = true;
    };
  }, [vaultId]);

  const handleClaim = async () => {
    if (vaultId === null || credential === null || claiming) return;
    setClaiming(true);
    setClaimError(null);
    try {
      const code = await claimVault(vaultId, credential);
      if (code === null) {
        setClaimError("That didn't work - please try again.");
        return;
      }
      setStatus({ claimed: true, code });
    } finally {
      setClaiming(false);
    }
  };

  const handleRegenerate = async () => {
    if (vaultId === null || regenerating) return;
    setRegenerating(true);
    setClaimError(null);
    try {
      const code = await regenerateClaimCode(vaultId);
      if (code === null) {
        setClaimError("That didn't work - please try again.");
        return;
      }
      setStatus({ claimed: true, code });
    } finally {
      setRegenerating(false);
    }
  };

  const handleRecover = async () => {
    const code = recoveryCode.trim();
    if (code.length === 0 || redeeming) return;
    setRedeeming(true);
    setRedeemMessage(null);
    try {
      const redeemed = await redeemClaimCode(code);
      if (redeemed) {
        setRedeemMessage('Recovered! Your tales should appear below.');
        setRecoveryCode('');
        onRecovered();
      } else {
        setRedeemMessage("That code didn't work - please check it and try again.");
      }
    } finally {
      setRedeeming(false);
    }
  };

  // Show the claim CTA whenever the vault is not KNOWN to be claimed - including when
  // the status probe failed (status === null): a network hiccup on getVaultClaim must
  // not hide the CTA from a signed-in family, since claiming can still succeed (and
  // re-claiming an already-claimed vault just rotates the code, harmlessly).
  const showClaimCta = isSignedIn && vaultId !== null && status?.claimed !== true;
  // Guard rather than assert (TS-strict convention): narrow through the claimed flag
  // so `code` is a real VaultClaimCode | null without a non-null `!` or a cast.
  const code: VaultClaimCode | null =
    vaultId !== null && status?.claimed === true ? status.code : null;
  const showCode = code !== null;

  return (
    <Stack spacing={2.5}>
      {showClaimCta && (
        <PanelCard>
          <Stack direction="row" spacing={1.5} alignItems="center">
            <FontAwesomeIcon icon="key" style={{ width: 18, height: 18, color: theme.palette.primary.main }} />
            <Typography sx={{ fontWeight: 800, fontSize: 15, color: 'text.primary' }}>
              Save this vault to your family account
            </Typography>
          </Stack>
          <Typography sx={{ fontSize: 13.5, fontWeight: 600, color: 'text.secondary' }}>
            Claiming makes every tale in this vault permanent and visible from any device signed into your family
            account.
          </Typography>
          <Button
            variant="contained"
            fullWidth
            disabled={claiming}
            aria-busy={claiming}
            onClick={() => void handleClaim()}
            startIcon={<FontAwesomeIcon icon="key" style={{ width: 18, height: 18 }} />}
          >
            {claiming ? 'Claiming...' : 'Claim this vault'}
          </Button>
          {claimError && (
            <Typography role="status" sx={{ fontSize: 12.5, fontWeight: 700, color: 'error.main', textAlign: 'center' }}>
              {claimError}
            </Typography>
          )}
        </PanelCard>
      )}

      {showCode && code && (
        <PanelCard>
          <Stack direction="row" spacing={1.5} alignItems="center">
            <FontAwesomeIcon icon="key" style={{ width: 18, height: 18, color: theme.palette.primary.main }} />
            <Typography sx={{ fontWeight: 800, fontSize: 15, color: 'text.primary' }}>Your recovery code</Typography>
          </Stack>
          <Box
            sx={{
              px: 2.5,
              py: 2,
              borderRadius: 2,
              textAlign: 'center',
              bgcolor: alpha(theme.palette.primary.main, 0.08),
              fontFamily: '"Fredoka", sans-serif',
              fontWeight: 700,
              fontSize: 24,
              letterSpacing: 2,
              color: 'primary.main',
            }}
          >
            {code.claimCode}
          </Box>
          <Typography sx={{ fontSize: 12.5, fontWeight: 600, color: 'text.secondary', textAlign: 'center' }}>
            {formatExpiryHint(code.claimCodeExpiresUtc)} Enter it on a new device to bring these tales along - no
            account needed there.
          </Typography>
          <Button
            variant="outlined"
            fullWidth
            disabled={regenerating}
            aria-busy={regenerating}
            onClick={() => void handleRegenerate()}
            startIcon={<FontAwesomeIcon icon="arrow-rotate-right" style={{ width: 18, height: 18 }} />}
          >
            {regenerating ? 'Regenerating...' : 'Regenerate code'}
          </Button>
          {claimError && (
            <Typography role="status" sx={{ fontSize: 12.5, fontWeight: 700, color: 'error.main', textAlign: 'center' }}>
              {claimError}
            </Typography>
          )}
        </PanelCard>
      )}

      <PanelCard>
        <Stack direction="row" spacing={1.5} alignItems="center">
          <FontAwesomeIcon icon="unlock" style={{ width: 18, height: 18, color: theme.palette.primary.main }} />
          <Typography sx={{ fontWeight: 800, fontSize: 15, color: 'text.primary' }}>
            Recover tales from another device
          </Typography>
        </Stack>
        <Typography sx={{ fontSize: 13.5, fontWeight: 600, color: 'text.secondary' }}>
          Got a recovery code from another device? Enter it here to bring its tales to this one.
        </Typography>
        <TextField
          label="Enter a recovery code"
          value={recoveryCode}
          onChange={(event) => setRecoveryCode(event.target.value)}
          fullWidth
          placeholder="K5Q-2NX-8CP"
          inputProps={{ autoCapitalize: 'characters', autoCorrect: 'off', spellCheck: false }}
        />
        <Button
          variant="contained"
          fullWidth
          disabled={redeeming || recoveryCode.trim().length === 0}
          aria-busy={redeeming}
          onClick={() => void handleRecover()}
          startIcon={<FontAwesomeIcon icon="unlock" style={{ width: 18, height: 18 }} />}
        >
          {redeeming ? 'Recovering...' : 'Recover'}
        </Button>
        {redeemMessage && (
          <Typography role="status" sx={{ fontSize: 12.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
            {redeemMessage}
          </Typography>
        )}
      </PanelCard>
    </Stack>
  );
}
