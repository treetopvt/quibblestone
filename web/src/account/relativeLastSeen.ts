// ----------------------------------------------------------------------------
//  relativeLastSeen.ts - a pure, unit-testable formatter for a linked
//  device's "last seen" row on the Account page (accounts-identity/09, AC-04).
//
//  AC-04 asks for a RELATIVE last-seen time ("used 2 hours ago") and, for a
//  device that has never resolved a room since it was linked, the friendly
//  "never used since linking" copy - never an absolute timestamp and never
//  anything device-identifying (no IP, no user agent - this function only
//  ever sees a nullable ISO timestamp).
//
//  Kept as a small pure function (no Date.now() baked in - `now` is injected,
//  defaulting to `new Date()`) so it is testable without faking the system
//  clock, matching the posture of this file's siblings (useGameHub.ts's
//  manualReconnectDelayMs, App.tsx's shouldHoldLiveRouteForResume).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

/**
 * Formats a device's `lastUsedUtc` into a short, relative, kid-and-parent
 * readable string. `null` (never used since linking) reads as exactly that.
 * A malformed/unparseable timestamp falls back to the same "never used"
 * copy rather than showing a broken date (never crash a device row over a
 * bad string).
 */
export function formatRelativeLastSeen(lastUsedUtc: string | null, now: Date = new Date()): string {
  if (lastUsedUtc === null) {
    return 'never used since linking';
  }

  const then = new Date(lastUsedUtc);
  const thenMs = then.getTime();
  if (Number.isNaN(thenMs)) {
    return 'never used since linking';
  }

  const deltaMs = now.getTime() - thenMs;
  if (deltaMs < MINUTE_MS) {
    return 'used moments ago';
  }
  if (deltaMs < HOUR_MS) {
    const minutes = Math.floor(deltaMs / MINUTE_MS);
    return `used ${minutes} minute${minutes === 1 ? '' : 's'} ago`;
  }
  if (deltaMs < DAY_MS) {
    const hours = Math.floor(deltaMs / HOUR_MS);
    return `used ${hours} hour${hours === 1 ? '' : 's'} ago`;
  }
  const days = Math.floor(deltaMs / DAY_MS);
  return `used ${days} day${days === 1 ? '' : 's'} ago`;
}
