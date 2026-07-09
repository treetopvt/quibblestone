// ----------------------------------------------------------------------------
//  familyDeviceToken.ts - device-local persistence for a redeemed family-device
//  token (accounts-identity/09, "family device link").
//
//  WHAT THIS IS: a kid's device is never signed in (README section 6 - kids
//  stay anonymous forever), so it cannot hold accounts-identity/03's in-memory
//  `PurchaserSession`. Instead, a parent mints a short-lived link code from the
//  Account page (see `linkedDevicesClient.ts`), the kid's device redeems it
//  ONCE (`/link-device`, see `RedeemDevice.tsx`) for a long-lived, individually
//  revocable opaque token, and THIS module is where that token is remembered
//  between app launches.
//
//  DELIBERATELY localStorage, NOT in-memory (unlike `PurchaserSession.tsx`,
//  which is intentionally in-memory-only so a purchaser credential never
//  outlives a tab): the whole point of a family-device token is surviving app
//  restarts/reloads on a kid's own device with no supervising adult re-signing
//  in each time (the story's Technical Notes, "web:" section). The mitigation
//  for that more persistent storage is the token's own shape - individually
//  revocable server-side by row (AC-04), a rolling TTL, and silent rotation on
//  each use (see useGameHub.ts's once-per-launch refresh call) - not a lighter
//  persistence posture here.
//
//  Mirrors reconnect.ts / identity.ts: a single versioned localStorage key, a
//  raw string value (no JSON envelope needed for one opaque token), and every
//  access wrapped in try/catch so private browsing / disabled storage / quota
//  never throws - callers just see "no token" and fall back to anonymous play.
//
//  NO PII (AC-05 of the story): the token is an opaque, server-minted secret
//  string carrying no player-identifying data of its own; this module never
//  inspects or decodes it, only stores/retrieves/clears it.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

// Versioned key: bump the suffix if the stored shape ever changes. Deliberately
// distinct from identity.ts's `qs.identity.v1` and reconnect.ts's
// `qs.reconnect.v1` - three separate device-local concerns, never conflated.
const STORAGE_KEY = 'qs.familyDeviceToken.v1';

/**
 * Load the stored family-device token, or null when there is none, storage is
 * unavailable, or the stored value is empty/malformed. Never throws.
 */
export function loadFamilyDeviceToken(): string | null {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return raw !== null && raw.length > 0 ? raw : null;
  } catch {
    // Storage unavailable / disabled / quota - treat as "no token" (falls
    // back to anonymous play, exactly as a device that was never linked).
    return null;
  }
}

/**
 * Remember a redeemed (or freshly rotated) family-device token, overwriting
 * whatever was stored before - a device holds at most one token at a time.
 * Silently no-ops if storage is unavailable; persistence here is a
 * convenience the token's own server-side rolling TTL already accounts for.
 */
export function saveFamilyDeviceToken(token: string): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, token);
  } catch {
    // Ignore: a failed write just means this device stays anonymous.
  }
}

/**
 * Forget the stored family-device token (a revoked / dead / rejected token,
 * per useGameHub.ts's once-per-launch refresh call). Silently no-ops if
 * storage is unavailable.
 */
export function clearFamilyDeviceToken(): void {
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Ignore: nothing to clear if storage never worked in the first place.
  }
}
