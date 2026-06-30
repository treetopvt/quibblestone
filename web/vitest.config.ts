// ----------------------------------------------------------------------------
//  vitest.config.ts - minimal Vitest wiring for the pure engine logic under
//  web/src/engine/ (template-model/01's AC-06 determinism tests).
//
//  NOTE: this is a MINIMAL SEED, not the canonical harness. platform-devops/01
//  (CLAUDE.md section 9 / README section 7) owns the project-wide test
//  harness (Vitest for engine logic, Playwright for the 2-player real-time
//  flow) and is not built yet. This config exists only so
//  web/src/engine/assemble.test.ts can run today via `npm run test:unit`.
//  Expect platform-devops/01 to fold this into (or replace it with) the
//  canonical setup later - keep this file disposable.
//
//  Environment is `node`, not `jsdom`: everything under web/src/engine/ is
//  pure TS with no DOM dependency (no React, no MUI), so there is no need to
//  pay jsdom's cost here.
// ----------------------------------------------------------------------------

import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
