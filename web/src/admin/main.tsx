// ----------------------------------------------------------------------------
//  admin/main.tsx - entry point for the SEPARATE operator back office
//  (sysadmin-console/01, issue #135).
//
//  This is a SECOND, independent Vite entry (web/admin.html -> here), NOT part of
//  the kid app's bundle (AC-04, load-bearing). It mounts <AdminLogin/> inside the
//  MUI ThemeProvider (the SHARED visual language, web/src/theme.ts - explicitly
//  allowed) with a CssBaseline reset, and registers the admin-only FontAwesome set.
//  It imports NOTHING from the kid app (pages / signalr / gallery / engine /
//  components) and opens NO SignalR connection - the back office has no realtime.
//
//  There is no BrowserRouter here: the foundation back office is the single login
//  screen (actual admin capability is stories 02/03). Keeping the entry minimal
//  keeps the admin bundle small and free of any kid-app code.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { AdminLogin } from './AdminLogin';
import { theme } from '../theme';
import './fontawesome';

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element #root not found in admin.html');
}

createRoot(rootElement).render(
  <StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <AdminLogin />
    </ThemeProvider>
  </StrictMode>,
);
