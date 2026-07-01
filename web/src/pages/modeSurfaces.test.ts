// ----------------------------------------------------------------------------
//  modeSurfaces.test.ts - Vitest spec for the ModeSurfaces contract
//  (game-modes/03, AC-05).
//
//  Two things worth proving here:
//    1. `classicBlindSurfaces` is exactly `{}` - the explicit, documented
//       "no surfaces" default every future mode's own value is diffed
//       against, so a reader never has to infer that an empty object here is
//       intentional rather than an oversight.
//    2. Every field on `ModeSurfaces` is genuinely OPTIONAL: a bare `{}`
//       literal type-checks as a full `ModeSurfaces` value with no cast, no
//       `as any`, and no per-field `undefined` spelled out. This is a
//       type-level assertion (compile-time), not a runtime one - if a future
//       edit makes any field required, this file fails to type-check (which
//       `npm run test:unit`'s Vitest run surfaces as a build-time TS error via
//       tsc --noEmit in `npm run build`, and the assignment below also fails
//       Vitest's own type-checking of test files).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { classicBlindSurfaces, type ModeSurfaces } from './modeSurfaces';

describe('modeSurfaces', () => {
  it('exports classicBlindSurfaces as the explicit "no surfaces" default ({})', () => {
    expect(classicBlindSurfaces).toEqual({});
  });

  it('type-checks a bare {} literal as a full ModeSurfaces value (all fields optional)', () => {
    const noSurfaces: ModeSurfaces = {};
    expect(noSurfaces).toEqual(classicBlindSurfaces);
  });

  it('allows any subset of the three surfaces to be supplied independently', () => {
    const onlyAnswerSurface: ModeSurfaces = { answerSurface: 'a node' };
    const onlySeeContext: ModeSurfaces = { seeContext: 'a node' };
    const onlyRevealPresentation: ModeSurfaces = { revealPresentation: 'a node' };

    expect(onlyAnswerSurface.seeContext).toBeUndefined();
    expect(onlySeeContext.answerSurface).toBeUndefined();
    expect(onlyRevealPresentation.answerSurface).toBeUndefined();
  });
});
