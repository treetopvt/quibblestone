// ----------------------------------------------------------------------------
//  admin/main.tsx - entry point for the SEPARATE operator back office
//  (sysadmin-console/01, issue #135; extended by sysadmin-console/03, issue #137).
//
//  This is a SECOND, independent Vite entry (web/admin.html -> here), NOT part of
//  the kid app's bundle (AC-04, load-bearing). It mounts the back office inside the
//  MUI ThemeProvider (the SHARED visual language, web/src/theme.ts - explicitly
//  allowed) with a CssBaseline reset, and registers the admin-only FontAwesome set.
//  It imports NOTHING from the kid app (pages / signalr / gallery / engine /
//  components) and opens NO SignalR connection - the back office has no realtime.
//
//  THE MINIMAL POST-LOGIN SHELL (sysadmin-console/03): on load the shell checks for
//  an established operator session (GET /api/admin/session, story 01). Until it
//  answers it shows a brief loader; then it renders EITHER <AdminLogin/> (no session)
//  OR the <ReviewQueue/> reported-tales screen (an operator session exists). This is
//  a tiny amount of conditional rendering, not a router - keeping the entry minimal
//  keeps the admin bundle small and free of any kid-app code (AC-04).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { StrictMode, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Box, CircularProgress, CssBaseline, ThemeProvider } from '@mui/material';
import { AdminLogin } from './AdminLogin';
import { ReviewQueue } from './ReviewQueue';
import { getOperatorSession } from './operatorClient';
import { theme } from '../theme';
import './fontawesome';

/** The shell's session-check phase: still checking, signed in, or not signed in. */
type ShellPhase = 'checking' | 'signed-in' | 'signed-out';

/**
 * The back-office shell: checks for an operator session once on load and routes
 * between the login screen and the review queue. A missing session (or any transport
 * failure) falls back to <AdminLogin/> - the fail-safe default, so an unauthenticated
 * visitor only ever sees the login gate (AC-06 of story 01).
 */
function AdminShell() {
  const [phase, setPhase] = useState<ShellPhase>('checking');
  const [operatorEmail, setOperatorEmail] = useState<string>('');

  useEffect(() => {
    let active = true;
    void (async () => {
      const session = await getOperatorSession();
      if (!active) return;
      if (session.signedIn && session.email) {
        setOperatorEmail(session.email);
        setPhase('signed-in');
      } else {
        setPhase('signed-out');
      }
    })();
    return () => {
      active = false;
    };
  }, []);

  if (phase === 'checking') {
    return (
      <Box sx={{ minHeight: '100dvh', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <CircularProgress color="primary" />
      </Box>
    );
  }

  if (phase === 'signed-in') {
    return <ReviewQueue operatorEmail={operatorEmail} />;
  }

  return <AdminLogin />;
}

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element #root not found in admin.html');
}

createRoot(rootElement).render(
  <StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <AdminShell />
    </ThemeProvider>
  </StrictMode>,
);
