// ----------------------------------------------------------------------------
//  main.tsx - web client entry point.
//
//  Mounts <App/> inside <BrowserRouter> (design-system/04 - client routing) and
//  the MUI ThemeProvider (the look-and-feel home) with a CssBaseline reset.
//  BrowserRouter wraps App so App can call useGameHub ABOVE <Routes> - the one
//  SignalR connection is never remounted by navigation. Importing ./fontawesome
//  registers the icon set once. StrictMode double-invokes effects in dev.
// ----------------------------------------------------------------------------

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
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
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ThemeProvider>
  </StrictMode>,
);
