// ----------------------------------------------------------------------------
//  familySafeGate.ts - the pure decision behind the family-safe toggle's age
//  gate (child-safety/02 follow-up: the grown-up content tier).
//
//  Turning family-safe OFF now unlocks the non-family-safe ("teen-plus") story
//  tier in the seed library (../content/seedLibrary.ts), so that ONE direction
//  is gated behind an explicit 18+ confirmation. This module holds just the
//  branch decision so it is unit-testable in the node test env (the repo has no
//  jsdom/testing-library) - FamilySafeToggle.tsx renders the dialog and owns the
//  ephemeral "already confirmed" state, but the RULE for what a flip should do
//  lives here, mirroring how every other content gate (familySafe.ts, length.ts,
//  fresh.ts, wordBankOffering.ts) is a pure, separately-tested function.
//
//  Pure by construction: inputs in, action out. No React, no state, no I/O.
// ----------------------------------------------------------------------------

/**
 * What a family-safe switch flip should do:
 *   - `apply`       : change the toggle immediately to `familySafe`.
 *   - `confirm-age` : the flip unlocks grown-up content and needs an explicit
 *                     18+ confirmation first (do not change the value yet).
 */
export type FamilySafeToggleAction =
  | { kind: 'apply'; familySafe: boolean }
  | { kind: 'confirm-age' };

/**
 * Decides what should happen when the family-safe switch is flipped to `next`.
 *
 * - Turning family-safe back ON (`next === true`) is ALWAYS immediate - widening
 *   safety never needs a gate.
 * - Turning it OFF (`next === false`) unlocks the grown-up tier, so it requires
 *   an 18+ confirmation the FIRST time; once `ageAlreadyConfirmed` is true (the
 *   player confirmed earlier this toggle session) it applies immediately and
 *   does not nag again.
 */
export function resolveFamilySafeToggle(
  next: boolean,
  ageAlreadyConfirmed: boolean,
): FamilySafeToggleAction {
  if (next || ageAlreadyConfirmed) {
    return { kind: 'apply', familySafe: next };
  }
  return { kind: 'confirm-age' };
}
