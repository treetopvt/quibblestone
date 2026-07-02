// ----------------------------------------------------------------------------
//  usageBeacon.ts - the web half of the anonymous PRODUCT-USAGE events for SOLO
//  rounds (platform-devops/05, AC-01/AC-02).
//
//  A GROUP round records its usage events server-side, straight from GameHub. A
//  SOLO round is a single browser tab with no SignalR round-trip, so it
//  fire-and-forgets a tiny POST to /api/usage here - on round START (mode) and on
//  round COMPLETE (mode + duration) - which the server forwards to App Insights
//  via TelemetryClient (see api/src/Controllers/UsageController.cs). That keeps the
//  App Insights connection string SERVER-SIDE (never a VITE_ var, never in the
//  browser - README section 6, AC-05) and routes every solo usage event through
//  the SAME PII scrubber as all other telemetry (AC-04). It mirrors errorBeacon.ts
//  (the client-error beacon) and rides story 04's App Insights pipeline - NOT a
//  third telemetry stack (AC-06): serveLog.ts (story-selection/04) posts to a
//  DIFFERENT sink (Table Storage) for content curation.
//
//  NO PII (AC-04, README section 6): the body carries ONLY anonymous facts - the
//  stable enum-ish mode id, an optional duration, and an anonymous device-local id
//  (AC-03, an approximate DEVICE count, never a person). There is NO field for a
//  nickname or code (solo has neither), no query string, nothing traceable. The
//  pure payload builder below is the guarantee: it reads ONLY the mode + duration
//  + the anonymous device id, so there is no field through which PII could travel.
//
//  FIRE-AND-FORGET / NEVER BLOCKS A ROUND (AC-08): sendBeacon (with a fetch
//  keepalive fallback) is best-effort - a failure is swallowed, there is no retry,
//  and nothing here can wedge or delay the round. Config: the API base URL comes
//  from import.meta.env.VITE_API_BASE_URL (the existing pattern in errorBeacon.ts
//  and serveLog.ts) - never hardcoded.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { getOrCreateDeviceId } from './deviceId';

/** The two anonymous usage event types (mirrors the server's UsageTelemetry). */
export type UsageEventType = 'RoundStarted' | 'RoundCompleted';

/** The anonymous usage payload. These fields are the WHOLE shape - there is
 *  intentionally no field that could carry PII (AC-04). Mirrors the server's
 *  UsageEventRequest (api/src/Controllers/UsageController.cs). */
export interface UsagePayload {
  /** "RoundStarted" or "RoundCompleted". */
  eventType: UsageEventType;
  /** The stable, enum-ish mode id (e.g. "classic-blind") - never free text. */
  mode: string;
  /** The round/session duration in ms (RoundCompleted only), else null. */
  durationMs: number | null;
  /** The anonymous device-local id (AC-03) - a device count, never a person. */
  deviceId: string;
}

/** The API path the beacon posts to (joined onto VITE_API_BASE_URL). */
const USAGE_ENDPOINT = '/api/usage';

/**
 * Builds the anonymous usage payload from a mode + optional duration + device id.
 * PURE and testable (this is the AC-04 guarantee): it reads ONLY those three
 * anonymous inputs - never a nickname, never a code, never any identity - and a
 * completion carries a non-negative duration (a start carries null). Exported so
 * the no-PII shape is asserted by a unit test, not just by comment.
 */
export function buildUsagePayload(
  eventType: UsageEventType,
  mode: string,
  deviceId: string,
  durationMs?: number,
): UsagePayload {
  return {
    eventType,
    mode,
    durationMs:
      eventType === 'RoundCompleted' && typeof durationMs === 'number'
        ? Math.max(0, durationMs)
        : null,
    deviceId,
  };
}

/**
 * Fire-and-forget: sends the payload to the API via navigator.sendBeacon, with a
 * fetch keepalive fallback. Never awaited, never retried, every failure swallowed
 * - a usage beacon must never surface to a player or wedge/delay a round (AC-08).
 * Mirrors errorBeacon.ts's sendClientError transport exactly.
 */
function sendUsage(payload: UsagePayload): void {
  const url = `${import.meta.env.VITE_API_BASE_URL}${USAGE_ENDPOINT}`;
  const body = JSON.stringify(payload);

  try {
    // sendBeacon is the ideal transport (survives page unload, no response to
    // handle). It exists in browsers but not in every test/SSR environment, so
    // guard it and fall back to fetch keepalive.
    if (typeof navigator !== 'undefined' && typeof navigator.sendBeacon === 'function') {
      const blob = new Blob([body], { type: 'application/json' });
      navigator.sendBeacon(url, blob);
      return;
    }
  } catch {
    // fall through to fetch below
  }

  try {
    void fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body,
      keepalive: true,
    }).catch(() => {
      // Best-effort: a down / slow / unreachable endpoint is a no-op here.
    });
  } catch {
    // Swallowed: telemetry must never surface to a player or wedge the app.
  }
}

/**
 * Fire-and-forget a solo "RoundStarted" usage event (AC-01) - the MODE played, in
 * solo context, with the anonymous device id for reach. Returns void immediately;
 * the caller never awaits it and it never blocks the round (AC-08).
 */
export function recordSoloRoundStarted(mode: string): void {
  sendUsage(buildUsagePayload('RoundStarted', mode, getOrCreateDeviceId()));
}

/**
 * Fire-and-forget a solo "RoundCompleted" usage event (AC-02) - the MODE plus the
 * measured session DURATION (ms), in solo context, with the anonymous device id.
 * Returns void immediately; the caller never awaits it and it never blocks the
 * transition to the reveal (AC-08).
 */
export function recordSoloRoundCompleted(mode: string, durationMs: number): void {
  sendUsage(buildUsagePayload('RoundCompleted', mode, getOrCreateDeviceId(), durationMs));
}
