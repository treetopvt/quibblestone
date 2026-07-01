// ----------------------------------------------------------------------------
//  smoke.spec.ts - the one end-to-end smoke test for the walking skeleton.
//
//  Proves the whole real-time loop is alive: the web client loads the Home
//  screen, opens the SignalR connection to the API hub, and the "Create a game"
//  CTA becomes enabled (Home disables it until useGameHub's connection.start()
//  resolves - so an enabled CTA is the player-visible proof the hub round trip
//  succeeded). Before session-engine/01 this asserted on a "Connected" status
//  chip; that walking-skeleton readout was replaced by the real Home screen, so
//  the smoke test now rides the CTA's connected-gated enabled state instead.
//
//  This asserts on PLAYER-VISIBLE behavior (the button a person would tap), not
//  component internals - per the testing-agent brief. It is the thin e2e slice
//  for platform-devops/01; the 2-player real-time sync test lands later with
//  group-play on top of this harness.
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

  // The Home screen identifies the app (the "QuibbleStone" wordmark heading).
  await expect(
    page.getByRole('heading', { name: 'QuibbleStone' }),
  ).toBeVisible();

  // The real assertion: the gold "Create a game" CTA becomes enabled once the
  // SignalR round trip to the API hub succeeds (Home gates it on the connected
  // state). expect.toBeEnabled polls up to the configured expect timeout, so
  // this waits on visible state rather than sleeping a fixed interval. With the
  // API down the CTA stays disabled and this test fails by design.
  await expect(page.getByRole('button', { name: 'Create a game' })).toBeEnabled();
});
