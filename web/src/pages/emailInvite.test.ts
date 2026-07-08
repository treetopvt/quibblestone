// ----------------------------------------------------------------------------
//  emailInvite.test.ts - Vitest coverage for the two PURE narrowing decisions the
//  email-invite REST client makes (session-engine/12, issue #180, AC-06). This
//  suite's vitest.config.ts runs in the `node` environment (no DOM/fetch harness -
//  see CLAUDE.md section 9), so - exactly like useRoomInvite.test.ts covers only
//  resolveOrigin - it covers the pure body-narrowing functions, not the `fetch`
//  wrappers (those fail-graceful paths are exercised manually per the story's Tests
//  table). The load-bearing guarantee here is AC-06's "fail toward hidden": anything
//  that is not an explicit { available: true } narrows to false.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { narrowAvailability, narrowSendResult } from './emailInvite';

describe('narrowAvailability', () => {
  it('returns true only for an explicit { available: true }', () => {
    expect(narrowAvailability({ available: true })).toBe(true);
  });

  it('returns false for { available: false }', () => {
    expect(narrowAvailability({ available: false })).toBe(false);
  });

  it('fails toward false for a missing field, a wrong type, null, or a non-object (AC-06)', () => {
    expect(narrowAvailability({})).toBe(false);
    expect(narrowAvailability({ available: 'yes' })).toBe(false);
    expect(narrowAvailability({ available: 1 })).toBe(false);
    expect(narrowAvailability(null)).toBe(false);
    expect(narrowAvailability('nope')).toBe(false);
    expect(narrowAvailability(undefined)).toBe(false);
  });
});

describe('narrowSendResult', () => {
  it('is ok for { sent: true } regardless of status', () => {
    expect(narrowSendResult(200, { sent: true })).toEqual({ ok: true });
  });

  it('maps a 429 to a not-ok result with a rate-limited message', () => {
    const result = narrowSendResult(429, null);
    expect(result.ok).toBe(false);
    expect(result.message).toBeTruthy();
  });

  it('uses the server-supplied message for a not-ok body when present', () => {
    const result = narrowSendResult(400, { sent: false, message: 'That room code does not look right.' });
    expect(result).toEqual({ ok: false, message: 'That room code does not look right.' });
  });

  it('falls back to a generic message when the body carries none', () => {
    const result = narrowSendResult(500, {});
    expect(result.ok).toBe(false);
    expect(result.message).toBeTruthy();
  });

  it('never treats a non-true sent value as success', () => {
    expect(narrowSendResult(200, { sent: 'true' }).ok).toBe(false);
    expect(narrowSendResult(200, null).ok).toBe(false);
  });
});
