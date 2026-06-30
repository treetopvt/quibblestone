// ----------------------------------------------------------------------------
//  vitest.config.ts - the CANONICAL unit-test harness for QuibbleStone.
//
//  This is the project-wide Vitest setup owned by platform-devops/01 (the test
//  harness story - CLAUDE.md section 9, README section 7). It supersedes the
//  minimal seed that template-model/01 dropped in earlier: same shape, now the
//  blessed home for all pure-logic specs, not a disposable stopgap.
//
//  Scope and intent:
//    - Vitest covers the PURE logic - the "one engine, many thin modes" core
//      (template parsing, blank typing, word collection, reveal assembly, mode
//      config) plus the seed content library. That logic is the natural unit-test
//      target (README section 4): extract pure functions and test those directly
//      rather than rendering React components. Component / real-time behavior is
//      Playwright's job (see ../playwright.config.ts + ../tests/smoke.spec.ts),
//      NOT this config's.
//    - `include` stays `src/**/*.test.ts`: specs live next to the code they
//      cover (e.g. src/engine/template.test.ts, src/engine/assemble.test.ts,
//      src/content/seedLibrary.test.ts). Add new pure-logic specs the same way.
//    - `*.spec.ts` is deliberately NOT matched here - that suffix is reserved
//      for Playwright e2e under tests/, so the two runners never collide.
//
//  Environment is `node`, not `jsdom`: everything under src/engine/ and
//  src/content/ is pure TS with no DOM dependency (no React, no MUI), so there is
//  no reason to pay jsdom's startup cost. If a future spec needs the DOM, give it
//  jsdom locally via a per-file `// @vitest-environment jsdom` pragma rather than
//  flipping the whole suite (keep the fast pure-logic path the default).
//
//  Commands (see web/README.md and CLAUDE.md section 9):
//    npm run test:unit   -> vitest run (CI uses this; it is wired into ci.yml).
// ----------------------------------------------------------------------------

import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
