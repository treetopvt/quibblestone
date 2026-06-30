// ----------------------------------------------------------------------------
//  main.tsx - web client entry point.
//
//  Mounts <App/> inside the MUI ThemeProvider (the look-and-feel home) with a
//  CssBaseline reset. Importing ./fontawesome registers the icon set once for
//  the whole app. StrictMode double-invokes effects in dev to surface bugs.
// ----------------------------------------------------------------------------

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CssBaseline, ThemeProvider } from '@mui/material';
import App from './App';
import { theme } from './theme';
import './fontawesome';

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element #root not found in index.html');
}

createRoot(rootElement).render(
  <StrictMode>
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <App />
    </ThemeProvider>
  </StrictMode>,
);
