// ----------------------------------------------------------------------------
//  deviceId.ts - the anonymous, device-local id for product-usage REACH
//  (platform-devops/05, AC-03).
//
//  WHAT THIS ANSWERS (and its hard limit): "how many are playing" has no honest
//  answer in QuibbleStone beyond an APPROXIMATE DEVICE COUNT, because there are NO
//  accounts and NO PII (README section 3): players are anonymous. So reach is a
//  random GUID minted once per device and kept in localStorage - attached to the
//  SOLO usage events so App Insights can dcount() distinct devices. It is a DEVICE
//  count, NEVER a verified unique person: it resets when the player clears storage,
//  a shared device counts once, and one person on two devices counts twice. True
//  unique-user identity is explicitly DEFERRED to accounts (Phase 2) - this module
//  makes NO attempt at cross-device dedupe (see docs/features/platform-devops/05).
//
//  POSTURE (mirrors identity.ts + serveLog.ts): device-local, anonymous, account-
//  free, versioned key, and EVERY storage access wrapped in try/catch because
//  localStorage can throw or be absent (private-browsing, disabled storage, quota,
//  SSR). The stored value is VALIDATED on load (isValidDeviceId) and never trusted
//  blindly - a corrupt/empty entry is replaced rather than mis-used, and we never
//  reach for a non-null assertion. The id itself is minted with safeUuid (reused
//  from serveLog.ts so we do not duplicate the crypto.randomUUID guard, which is
//  required on http-over-LAN where randomUUID is undefined - "different houses,
//  same car").
//
//  NOT PII, NOT A SECRET: this is an opaque telemetry key. It is never shown to a
//  player, never tied to a name, and only ever leaves the device as the anonymous
//  "deviceId" property on a usage beacon (which the server scrubber explicitly
//  allows - it is a device-count key, not a player session id).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { safeUuid } from './serveLog';

// Versioned key: bump the suffix if the stored shape ever changes so an old entry
// is simply replaced rather than mis-read (mirrors identity.ts's STORAGE_KEY).
const STORAGE_KEY = 'qs.telemetry.deviceId.v1';

/**
 * True when the value is a usable device id: a non-empty string. PURE (no window
 * access) so the robustness guarantee is unit-testable without a DOM. We keep the
 * validity bar deliberately low (any non-empty string) because the id is an opaque
 * telemetry key, not a structured value - the point is to reject null / non-string
 * / empty entries a corrupt or cleared store might hand back, not to police format.
 */
export function isValidDeviceId(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

/**
 * Returns the anonymous device-local id, minting one on first use and reusing it
 * thereafter (AC-03). NEVER throws: if storage is unavailable or holds a corrupt
 * value, it falls back to minting a fresh id (transient when the write itself
 * fails) so a usage beacon can still carry an anonymous reach key without ever
 * breaking the flow. safeUuid never throws, so this fallback is itself safe.
 */
export function getOrCreateDeviceId(): string {
  try {
    const existing = window.localStorage.getItem(STORAGE_KEY);
    if (isValidDeviceId(existing)) {
      return existing;
    }
    const minted = safeUuid();
    window.localStorage.setItem(STORAGE_KEY, minted);
    return minted;
  } catch {
    // Storage disabled / quota / SSR: fall back to a transient, still-anonymous id.
    return safeUuid();
  }
}
