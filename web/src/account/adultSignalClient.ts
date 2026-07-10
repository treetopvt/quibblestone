// ----------------------------------------------------------------------------
//  adultSignalClient.ts - the READ-ONLY web client that asks the server whether
//  THIS device carries an adult-unlock signal (accounts-identity/10, issue #247).
//
//  WHAT IT IS FOR: solo play is client-driven with no server session, so its
//  teen-plus content tier was gated only by a UI-only family-safe toggle a kid
//  could flip. This module resolves the SAME server-side adult signal group play
//  resolves at CreateRoom - a purchaser credential (adult-by-construction) or an
//  adult-confirmed family-device token -> unlocked; anything else -> family-safe -
//  by calling GET /api/accounts/adult-signal ONCE on the solo setup screen. Solo
//  then honors its family-safe toggle ONLY when this resolves true (mirroring
//  group play's `room.AdultUnlocked ? familySafe : true`, client-side).
//
//  THE CREDENTIAL: this presents the SAME two sources the hub's accessTokenFactory
//  prefers (useGameHub.ts: `purchaserCredentialRef.current ?? familyDeviceTokenRef
//  .current`) - a live purchaser credential if signed in, else a stored
//  family-device token - as `Authorization: Bearer`, with `credentials: 'include'`
//  so a same-site HttpOnly purchaser cookie rides along too. The caller resolves
//  the credential (usePurchaserSession()'s credential, then loadFamilyDeviceToken())
//  and passes it here, rather than this module inventing a third way to ask "what
//  does this device hold." A bearer NEVER travels in the URL (handles are secrets -
//  a path/query segment leaks to logs / App Insights / Referer, ADR 0003).
//
//  FAIL-SAFE, NON-NEGOTIABLE (AC-04 - this is a child-safety seam): the default IS
//  the safe state. A network error, a timeout, a non-2xx, or an unparseable body
//  EACH resolve to `false` (family-safe) - this function NEVER throws and NEVER
//  returns true on anything but a positive, freshly parsed `{ adultUnlocked: true }`.
//  Offline / cold-cache solo therefore stays family-safe with no special-casing.
//
//  HONEST SCOPE (AC-07): this is an identity-aware CLIENT NUDGE, not a structural
//  "can never." The teen-plus templates remain bundled in the web build and cached
//  offline regardless of this signal, so a determined, technically capable kid can
//  still reach them by overriding the client-held boolean or reading the cached
//  bundle directly - the same bundled-content caveat group play's server-side
//  Room-based gate does not have to make. Closing that residual gap is the Option-B
//  content-supply escalation the story tracks, deliberately out of scope here.
//
//  Mirrors deviceRedeemClient.ts / cloudGalleryClient.ts: API base from
//  `import.meta.env.VITE_API_BASE_URL` (never hardcoded), every failure caught and
//  turned into the safe default rather than thrown.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

// A tight client-side deadline (AC-04 defence in depth): the gate defaults to false,
// so a stalled server already keeps solo family-safe - but bounding the request means a
// hung endpoint resolves to the safe default PROMPTLY (via an AbortError the catch below
// turns into false) instead of leaving the promise pending for the browser's own long
// default timeout. Short, because this runs once on the solo setup screen's mount.
const REQUEST_TIMEOUT_MS = 5000;

/** Narrows an unknown response body to its boolean adultUnlocked field, or null if malformed. */
function readAdultUnlocked(value: unknown): boolean | null {
  if (typeof value !== 'object' || value === null) return null;
  const unlocked = (value as Record<string, unknown>).adultUnlocked;
  return typeof unlocked === 'boolean' ? unlocked : null;
}

/**
 * Resolves this device's adult-unlock signal from the server (GET
 * /api/accounts/adult-signal), presenting `credential` (a purchaser credential or
 * a family-device token, or null for an anonymous device) as a bearer. Resolves
 * `true` ONLY on a positive, freshly parsed response; EVERY other outcome - no
 * credential, a non-2xx, an unparseable body, a network error / timeout / offline -
 * resolves to `false` (family-safe, AC-04). Never throws.
 */
export async function resolveAdultSignal(credential: string | null): Promise<boolean> {
  // Bound the request so a stalled server resolves to the safe default promptly (the
  // abort surfaces as an AbortError the catch turns into false). clearTimeout in finally
  // so a fast response never leaves a dangling timer.
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    // The bearer rides the Authorization header (never the URL); credentials:'include'
    // lets a same-site HttpOnly purchaser cookie serve as the fallback the server reads.
    const headers: Record<string, string> = {};
    if (credential !== null && credential.length > 0) {
      headers.Authorization = `Bearer ${credential}`;
    }

    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/accounts/adult-signal`, {
      method: 'GET',
      headers,
      credentials: 'include',
      signal: controller.signal,
    });

    // A non-2xx (401 / 429 / 5xx) is NOT an adult signal - fail safe to family-safe.
    if (!response.ok) {
      return false;
    }

    const body: unknown = await response.json();
    // Only a definitive, freshly parsed `true` unlocks; a malformed body defaults false.
    return readAdultUnlocked(body) === true;
  } catch {
    // Network error / offline / timeout / abort - the safe default, never a throw (AC-04).
    return false;
  } finally {
    clearTimeout(timeout);
  }
}
