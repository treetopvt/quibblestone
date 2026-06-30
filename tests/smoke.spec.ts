// ----------------------------------------------------------------------------
//  smoke.spec.ts - the one end-to-end smoke test for the walking skeleton.
//
//  Proves the whole real-time loop is alive: the web client loads, opens the
//  SignalR connection to the API hub, and the connection-status readout reaches
//  "Connected" (web/src/components/ConnectionStatus.tsx renders that label on the
//  status Chip once useGameHub's connection.start() resolves).
//
//  This asserts on PLAYER-VISIBLE behavior (the "Connected" text a person would
//  see), not component internals - per the testing-agent brief. It is the thin
//  e2e slice for platform-devops/01; the 2-player real-time sync test lands later
//  with group-play on top of this harness.
//
//  Requires a runnable full stack (see playwright.config.ts header): the Vite web
//  dev server (Playwright's webServer boots it) AND the API hub on :5180 (start it
//  with `dotnet run --project api/QuibbleStone.Api.csproj`). With the API down the
//  client stays at "Connecting..." and this test fails by design.
//
//  Run from the repo root: `npm --prefix web run test:e2e`.
// ----------------------------------------------------------------------------

import { test, expect } from '@playwright/test';

test('app loads and reaches the Connected state', async ({ page }) => {
  await page.goto('/');

  // The placeholder landing page identifies the app.
  await expect(
    page.getByRole('heading', { name: 'QuibbleStone' }),
  ).toBeVisible();

  // The real assertion: the connection-status Chip reaches "Connected" once the
  // SignalR round trip to the API hub succeeds. expect.toBeVisible polls up to
  // the configured expect timeout, so this waits on visible state rather than
  // sleeping a fixed interval.
  await expect(page.getByText('Connected', { exact: true })).toBeVisible();
});
