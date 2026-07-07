// ----------------------------------------------------------------------------
//  ErrorBoundary - the app's LAST-RESORT render-error safety net (B5, alpha-
//  gate hardening pre-friends-and-family audit).
//
//  React error boundaries can only be class components - `componentDidCatch` /
//  `static getDerivedStateFromError` have no Hook equivalent - so this is the
//  ONE class component in the app, and it exists for exactly that reason.
//  Without it, ANY uncaught exception during render anywhere under `<App/>`
//  unmounts the WHOLE tree to a permanent blank page: the anonymous beacon
//  (../telemetry/errorBeacon.ts) still fires for a window 'error' /
//  'unhandledrejection', but React catches a render-thrown error BEFORE it
//  ever reaches those listeners, so the beacon alone left the player staring
//  at nothing actionable.
//
//  Only `getDerivedStateFromError` is implemented - that alone is enough for
//  React to treat this as a valid error boundary and swap in the fallback
//  below. `componentDidCatch` (the usual place to LOG a caught error) is
//  deliberately omitted: this codebase has no console.* anywhere (the same
//  no-console posture CloudGallery.tsx's header calls out), and there is no
//  second reporting channel to wire up here without inventing one - the
//  window-level beacon already covers what it is scoped to cover.
//
//  On catch, this renders a minimal, playful "Something went off" screen with
//  one action: reload the page (window.location.reload()). This is a safety
//  net, not a new feature surface, so it stays deliberately small - no retry-
//  in-place, no error detail shown to the player. Styled via the MUI theme
//  (no hardcoded colors/spacing) with a FontAwesome icon, matching every
//  other shared component in this folder.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { Component, type ReactNode } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { Box, Button, Stack, Typography } from '@mui/material';

export interface ErrorBoundaryProps {
  /** The tree to render normally; swapped for the fallback screen once it throws. */
  children: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
}

/**
 * Wraps `<App/>` in main.tsx. A class component is REQUIRED here - see the
 * file header for why a functional component cannot fill this role.
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false };

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { hasError: true };
  }

  private handleReload = (): void => {
    window.location.reload();
  };

  render(): ReactNode {
    if (!this.state.hasError) {
      return this.props.children;
    }

    return (
      <Box
        sx={{
          minHeight: '100dvh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          px: 5.5,
        }}
      >
        <Stack spacing={2} alignItems="center" sx={{ textAlign: 'center', maxWidth: 340 }}>
          <Box aria-hidden sx={{ color: 'coral.main', fontSize: 34, display: 'flex' }}>
            <FontAwesomeIcon icon="triangle-exclamation" />
          </Box>
          <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 20 }}>
            Something went off
          </Typography>
          <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.secondary' }}>
            Give it a fresh start and you should be back in business.
          </Typography>
          <Button
            variant="contained"
            onClick={this.handleReload}
            startIcon={<FontAwesomeIcon icon="arrow-rotate-right" />}
          >
            Reload
          </Button>
        </Stack>
      </Box>
    );
  }
}
