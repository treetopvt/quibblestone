// ----------------------------------------------------------------------------
//  routing.spec.ts - verifies the react-router refactor (design-system/04).
//
//  Covers what the unit tests cannot: real URLs, the /join/:code deep link
//  pre-fill (session-engine/06's surface), and route navigation from Home. The
//  live 2-player round/reveal precedence stays a manual 2-context check (noted in
//  the story) - this spec exercises the entry routes a single browser can drive.
//
//  Requires the same running stack as smoke.spec.ts (Vite dev server via the
//  config's webServer + the API hub on :5180).
// ----------------------------------------------------------------------------

import { test, expect } from '@playwright/test';

test('deep link /join/:code lands on Join with the code pre-filled', async ({ page }) => {
  // WXYZ uses only the unambiguous code alphabet (no O/0/I/1/L), so normalizeCode
  // keeps all four characters - proving the route param seeds the code field.
  await page.goto('/join/WXYZ');

  await expect(page.getByRole('heading', { name: 'Join a game' })).toBeVisible();

  // The gold CTA interpolates the entered code ("Join <CODE>"), so a pre-filled
  // code shows on the button - the player-visible proof the deep link hydrated.
  await expect(page.getByRole('button', { name: 'Join WXYZ' })).toBeVisible();
  expect(page.url()).toContain('/join/WXYZ');
});

test('plain /join has no pre-filled code', async ({ page }) => {
  await page.goto('/join');
  // With no code, the CTA falls back to the bare "Join" label.
  await expect(page.getByRole('button', { name: 'Join', exact: true })).toBeVisible();
});

test('Home navigates to the solo route', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Play solo right now' }).click();
  await expect(page).toHaveURL(/\/solo$/);
  // The Solo screen identifies itself via its app-bar title.
  await expect(page.getByText('Play solo')).toBeVisible();
});

test('an unknown in-game URL with no live room redirects home', async ({ page }) => {
  // Refresh-safety (AC-05): /lobby with no live room must not render a broken
  // shell - it redirects to Home (the "Create a game" CTA is Home's signature).
  await page.goto('/lobby');
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole('button', { name: 'Create a game' })).toBeVisible();
});
