// ----------------------------------------------------------------------------
//  reconnect.spec.ts - two-context end-to-end for the "Don't Lose the Room"
//  reconnect chain (session-engine/07-10), run at the orchestration playbook's
//  Phase 4 verification checkpoint against the real stack (Vite on :5173 + the
//  API hub on :5180, same prereqs as smoke.spec.ts / group-mode.spec.ts).
//
//  What it proves end-to-end (the whole 07-10 chain, not any one story in
//  isolation):
//    - 07 (grace window) + 10 (tile): when a seat's connection drops (a closed
//      page), the seat is HELD, not evicted - the host's lobby keeps the tile and
//      shows a calm "reconnecting..." treatment (AC-04) instead of the seat
//      silently vanishing.
//    - 08 (Rejoin) + 09 (web auto-rejoin) + AC-06: reopening the device (the
//      stored {code, token} handle survives in localStorage) auto-rejoins the held
//      seat with no user action, and the host's roster flips that tile back to
//      READY in near-real-time.
//    - 09 + 10 (the headline payoff): a real page RELOAD mid-round drops the
//      connection, holds the seat, and auto-resumes the reloaded client onto its
//      LIVE screen (/round) with its round intact - it does NOT bounce Home
//      (the refresh-safety redirect is correctly narrowed, AC-01/02/03).
//
//  Determinism: this uses a real page close / reload (an immediate transport
//  teardown -> the server's OnDisconnectedAsync fires at once), NOT a slow
//  network-timeout simulation, so the grace hold and the auto-rejoin are both
//  observed without racing SignalR's keepalive timers.
//
//  Run from web: `npm run test:e2e` (config ../playwright.config.ts).
// ----------------------------------------------------------------------------

import { test, expect, type BrowserContext, type Page } from '@playwright/test';

/** Create a room as the host and return its 4-char join code (read from the lobby). */
async function hostCreatesRoom(page: Page, name: string): Promise<string> {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Create a game' })).toBeEnabled();
  await page.getByRole('button', { name: 'Create a game' }).click();

  await page.getByLabel('Display name').fill(name);
  await page.getByRole('button', { name: 'Create game' }).click();

  // The unambiguous alphabet excludes O/0/I/1/L (session-engine/01).
  const codeEl = page.getByText(/^[A-HJ-NP-Z2-9]{4}$/).first();
  await expect(codeEl).toBeVisible();
  return (await codeEl.innerText()).trim();
}

/** Join an existing room by code as a second device. */
async function joinerJoinsRoom(page: Page, code: string, name: string): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Join a game' }).click();
  await page.getByLabel('Room code').fill(code);
  await page.getByLabel('Display name').fill(name);
  await page.getByRole('button', { name: new RegExp(`^Join ${code}$`) }).click();
}

test('a dropped seat is HELD and shown "reconnecting...", then auto-rejoins back to READY', async ({
  browser,
}) => {
  test.setTimeout(90_000);

  const hostContext = await browser.newContext();
  const joinerContext: BrowserContext = await browser.newContext();
  const host = await hostContext.newPage();
  let joiner: Page | null = await joinerContext.newPage();

  try {
    const code = await hostCreatesRoom(host, 'Mossy');
    await joinerJoinsRoom(joiner, code, 'Maple');

    // The host sees the joiner arrive and READY (exact match targets the roster
    // tile, not the transient "pulled up a stone" toast).
    await expect(host.getByText('Maple', { exact: true })).toBeVisible();
    await expect(host.getByText('reconnecting...')).toHaveCount(0);

    // The joiner's device drops (page closed = immediate transport teardown).
    // localStorage (the {code, token} handle) survives on the CONTEXT.
    await joiner.close();
    joiner = null;

    // 07 + 10 (AC-04): the seat is HELD (still present) and flagged reconnecting -
    // it does NOT vanish. This persists for the grace window because nothing has
    // rejoined yet, so it is reliably assertable (no timing race).
    await expect(host.getByText('Maple', { exact: true })).toBeVisible();
    await expect(host.getByText('reconnecting...')).toBeVisible();

    // 08 + 09: the device reopens (a fresh page in the SAME context -> same
    // localStorage handle). The hook's mount-time effect auto-invokes Rejoin with
    // no user action and resumes the held seat.
    const resumed = await joinerContext.newPage();
    await resumed.goto('/');

    // AC-06: the host's roster flips the tile back to connected (READY) in
    // near-real-time - "reconnecting..." clears.
    await expect(host.getByText('reconnecting...')).toHaveCount(0, { timeout: 30_000 });
    await expect(host.getByText('Maple', { exact: true })).toBeVisible();

    // The resumed device landed back in the live lobby (NOT bounced Home): it
    // shows the shared join code, not the Home "Create a game" CTA.
    await expect(resumed.getByText(code, { exact: true })).toBeVisible({ timeout: 30_000 });
    await expect(resumed.getByRole('button', { name: 'Create a game' })).toHaveCount(0);
  } finally {
    await hostContext.close();
    await joinerContext.close();
  }
});

test('a mid-round page reload resumes onto the live /round screen, not Home', async ({
  browser,
}) => {
  test.setTimeout(90_000);

  const hostContext = await browser.newContext();
  const joinerContext = await browser.newContext();
  const host = await hostContext.newPage();
  const joiner = await joinerContext.newPage();

  try {
    const code = await hostCreatesRoom(host, 'Mossy');
    await joinerJoinsRoom(joiner, code, 'Maple');
    await expect(host.getByText('Maple', { exact: true })).toBeVisible();

    // Host starts a Word Bank round so both clients have a stable, mode-agnostic
    // surface label to assert on ("Tap a word from the bank"). The mode picker now
    // lives in the collapsed "Game settings" bottom sheet (design-system/05), so
    // open it, pick, and close it (its scrim blocks Start) before starting.
    await host.getByRole('button', { name: /Game settings/ }).click();
    const modePicker = host.getByRole('radiogroup', { name: 'Choose a mode' });
    await modePicker.getByRole('radio', { name: /Word Bank/ }).click();
    await host.getByRole('button', { name: 'Done' }).click();
    await host.getByRole('button', { name: 'Start game' }).click();

    await expect(host).toHaveURL(/\/round$/);
    await expect(joiner).toHaveURL(/\/round$/);
    await expect(joiner.getByText('Tap a word from the bank')).toBeVisible();

    // The joiner's phone reloads mid-round (a real reload drops the SignalR
    // connection -> the seat is held by the grace window). Story 09 auto-rejoins on
    // remount; story 10 holds the live route through the resume instead of bouncing
    // Home.
    await joiner.reload();

    // AC-01/02/03: it resumes onto its LIVE screen with the round intact - back on
    // /round showing its own outstanding Word Bank blanks - and is NOT stranded on
    // Home ('/').
    await expect(joiner).toHaveURL(/\/round$/, { timeout: 30_000 });
    await expect(joiner.getByText('Tap a word from the bank')).toBeVisible({ timeout: 30_000 });
  } finally {
    await hostContext.close();
    await joinerContext.close();
  }
});
