// ----------------------------------------------------------------------------
//  usageBeacon.test.ts - proves the anonymous product-usage payload carries NO
//  PII (platform-devops/05, AC-04). A usage event exists to answer "which modes
//  get played + how long a session lasts + approximate device reach" WITHOUT ever
//  shipping a nickname, code, or any identity. We test the PURE payload builder so
//  the guarantee is asserted, not just asserted-by-comment (mirrors
//  errorBeacon.test.ts).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { buildUsagePayload, type UsagePayload } from './usageBeacon';

describe('buildUsagePayload', () => {
  it('carries ONLY eventType, mode, durationMs, and deviceId - no PII fields (AC-04)', () => {
    const payload = buildUsagePayload('RoundStarted', 'classic-blind', 'device-abc');

    // The shape is exactly the four anonymous fields - nothing else.
    expect(Object.keys(payload).sort()).toEqual(['deviceId', 'durationMs', 'eventType', 'mode']);

    // Defensively assert no identity-bearing key can sneak in.
    const forbidden = [
      'nickname',
      'name',
      'code',
      'joinCode',
      'roomCode',
      'sessionId',
      'playerId',
      'connectionId',
      'word',
      'query',
      'ip',
    ];
    for (const key of forbidden) {
      expect(payload as unknown as Record<string, unknown>).not.toHaveProperty(key);
    }
  });

  it('records the stable mode id verbatim and no free text beyond it', () => {
    const payload = buildUsagePayload('RoundStarted', 'word-bank', 'device-1');
    expect(payload.mode).toBe('word-bank');
    expect(payload.eventType).toBe('RoundStarted');
  });

  it('a RoundStarted carries a null duration (a start has no session length yet)', () => {
    const payload: UsagePayload = buildUsagePayload('RoundStarted', 'classic-blind', 'device-1', 1234);
    // Even if a caller passes a duration on a start, it is not recorded.
    expect(payload.durationMs).toBeNull();
  });

  it('a RoundCompleted carries the measured duration (AC-02)', () => {
    const payload = buildUsagePayload('RoundCompleted', 'classic-blind', 'device-1', 4200);
    expect(payload.durationMs).toBe(4200);
  });

  it('clamps a negative duration to zero (clock skew never records a negative length)', () => {
    const payload = buildUsagePayload('RoundCompleted', 'classic-blind', 'device-1', -50);
    expect(payload.durationMs).toBe(0);
  });

  it('a RoundCompleted with no duration supplied records a null (defensive)', () => {
    const payload = buildUsagePayload('RoundCompleted', 'classic-blind', 'device-1');
    expect(payload.durationMs).toBeNull();
  });
});
