// ----------------------------------------------------------------------------
//  ConsentBanner - the lightweight, one-time analytics-consent notice
//  (analytics/01, AC-05).
//
//  DEFERRED FOR THE INITIAL ROLLOUT: monitoring ships LIVE (consent defaults to
//  granted) and this banner is turned OFF via ANALYTICS_SHOW_CONSENT_BANNER in
//  ../telemetry/consent.ts, so nothing is shown to a player yet. It is built and
//  wired now so turning it on later is a one-line flag flip, not new work.
//
//  WHEN ENABLED it is deliberately light and unobtrusive (product direction): a
//  slim bottom bar (NOT a modal, NO full-screen backdrop, nothing blocking play),
//  shown ONCE - the moment a player taps "Got it" or "Opt out" (or dismisses) the
//  choice persists device-local, so it never appears again. Because the rollout
//  posture is opt-OUT (measured by default), the copy is a plain notice with an
//  easy opt-out, and dismissing keeps the default rather than nagging.
//
//  Styling is entirely theme-driven (web/src/theme.ts) - no hex/rgb literals or
//  raw-px colors - with a FontAwesome glyph and tappable targets (README section
//  10). It respects the iOS safe-area inset (the app uses viewport-fit=cover).
//
//  Self-gating: it reads shouldPromptConsent() ONCE on mount and renders nothing
//  when the banner is disabled, analytics is unconfigured, or a choice already
//  exists - so App can mount it unconditionally.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { Box, Button, Stack, Typography, useTheme } from '@mui/material';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { setAnalyticsConsent, shouldPromptConsent } from '../telemetry/analytics';

export function ConsentBanner() {
  const theme = useTheme();
  // Decide ONCE on mount whether to show (pure read of the flag + stored choice).
  // A choice made below re-persists, so this component never needs to re-query.
  const [open, setOpen] = useState<boolean>(() => shouldPromptConsent());

  if (!open) {
    return null;
  }

  // Any resolution persists the choice (so the banner never shows again) and hides
  // it. "Got it" / dismiss keeps the default (granted for the rollout); "Opt out"
  // stops all analytics for this device.
  const resolve = (granted: boolean) => {
    setAnalyticsConsent(granted);
    setOpen(false);
  };

  return (
    <Box
      role="region"
      aria-label="Analytics notice"
      sx={{
        position: 'fixed',
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: theme.zIndex.snackbar,
        display: 'flex',
        justifyContent: 'center',
        px: 3,
        // Sit just above the iOS home indicator (viewport-fit=cover safe area).
        pb: 'calc(env(safe-area-inset-bottom) + 12px)',
        pointerEvents: 'none',
      }}
    >
      <Stack
        direction="row"
        spacing={3}
        alignItems="center"
        sx={{
          pointerEvents: 'auto',
          width: '100%',
          maxWidth: 460,
          bgcolor: 'card.main',
          border: `1.5px solid ${theme.palette.stoneSlot.main}`,
          borderRadius: '16px',
          boxShadow: '0 10px 28px -12px rgba(43,38,34,.35)',
          px: 4,
          py: 3,
        }}
      >
        <Box
          aria-hidden
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0,
            color: theme.palette.teal.main,
          }}
        >
          <FontAwesomeIcon icon="shield-heart" fontSize={20} />
        </Box>

        <Typography
          variant="body2"
          sx={{ flexGrow: 1, minWidth: 0, color: 'text.secondary', fontSize: 13, lineHeight: 1.35 }}
        >
          We use privacy-friendly analytics to make QuibbleStone better. No names or
          typed words are ever collected.
        </Typography>

        <Stack direction="row" spacing={1} alignItems="center" sx={{ flexShrink: 0 }}>
          <Button
            onClick={() => resolve(false)}
            sx={{
              minHeight: 40,
              px: 2.5,
              fontSize: 14,
              fontWeight: 700,
              color: 'text.secondary',
              textTransform: 'none',
            }}
          >
            Opt out
          </Button>
          <Button
            onClick={() => resolve(true)}
            sx={{
              minHeight: 40,
              px: 3,
              fontSize: 15,
              fontWeight: 700,
              color: theme.palette.primary.main,
              textTransform: 'none',
            }}
          >
            Got it
          </Button>
        </Stack>
      </Stack>
    </Box>
  );
}
