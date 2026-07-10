// ----------------------------------------------------------------------------
//  admin/main.tsx - entry point for the SEPARATE operator back office
//  (sysadmin-console/01, issue #135; extended by /03 #137, /04, and /05's jobs
//  shell reorganization, issue #214).
//
//  This is a SECOND, independent Vite entry (web/admin.html -> here), NOT part of
//  the kid app's bundle (AC-04, load-bearing). It mounts the back office inside the
//  MUI ThemeProvider (the SHARED visual language, web/src/theme.ts - explicitly
//  allowed) with a CssBaseline reset, and registers the admin-only FontAwesome set.
//  It imports NOTHING from the kid app (pages / signalr / gallery / engine /
//  components) and opens NO SignalR connection - the back office has no realtime.
//
//  THE JOBS SHELL (sysadmin-console/05, AC-01): on load the shell checks for an
//  established operator session (GET /api/admin/session, story 01). Until it
//  answers it shows a brief loader; then it renders EITHER <AdminLogin/> (no
//  session) OR - once an operator session exists - a three-tab back office
//  organized around the JOBS an operator does (ADR 0003 Layer 3), not around
//  which feature happened to ship a screen: Support (find a person, fix their
//  problem - <SupportLookup/>, sysadmin-console/07, the account lookup + verbs that
//  folds in story 02's grant/revoke via the SAME purchasersClient plumbing, AC-02),
//  Content (moderation - <ReviewQueue/>, story 03, relocated as-is, AC-03), and
//  Operations (settings/flags + Stripe mode - <OperationsPanel/>, composing
//  story 04's Stripe-mode panel with the new dependency-tolerant settings view,
//  AC-04). This replaces the prior flat 'review' | 'entitlements' | 'stripe-mode'
//  shell. The tab is a single useState toggle, NOT a router - keeping the entry
//  minimal keeps the admin bundle small and free of any kid-app code (AC-04 (of
//  story 01) / AC-05). No role-management or operator-list UI is added anywhere
//  here (AC-07 of story 05 - out of scope, parked in feature.md).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { StrictMode, useCallback, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Box, CircularProgress, CssBaseline, Stack, Tab, Tabs, ThemeProvider } from '@mui/material';
import { AdminLogin } from './AdminLogin';
import { ReviewQueue } from './ReviewQueue';
import { SupportLookup } from './SupportLookup';
import { OperationsPanel } from './OperationsPanel';
import { ADMIN_TABS, type AdminTab } from './adminTabs';
import { getOperatorSession, type OperatorSessionResult } from './operatorClient';
import { theme } from '../theme';
import './fontawesome';

/** The shell's session-check phase: still checking, signed in, or not signed in. */
type ShellPhase = 'checking' | 'signed-in' | 'signed-out';

/**
 * The back-office shell: checks for an operator session once on load and routes
 * between the login screen and the two signed-in screens. A missing session (or any
 * transport failure) falls back to <AdminLogin/> - the fail-safe default, so an
 * unauthenticated visitor only ever sees the login gate (AC-06 of story 01).
 */
function AdminShell() {
  const [phase, setPhase] = useState<ShellPhase>('checking');
  const [operatorEmail, setOperatorEmail] = useState<string>('');
  const [tab, setTab] = useState<AdminTab>('support');
  // The short-lived operator credential handed up by AdminLogin's verify, held IN
  // MEMORY for the shell's lifetime (never persisted - mirrors the purchaser
  // PurchaserSession). It is the PRIMARY credential on a cross-ORIGIN deployment (web
  // and API on different sites), where the HttpOnly operator cookie is never sent: the
  // session echo and both admin screens present it as a bearer. Null until a magic link
  // verifies (and again after a full reload - re-signing-in is a cheap link); on a
  // same-site deployment the cookie alone still works with it null.
  const [credential, setCredential] = useState<string | null>(null);

  // Apply a fetched session: route to the console (signed-in) or the login gate
  // (signed-out).
  const applySession = useCallback((session: OperatorSessionResult) => {
    if (session.signedIn && session.email) {
      setOperatorEmail(session.email);
      setPhase('signed-in');
    } else {
      setPhase('signed-out');
    }
  }, []);

  // Re-fetch + apply the session, presenting the operator credential as a bearer (the
  // cross-origin path). Used after a magic-link verify (AdminLogin's onAuthenticated) so
  // a followed link lands the operator IN the console, not on a dead-end panel. The
  // credential is passed EXPLICITLY (not read from state) so a just-set value is used at
  // once, before React commits the setCredential update. The shell is mounted when the
  // operator triggers this, so no unmount guard is needed on this caller.
  const refreshSession = useCallback(
    async (cred: string | null) => {
      applySession(await getOperatorSession(cred));
    },
    [applySession],
  );

  // Initial session check, guarded so a resolve after unmount does not setState
  // (StrictMode's mount/unmount/mount, or navigating away mid-fetch).
  useEffect(() => {
    let active = true;
    void (async () => {
      const session = await getOperatorSession();
      if (active) applySession(session);
    })();
    return () => {
      active = false;
    };
  }, [applySession]);

  if (phase === 'checking') {
    return (
      <Box sx={{ minHeight: '100dvh', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <CircularProgress color="primary" />
      </Box>
    );
  }

  if (phase === 'signed-in') {
    return (
      <Stack sx={{ minHeight: '100dvh' }}>
        {/* The minimal back-office nav: a three-JOB toggle, not a router (AC-01/AC-05). */}
        <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
          <Tabs
            value={tab}
            onChange={(_, next: AdminTab) => setTab(next)}
            variant="fullWidth"
            sx={{ maxWidth: 720, mx: 'auto' }}
          >
            {ADMIN_TABS.map((descriptor) => (
              <Tab
                key={descriptor.value}
                value={descriptor.value}
                label={descriptor.label}
                sx={{ fontWeight: 800, minHeight: 56 }}
              />
            ))}
          </Tabs>
        </Box>
        {tab === 'support' && <SupportLookup operatorEmail={operatorEmail} credential={credential} />}
        {tab === 'content' && <ReviewQueue operatorEmail={operatorEmail} credential={credential} />}
        {tab === 'ops' && <OperationsPanel operatorEmail={operatorEmail} credential={credential} />}
      </Stack>
    );
  }

  // The login gate re-checks the session after a magic-link verify (onAuthenticated),
  // so a followed operator link lands in the console rather than a dead-end panel.
  return (
    <AdminLogin
      onAuthenticated={(cred) => {
        setCredential(cred);
        setPhase('checking');
        void refreshSession(cred);
      }}
    />
  );
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
