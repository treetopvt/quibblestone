// ----------------------------------------------------------------------------
//  Support - the tip jar, "Buy the Guardians a coffee" (billing-entitlements/02,
//  issue #71). A friendly, ungated way for a happy family to say thanks with a small
//  one-time donation. It grants NOTHING (story 02 AC-02 - entitlement-neutral), needs
//  NO sign-in or account (AC-03), and lives OUTSIDE the kid play-flow - reachable only
//  from a Home entry link (AC-01), never Join / Lobby / word entry / Reveal.
//
//  NO DARK PATTERNS (AC-04): a warm invitation and a gold Guardian thank-you - no
//  guilt prompts, no forced upsell, no countdown timers. Fully passive until tapped
//  (AC-06): nothing here nags or blocks free play.
//
//  CHILD SAFETY (AC-05): the optional "message to the Guardians" runs through the
//  server's ONE content-safety filter (via the tip endpoint) before it goes anywhere;
//  a blocked message shows a friendly note and does not proceed. QuibbleStone stores no
//  message itself.
//
//  CONFIG-OFF: when billing is not configured the tip shows a warm "not available yet"
//  state rather than erroring. The gold-Guardian thank-you renders on return from a
//  successful Stripe checkout (?tip=success).
//
//  Styling: theme tokens only; FontAwesome icons only; reuses the shared Guardian.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { Box, Button, Stack, TextField, Typography } from '@mui/material';
import { AppBar, Guardian } from '../components';
import { startTip } from '../billing/billingClient';

export interface SupportProps {
  /** Return to Home (the shared app-bar back action). */
  onBack: () => void;
}

/** A gentle cap on the optional message - a thank-you note, not an essay (server is authoritative on safety). */
const MAX_MESSAGE = 140;

export function Support({ onBack }: SupportProps) {
  const [searchParams] = useSearchParams();
  const tipState = searchParams.get('tip'); // "success" | "cancel" | null

  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState<string | null>(null);

  const onTip = async () => {
    setBusy(true);
    setNote(null);
    const result = await startTip(message.trim() || undefined);
    if (result.url) {
      window.location.href = result.url;
      return;
    }
    // Blocked message or billing off - a friendly note, never an error.
    setNote(result.message ?? 'Tips are not available just now - thank you for the thought!');
    setBusy(false);
  };

  // The warm gold-Guardian thank-you, shown on return from a successful tip.
  if (tipState === 'success') {
    return (
      <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
        <AppBar title="Thank you" leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onBack }} />
        <Stack spacing={3} alignItems="center" sx={{ px: 5.5, pt: 6, pb: 6, textAlign: 'center' }}>
          <Guardian variant="gold" crowned size={120} />
          <Typography sx={{ fontWeight: 800, fontSize: 20, color: 'text.primary' }}>
            The Guardians thank you!
          </Typography>
          <Typography sx={{ fontWeight: 600, fontSize: 14.5, color: 'text.secondary', maxWidth: 300 }}>
            Your kindness keeps the tablets carved and the games rolling. That is all it does - and it means a lot.
          </Typography>
          <Button variant="contained" onClick={onBack} startIcon={<FontAwesomeIcon icon="arrow-left" style={{ width: 16, height: 16 }} />}>
            Back to the fun
          </Button>
        </Stack>
      </Box>
    );
  }

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar title="Support us" leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onBack }} />

      <Stack spacing={4} sx={{ px: 5.5, pt: 3, pb: 6 }}>
        <Stack spacing={2} alignItems="center" sx={{ textAlign: 'center' }}>
          <Guardian variant="gold" size={84} />
          <Typography sx={{ fontWeight: 800, fontSize: 18, color: 'text.primary' }}>
            Buy the Guardians a coffee
          </Typography>
          <Typography sx={{ fontWeight: 600, fontSize: 14.5, color: 'text.secondary' }}>
            Enjoying QuibbleStone? A small one-time thank-you helps us keep it going. It unlocks nothing - just our
            gratitude.
          </Typography>
        </Stack>

        {tipState === 'cancel' && (
          <Typography sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
            No worries - nothing was charged. Thank you for the thought!
          </Typography>
        )}

        <TextField
          label="A message to the Guardians (optional)"
          value={message}
          onChange={(e) => setMessage(e.target.value.slice(0, MAX_MESSAGE))}
          fullWidth
          multiline
          minRows={2}
          inputProps={{ maxLength: MAX_MESSAGE }}
          placeholder="You all are the best!"
        />

        <Button
          variant="contained"
          color="secondary"
          fullWidth
          disabled={busy}
          onClick={() => void onTip()}
          startIcon={busy ? undefined : <FontAwesomeIcon icon="mug-hot" style={{ width: 18, height: 18 }} />}
        >
          {busy ? 'Opening...' : 'Buy the Guardians a coffee'}
        </Button>

        {note && (
          <Typography role="status" sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
            {note}
          </Typography>
        )}

        {/* Free-play reassurance (AC-06): tipping is pure goodwill, never required. */}
        <Stack direction="row" spacing={1.5} alignItems="center" justifyContent="center">
          <Box sx={{ color: 'teal.main', fontSize: 15, display: 'flex' }}>
            <FontAwesomeIcon icon="shield-heart" />
          </Box>
          <Typography sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary' }}>
            Playing is always free - no account needed
          </Typography>
        </Stack>
      </Stack>
    </Box>
  );
}
