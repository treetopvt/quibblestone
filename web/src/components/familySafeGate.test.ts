// ----------------------------------------------------------------------------
//  familySafeGate.test.ts - Vitest coverage for the family-safe toggle's age
//  gate decision (see ./familySafeGate.ts). Node env, no DOM: proves the RULE
//  (when a flip needs an 18+ confirmation vs. applies immediately) without
//  rendering the dialog - the child-safety-critical branch of the toggle.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { resolveFamilySafeToggle } from './familySafeGate';

describe('resolveFamilySafeToggle', () => {
  it('turning family-safe ON is always immediate (never gated)', () => {
    expect(resolveFamilySafeToggle(true, false)).toEqual({ kind: 'apply', familySafe: true });
    expect(resolveFamilySafeToggle(true, true)).toEqual({ kind: 'apply', familySafe: true });
  });

  it('turning family-safe OFF requires an 18+ confirmation the first time', () => {
    expect(resolveFamilySafeToggle(false, false)).toEqual({ kind: 'confirm-age' });
  });

  it('turning family-safe OFF is immediate once age is already confirmed', () => {
    expect(resolveFamilySafeToggle(false, true)).toEqual({ kind: 'apply', familySafe: false });
  });
});
