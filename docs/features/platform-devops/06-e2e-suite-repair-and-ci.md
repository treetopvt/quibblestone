# Story: Repair the drifted e2e suite and gate it in CI

**Feature:** Platform & DevOps  ·  **Status:** Complete  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #189

## Context
Story 01 stood up Playwright as the e2e harness and it has since proven itself on real
regressions (`group-play/05`'s mode selection, the `session-engine/07-11` reconnect
chain) - but it was deliberately kept out of CI (`.github/workflows/ci.yml`) because its
specs assert against the LIVE SignalR hub, which needs a runnable full stack (CLAUDE.md
section 9), not just a built artifact. Two later, entirely intentional UI changes have
since drifted 3 of the suite's 8 tests: the Lobby's fit-to-viewport redesign moved the
mode picker off the main screen into a collapsed "Game settings" bottom sheet
(`web/src/components/GameSettingsSheet.tsx`, `web/src/pages/Lobby.tsx`), and the Home
screen's play-solo pill dropped its "Or " prefix (`web/src/pages/Home.tsx`). Because the
suite was not in CI, nobody noticed - exactly the failure mode `docs/ROADMAP.md`'s
"Open / near-done" table calls out: "e2e is not in CI so drift goes unnoticed." This
matters now because the pre-beta polish sprint (after the friends-and-family test) is
exactly the kind of UI churn that re-breaks selectors; this story is the safety net so
the next drift shows up as a red CI check instead of a silent gap. See
[feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given `tests/group-mode.spec.ts`'s host, when the spec drives it to pick
      Word Bank, then it first taps the collapsed settings row
      (`getByRole('button', { name: /Game settings/ })`) to open
      `<GameSettingsSheet>` before asserting on or interacting with the `radiogroup`
      named "Choose a mode" (today unmounted until the sheet opens - MUI `Drawer`
      defaults to `keepMounted={false}`), and closes the sheet (its "Done" button)
      before tapping "Start game" (the sheet's scrim blocks the pinned Start CTA
      while open) - the file's one test passes.
- [x] AC-02: Given `tests/reconnect.spec.ts`'s "a mid-round page reload resumes onto
      the live /round screen, not Home" test, when it drives the host to start a
      Word Bank round (today it hangs at `getByRole('radiogroup', { name: 'Choose a
      mode' })` until the test's 90s bound, because the picker is off-screen), then
      it uses the same open-sheet / select / close-sheet sequence as AC-01 before
      starting the round, and the test passes well inside its timeout.
- [x] AC-03: Given `tests/routing.spec.ts`'s "Home navigates to the solo route" test,
      when it clicks the play-solo pill, then it targets the CURRENT accessible
      name "Play solo right now" (not the stale "Or play solo right now" -
      `web/src/pages/Home.tsx`'s pill dropped the "Or " prefix in the
      fit-to-viewport redesign), and the test passes without hitting its 30s click
      timeout.
- [x] AC-04: Given the three repairs above, when `npm run test:e2e` runs locally
      against a running stack (the API on :5180, the web dev server), then all 8
      tests across all 4 spec files pass, and the diff touches only files under
      `tests/` - this is a test-only repair; the app's current behavior is correct
      and intentional, so nothing under `api/` or `web/src` changes.
- [x] AC-05: Given a push or PR to `main`, when CI runs, then a job boots the API
      (`dotnet run --project api/QuibbleStone.Api.csproj`) in the background and
      gates on its readiness via a bounded retry loop against `GET /health`
      (`api/src/Controllers/HealthController.cs`) - not a fixed `sleep N` - before
      running `npm run test:e2e`; Playwright boots/awaits its own web dev server via
      the existing `webServer` block in `playwright.config.ts`, so this job never
      separately starts or waits on :5173.
- [x] AC-06: Given the api and web CI jobs exist, when the e2e job is defined, then
      it depends on both (`needs: [api, web]`) so a broken build or a failing unit
      test never pays the cost of a full-stack e2e run; and given the API never
      becomes healthy within AC-05's bound, then the job fails with a clear message
      rather than hanging into Playwright's own per-test timeouts.
- [x] AC-07: Given the new job runs, when it executes, then it does NOT run
      `playwright install` (Chromium is pre-provisioned in the CI image, per
      `playwright.config.ts`'s own header comment), it keeps the config's existing
      CI behavior (`retries: 1`, `trace: 'on-first-retry'`, the `github` reporter)
      rather than re-implementing retry/trace logic in the workflow, it uploads the
      Playwright report (and any trace) as a build artifact so a red run is
      triageable without a blind local re-run, and it carries a bounded
      `timeout-minutes` so a genuine hang fails within a few minutes instead of
      exhausting the whole CI run's budget.

## Out of Scope
- Any change to `api/` or `web/src` application code - both drifts are the tests
  catching up to intentional, already-shipped product changes (the settings sheet,
  the shortened pill label), not app bugs.
- New e2e coverage (group Progressive Story, a second browser project, visual
  regression, etc.) - this story repairs and gates the EXISTING 8 tests only.
  Extending coverage is a separate story when a new flow needs it.
- Cross-browser runs (Firefox/WebKit) - stays Chromium-only, matching the current
  config.
- Softening the new CI job to a non-blocking/advisory check - it is wired as a real
  gate (a failing spec fails the run), mirroring how Vitest and xUnit already gate.
  If CI-environment flakiness proves unmanageable in practice, softening it is a
  follow-up decision, not this story's default.
- Sharing build artifacts between the existing `api`/`web` jobs and the new job
  (e.g. publishing the API build once and downloading it) - the new job
  restores/builds the API itself; that optimization can follow later if the added
  CI time becomes a problem.

## Technical Notes
- **Files this story touches:** `tests/group-mode.spec.ts`, `tests/reconnect.spec.ts`,
  `tests/routing.spec.ts` (repairs), and `.github/workflows/ci.yml` (a new job).
  `tests/smoke.spec.ts` is untouched - it never referenced either drifted surface.
  `playwright.config.ts` needs no change: its `webServer`, `retries`, `trace`, and
  `reporter` settings (platform-devops/01) already do the right thing locally and
  in CI.
- **The mode-picker fix, precisely:** the collapsed row's accessible name is
  `"Game settings <live summary>"` (e.g. "Game settings Full tale - Classic
  (Blind) - Family-safe on", confirmed against a live DOM snapshot) - target it
  with a substring/regex match (`{ name: /Game settings/ }`), the same pattern
  `group-mode.spec.ts` already uses for the dynamic "Join <CODE>" button. After
  selecting the radio, close via `getByRole('button', { name: 'Done' })` (or
  Escape) before the next tap - the `Drawer`'s backdrop intercepts clicks to
  "Start game" while the sheet is open.
- **Readiness gate shape:** a bounded loop against `GET http://localhost:5180/health`
  (e.g. `curl -fsS`, retried every couple of seconds up to a capped total wait), not
  a fixed `sleep N` - the point of gating on readiness is that a real hub dependency
  fails clearly on a slow/broken boot instead of flaking downstream in Playwright.
- **No `playwright install`:** Chromium is pre-provisioned in this dev environment at
  a path Playwright resolves automatically (`/opt/pw-browsers`); confirm the same
  holds on the actual GitHub Actions runner CI targets before assuming it - if it
  does not, provisioning the browser becomes part of this story's job, not a
  separate one.
  **Resolution (shipped):** this contingency fired. A GitHub-hosted `ubuntu-latest`
  runner does NOT carry the dev image's pre-provisioned `/opt/pw-browsers`, so the
  `e2e` job runs `npx playwright install --with-deps chromium` (Chromium only,
  matching the config's single project) before the suite. This is the anticipated
  browser-provisioning step, not a re-implementation of retry/trace/reporter logic -
  those stay in `playwright.config.ts` (`retries: 1`, `trace: 'on-first-retry'`, the
  `github` reporter), untouched. The rest of AC-07 holds as written: bounded
  `timeout-minutes`, and the report/trace uploaded as an artifact.
- **Empirically verified, not guessed:** ran the full suite locally against the API
  on :5180 (`npm run test:e2e`): 5 passed, 3 failed exactly as described above -
  `group-mode.spec.ts:80` (`toBeVisible()` on the radiogroup finds no element),
  `reconnect.spec.ts`'s second test hanging at its `radiogroup`/radio interaction
  to the full 90s test timeout, and `routing.spec.ts:36` timing out at 30s waiting
  for `{ name: 'Or play solo right now' }`. This confirms these are the only 3 of 8
  currently red and that the other 5 (including `reconnect.spec.ts`'s first test)
  need no changes.
- Reuse map: no new packages or infrastructure - this story wires the harness
  `platform-devops/01` already built into `ci.yml`, and repairs specs originally
  written for `group-play/05` and `session-engine/07-11`.

## Tests

| AC | Test |
|---|---|
| AC-01 | `tests/group-mode.spec.ts` (repaired) - `npm run test:e2e`; manual: `npx playwright test group-mode.spec.ts` passes in isolation |
| AC-02 | `tests/reconnect.spec.ts` (repaired) - `npm run test:e2e`; manual: both tests in the file pass, the mid-round-reload one well under its 90s bound |
| AC-03 | `tests/routing.spec.ts` (repaired) - `npm run test:e2e`; manual: "Home navigates to the solo route" passes well under its 30s click bound |
| AC-04 | `npm run test:e2e` (full suite) - manual: 8/8 pass locally, run twice back to back to rule out one-off flake, and `git diff --stat` shows only `tests/` files touched |
| AC-05 | `.github/workflows/ci.yml` (new job) - manual: open a PR, confirm the Actions run shows the job booting the API and gating on `/health` before Playwright starts |
| AC-06 | `.github/workflows/ci.yml` (same job) - manual: confirm the job graph shows `needs: [api, web]`; temporarily break the API's health route on a scratch branch and confirm the job fails clearly instead of hanging |
| AC-07 | `.github/workflows/ci.yml` (same job) - manual: confirm no `playwright install` step exists; force one spec red on a scratch branch and confirm the job fails within its `timeout-minutes` with a report/trace artifact attached to the run |

## Dependencies
Story 01 (Test harness - Vitest + Playwright; Complete) - this story repairs and
gates the Playwright harness it stood up. Otherwise none; independent of every
other open story.
