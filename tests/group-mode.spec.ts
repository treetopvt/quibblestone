// ----------------------------------------------------------------------------
//  group-mode.spec.ts - two-context end-to-end for group mode selection
//  (group-play/05). This is the "scary part" check the harness deferred to
//  group-play (see playwright.config.ts header): a REAL second device proving the
//  host's chosen mode reaches every player over the one SignalR connection.
//
//  What it proves (against the running stack - Vite on :5173 + the API hub on
//  :5180, same prereqs as smoke.spec.ts):
//    - AC-01: the mode picker is HOST-ONLY - the host lobby shows it, a joiner's
//      lobby does not.
//    - AC-04 / AC-05: the group picker offers exactly the THREE deferred-surface-
//      free modes (Classic Blind, Word Bank, Progressive Reveal) and NOT Progressive
//      Story (its live "story so far" is deferred).
//    - AC-02 / AC-03: when the host picks Word Bank and starts, BOTH players' round
//      screens render the Word Bank answer surface ("Tap a word from the bank") -
//      the player-visible proof the chosen mode propagated over the wire to every
//      client and each resolved it through the shared registry, not just the host.
//
//  It stops once both players see the Word Bank surface: the distribute -> collect
//  -> shared-reveal loop itself is already covered by the group-play/03-04 flow;
//  this spec's job is the NEW seam (the host's mode reaching everyone). Asserting
//  on the surface label avoids any dependence on which blank category each player
//  is dealt (the label renders regardless of the per-category tap list).
//
//  Run from the repo root: `npm --prefix web run test:e2e`.
// ----------------------------------------------------------------------------

import { test, expect, type Page } from '@playwright/test';

// The mode picker is a labelled radiogroup ("Choose a mode"); scope every mode
// assertion to it so it never collides with the story-length choice control.
const modePicker = (page: Page) => page.getByRole('radiogroup', { name: 'Choose a mode' });

/** Create a room as the host and return its 4-char join code (read from the lobby). */
async function hostCreatesRoom(page: Page, name: string): Promise<string> {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Create a game' })).toBeEnabled();
  await page.getByRole('button', { name: 'Create a game' }).click();

  await page.getByLabel('Display name').fill(name);
  await page.getByRole('button', { name: 'Create game' }).click();

  // The lobby's share widget renders the code as a standalone 4-char token (the
  // unambiguous alphabet excludes O/0/I/1/L). Read it to hand to the joiner.
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

test('the host picks the mode and it reaches every player (Word Bank across two devices)', async ({
  browser,
}) => {
  test.setTimeout(60_000);

  // Two independent devices: separate contexts so their anonymous identity /
  // connection state never bleed together.
  const hostContext = await browser.newContext();
  const joinerContext = await browser.newContext();
  const host = await hostContext.newPage();
  const joiner = await joinerContext.newPage();

  try {
    const code = await hostCreatesRoom(host, 'Mossy');
    await joinerJoinsRoom(joiner, code, 'Maple');

    // The host sees the joiner arrive (roster is live) before starting. Exact
    // match targets the roster tile, not the transient "Maple pulled up a stone" toast.
    await expect(host.getByText('Maple', { exact: true })).toBeVisible();

    // AC-01: the mode picker is host-only.
    await expect(modePicker(host)).toBeVisible();
    await expect(modePicker(joiner)).toHaveCount(0);

    // AC-04 / AC-05: exactly the three offered modes, and Progressive Story is
    // NOT among them (it is deferred for group play).
    await expect(modePicker(host).getByRole('radio')).toHaveCount(3);
    await expect(modePicker(host).getByRole('radio', { name: /Classic/ })).toBeVisible();
    await expect(modePicker(host).getByRole('radio', { name: /Word Bank/ })).toBeVisible();
    await expect(modePicker(host).getByRole('radio', { name: /Progressive Reveal/ })).toBeVisible();
    await expect(modePicker(host).getByRole('radio', { name: /Progressive Story/ })).toHaveCount(0);

    // The host taps Word Bank. The setup controls live in the scroll flow (only
    // the Start CTA is pinned), so the card is a real tap target - a plain click
    // (not a keyboard workaround) proves a host can actually reach and select it.
    const wordBankRadio = modePicker(host).getByRole('radio', { name: /Word Bank/ });
    await wordBankRadio.click();
    await expect(wordBankRadio).toHaveAttribute('aria-checked', 'true');

    // Start the round for the whole room (the CTA lives in the always-visible bar).
    await host.getByRole('button', { name: 'Start game' }).click();
    await expect(host).toHaveURL(/\/round$/);

    // AC-02 / AC-03: the chosen mode reached BOTH clients - each round screen
    // renders the Word Bank answer surface (not the Classic free-text default),
    // proving the host's pick propagated over the wire and every client resolved
    // it through the shared registry.
    await expect(host.getByText('Tap a word from the bank')).toBeVisible();
    await expect(joiner.getByText('Tap a word from the bank')).toBeVisible();
  } finally {
    await hostContext.close();
    await joinerContext.close();
  }
});
