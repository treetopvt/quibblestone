// ----------------------------------------------------------------------------
//  errorBeacon.test.ts - proves the anonymous client-error payload carries NO
//  PII (platform-devops/04, AC-04/AC-06). The whole point of the beacon is that
//  it reports an unhandled error WITHOUT ever shipping a nickname, room code, or
//  the query string (which could carry either). We test the PURE payload builder
//  so the guarantee is asserted, not just asserted-by-comment.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { buildClientErrorPayload, normalizeRoutePath, type ClientErrorPayload } from './errorBeacon';

describe('buildClientErrorPayload', () => {
  it('carries ONLY message, stack, and path - no PII fields (AC-04)', () => {
    const err = new Error('TypeError: boom');
    const payload = buildClientErrorPayload(err, '/room');

    // The shape is exactly the three anonymous fields - nothing else.
    expect(Object.keys(payload).sort()).toEqual(['message', 'path', 'stack']);

    // Defensively assert no identity-bearing key can sneak in.
    const forbidden = ['nickname', 'name', 'code', 'joinCode', 'roomCode', 'sessionId', 'query', 'search', 'ip'];
    for (const key of forbidden) {
      expect(payload as unknown as Record<string, unknown>).not.toHaveProperty(key);
    }
  });

  it('records only pathname and never a query string (AC-04)', () => {
    // Even if a caller passed a full location, we only ever build from pathname;
    // simulate that the caller supplied the pathname (as installErrorBeacon does).
    const payload = buildClientErrorPayload(new Error('oops'), '/room');
    expect(payload.path).toBe('/room');
    expect(payload.path).not.toContain('?');
    expect(payload.path).not.toContain('nickname');
  });

  it('narrows a non-Error rejection to a safe message with no stack', () => {
    const payload: ClientErrorPayload = buildClientErrorPayload('string rejection', '/');
    expect(payload.message).toBe('string rejection');
    expect(payload.stack).toBeNull();
    expect(payload.path).toBe('/');
  });

  it('falls back to a generic message for an opaque throw', () => {
    const payload = buildClientErrorPayload(undefined, '/join');
    expect(payload.message).toBe('Unknown client error');
    expect(payload.stack).toBeNull();
  });

  it('uses the error name when the message is empty', () => {
    const err = new Error('');
    const payload = buildClientErrorPayload(err, '/');
    expect(payload.message).toBe('Error');
  });

  it('collapses the /join/:code deep link so the join code never leaks (AC-04)', () => {
    // The deep-link route "/join/ABCD" carries the join CODE in its pathname; the
    // payload must reduce it to the "/join" route template, dropping the code.
    const payload = buildClientErrorPayload(new Error('boom on a deep link'), '/join/ABCD');
    expect(payload.path).toBe('/join');
    expect(payload.path).not.toContain('ABCD');
  });
});

describe('normalizeRoutePath', () => {
  it('keeps only the top-level route segment, dropping any dynamic tail', () => {
    expect(normalizeRoutePath('/join/ABCD')).toBe('/join');
    expect(normalizeRoutePath('/join/ABCD/extra')).toBe('/join');
    expect(normalizeRoutePath('/solo')).toBe('/solo');
    expect(normalizeRoutePath('/')).toBe('/');
    expect(normalizeRoutePath('')).toBe('/');
  });
});
