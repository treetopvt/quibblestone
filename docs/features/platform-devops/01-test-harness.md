# Story: Test harness (Vitest + Playwright)

**Feature:** Platform & DevOps  ·  **Status:** Not Started

## Context
The walking skeleton has no test framework. Before the engine and real-time
flows grow, stand up a lightweight harness so features can be covered where the
risk actually is. See [feature.md](./feature.md) and
`.claude/agents/testing-agent.md`.

## Acceptance Criteria
- [ ] AC-01: Given the web project, when I run the unit-test command, then Vitest
      runs and a sample pure-logic test passes.
- [ ] AC-02: Given the repo, when I run the end-to-end command, then Playwright
      runs and a sample smoke test (loads the app, sees "Connected") passes.
- [ ] AC-03: Given CI runs, then it executes the unit tests (and the build) and
      fails the run if a test fails.
- [ ] AC-04: Given a developer reads the docs, then the test commands are
      documented (web/README and/or CLAUDE.md).

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
