// ----------------------------------------------------------------------------
//  main.tsx - web client entry point.
//
//  Mounts <App/> inside <BrowserRouter> (design-system/04 - client routing) and
//  the MUI ThemeProvider (the look-and-feel home) with a CssBaseline reset.
//  BrowserRouter wraps App so App can call useGameHub ABOVE <Routes> - the one
//  SignalR connection is never remounted by navigation. Importing ./fontawesome
//  registers the icon set once. StrictMode double-invokes effects in dev.
//
//  platform-devops/04 (AC-06): installErrorBeacon() wires the anonymous
//  unhandled-error beacon ONCE at startup (window 'error' + 'unhandledrejection'
//  -> a tiny server-side POST, no App Insights JS SDK, no PII). It is idempotent,
//  so StrictMode's dev double-invoke is harmless.
//
//  B5 (alpha-gate hardening): <ErrorBoundary/> wraps <App/> so an uncaught
//  RENDER error (a channel the window-level beacon above never sees - React
//  catches it first) shows a minimal "Something went off" + reload screen
//  instead of unmounting the whole tree to a permanent blank page. It sits
//  INSIDE ThemeProvider/CssBaseline so its own fallback UI is themed too.
// ----------------------------------------------------------------------------

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { CssBaseline, ThemeProvider } from '@mui/material';
import App from './App';
import { theme } from './theme';
import { PurchaserSessionProvider } from './account/PurchaserSession';
import { installErrorBeacon } from './telemetry/errorBeacon';
import { ErrorBoundary } from './components';
import './fontawesome';

// AC-06: install the anonymous client-error beacon before the app mounts, so an
// error during first render is still reported (anonymously, no PII).
installErrorBeacon();

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element #root not found in index.html');
}

createRoot(rootElement).render(
  <StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <ErrorBoundary>
        <BrowserRouter>
          <PurchaserSessionProvider>
            <App />
          </PurchaserSessionProvider>
        </BrowserRouter>
      </ErrorBoundary>
    </ThemeProvider>
  </StrictMode>,
);
