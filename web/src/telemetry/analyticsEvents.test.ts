// ----------------------------------------------------------------------------
//  analyticsEvents.test.ts - proves the GA4 event params carry NO PII
//  (analytics/01, AC-02). GA4 is a THIRD-PARTY sink with no server scrubber in
//  front of it, so the browser is the only enforcement point - and buildEventParams
//  is a DEFAULT-DENY allowlist. We test that only the allowlisted keys survive and
//  any identity-shaped field is dropped, mirroring usageBeacon.test.ts.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { ANALYTICS_EVENTS, buildEventParams } from './analyticsEvents';

describe('buildEventParams', () => {
  it('keeps only the allowlisted anonymous params', () => {
    const params = buildEventParams({
      mode: 'classic-blind',
      context: 'group',
      reaction: 'laugh',
      method: 'copy-link',
      players: 4,
    });
    expect(Object.keys(params).sort()).toEqual(['context', 'method', 'mode', 'players', 'reaction']);
    expect(params.mode).toBe('classic-blind');
    expect(params.context).toBe('group');
    expect(params.players).toBe(4);
  });

  it('drops any identity-shaped field a caller tries to smuggle in (AC-02)', () => {
    // A deliberately loosened, PII-shaped input - the exact thing that must never
    // reach GA4. Only `mode` is allowlisted; everything else is stripped.
    const params = buildEventParams({
      mode: 'word-bank',
      nickname: 'Bob',
      name: 'Bob Smith',
      word: 'banana',
      story: 'once upon a time',
      code: 'ABCD',
      joinCode: 'ABCD',
      sessionId: 'sess-123',
      playerId: 'p-1',
      ip: '10.0.0.1',
    });

    expect(Object.keys(params)).toEqual(['mode']);
    const forbidden = [
      'nickname',
      'name',
      'word',
      'story',
      'code',
      'joinCode',
      'sessionId',
      'playerId',
      'ip',
    ];
    for (const key of forbidden) {
      expect(params).not.toHaveProperty(key);
    }
  });

  it('drops an empty or over-long string value (no free text can ride an event)', () => {
    expect(buildEventParams({ mode: '' })).not.toHaveProperty('mode');
    expect(buildEventParams({ mode: 'x'.repeat(41) })).not.toHaveProperty('mode');
    // A value at the cap is kept.
    expect(buildEventParams({ mode: 'x'.repeat(40) }).mode).toBe('x'.repeat(40));
  });

  it('clamps players to a small non-negative integer (a count, never a fingerprint)', () => {
    expect(buildEventParams({ players: 1000 }).players).toBe(99);
    expect(buildEventParams({ players: 3.7 }).players).toBe(3);
    // A negative or non-finite count is dropped, not recorded.
    expect(buildEventParams({ players: -5 })).not.toHaveProperty('players');
    expect(buildEventParams({ players: Number.NaN })).not.toHaveProperty('players');
  });

  it('returns an empty object for empty input (an event may carry no params)', () => {
    expect(buildEventParams({})).toEqual({});
  });

  it('exposes a fixed, stable set of snake_case event names', () => {
    // A closed set - an event name is never assembled from player-supplied text.
    expect(ANALYTICS_EVENTS.RoundStarted).toBe('round_started');
    expect(ANALYTICS_EVENTS.RevealReached).toBe('reveal_reached');
    expect(Object.values(ANALYTICS_EVENTS).every((name) => /^[a-z_]+$/.test(name))).toBe(true);
  });
});
