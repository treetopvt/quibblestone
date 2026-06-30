// ----------------------------------------------------------------------------
//  playwright.config.ts - the CANONICAL end-to-end harness for QuibbleStone.
//
//  Owned by platform-devops/01 (the test harness story). Playwright covers the
//  scary part the unit tests cannot: the real, browser-rendered web client
//  reaching the real-time hub. This wave ships ONE smoke test (tests/smoke.spec.ts)
//  that loads the app and asserts it reaches the "Connected" state. The 2-player /
//  two-context real-time sync test is deliberately NOT here yet - it lands with
//  group-play on top of this harness (story Out of Scope).
//
//  How the smoke test gets a running app:
//    - `webServer` boots the Vite dev server (web/) on :5173 and waits for it.
//      Playwright reuses an already-running server locally (reuseExistingServer)
//      and starts a fresh one in CI.
//    - The smoke test asserts "Connected", which requires the API SignalR hub to
//      be up too. The web dev server alone is NOT enough: start the API first
//      (dotnet run --project api/QuibbleStone.Api.csproj on :5180, the URL the
//      web client's VITE_SIGNALR_HUB_URL points at). With the API down the client
//      sits at "Connecting..." and the smoke test (correctly) fails. We do not
//      start the API from here because that is a cross-project concern - the e2e
//      run assumes a runnable full stack (README section 9 dev commands).
//
//  Browser:
//    - Chromium is PRE-INSTALLED in this environment (PLAYWRIGHT_BROWSERS_PATH ->
//      /opt/pw-browsers); do NOT run `playwright install`. Playwright resolves the
//      matching build automatically. If a pinned version ever drifts from the
//      pre-installed one, set PW_CHROMIUM_EXECUTABLE to the chrome binary (e.g.
//      /opt/pw-browsers/chromium-1194/chrome-linux/chrome) and it is honored below.
//
//  Specs use the `.spec.ts` suffix under tests/ so they never collide with the
//  Vitest unit specs (`src/**/*.test.ts`).
//
//  Module resolution note: this config lives at the repo root but the only
//  node_modules is web/'s (the web client owns the @playwright/test devDep). Node
//  resolves an import relative to the importing FILE, so a bare root config cannot
//  see @playwright/test. The `test:e2e` script runs from web/ with
//  NODE_PATH=node_modules so this import resolves. Run it that way
//  (`npm --prefix web run test:e2e`), not `npx playwright test` from the root.
//
//  Commands (see web/README.md and CLAUDE.md section 9):
//    npm --prefix web run test:e2e   -> playwright test (this root config).
// ----------------------------------------------------------------------------

import { defineConfig, devices } from '@playwright/test';

// Optional override for environments where the pinned Playwright build differs
// from the pre-installed Chromium. Empty string -> let Playwright resolve it.
const chromiumExecutable = process.env.PW_CHROMIUM_EXECUTABLE ?? '';

// Where the web client is served for the smoke test.
const WEB_BASE_URL = process.env.PW_WEB_BASE_URL ?? 'http://localhost:5173';

export default defineConfig({
  testDir: './tests',
  // One smoke test today; fail fast on a hang rather than wait the full default.
  timeout: 30_000,
  expect: { timeout: 10_000 },
  // No accidental .only sneaking into CI.
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: WEB_BASE_URL,
    trace: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        launchOptions: chromiumExecutable
          ? { executablePath: chromiumExecutable }
          : {},
      },
    },
  ],
  // Boot the Vite dev server for the smoke test. The API hub must be started
  // separately (see header) for the client to reach "Connected".
  webServer: {
    command: 'npm run dev',
    cwd: 'web',
    url: WEB_BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
  },
});
