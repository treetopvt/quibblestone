# Story: Test harness (Vitest + Playwright)

**Feature:** Platform & DevOps  ·  **Status:** Complete

## Context
The walking skeleton has no test framework. Before the engine and real-time
flows grow, stand up a lightweight harness so features can be covered where the
risk actually is. See [feature.md](./feature.md) and
`.claude/agents/testing-agent.md`.

## Acceptance Criteria
- [x] AC-01: Given the web project, when I run the unit-test command, then Vitest
      runs and a sample pure-logic test passes.
      `npm run test:unit` runs `vitest run` (`web/package.json`,
      `web/vitest.config.ts`) against `src/**/*.test.ts`; multiple pure-logic
      specs exist (engine, content, pages).
- [x] AC-02: Given the repo, when I run the end-to-end command, then Playwright
      runs and a sample smoke test (loads the app, sees "Connected") passes.
      `npm run test:e2e` runs Playwright against `playwright.config.ts`
      (repo root) and `tests/smoke.spec.ts`, which loads the app and asserts
      the connected-gated "Create a game" CTA becomes enabled (the smoke
      test's evolution of the original "Connected" assertion, per the
      spec's own header comment).
- [x] AC-03: Given CI runs, then it executes the unit tests (and the build) and
      fails the run if a test fails.
      `.github/workflows/ci.yml` `web` job runs `npm run test:unit` before
      `npm run build`; the `api` job runs `dotnet test` before build-gated
      steps complete. Playwright e2e is deliberately NOT wired into CI yet
      (needs a running API hub) - noted in the workflow's own comments and
      consistent with this story's Out of Scope.
- [x] AC-04: Given a developer reads the docs, then the test commands are
      documented (web/README and/or CLAUDE.md).
      Documented in `web/README.md` ("Testing" section) and CLAUDE.md
      section 9.

## Out of Scope
- A full test pyramid or high coverage targets - cover real risk first.
- Real-time multi-player E2E (that lands with group-play, on top of this harness).
- A second unit framework (do not add Jest/RTL alongside Vitest without cause).

## Technical Notes
- Vitest in `web/` for pure TS (extract engine logic into pure functions and test
  those rather than rendering components).
- Playwright at the repo root (or a `tests/` folder); the smoke test asserts the
  placeholder page reaches the "Connected" state.
- Wire the unit-test step into `.github/workflows/ci.yml`.

## Dependencies
None (builds on the skeleton).

## Tests
- AC-01: the harness itself is proven by the many `web/src/**/*.test.ts`
  specs it runs (e.g. `web/src/engine/engine.test.ts`,
  `web/src/content/seedLibrary.test.ts`).
- AC-02: `tests/smoke.spec.ts` is the sample e2e smoke test this story
  delivers.
- AC-03: `.github/workflows/ci.yml` (`web` job's "Unit tests" step; `api`
  job's `dotnet test` step) - this is the CI config that makes the harness a
  real gate, not just a local convenience.
