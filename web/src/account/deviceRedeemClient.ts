// ----------------------------------------------------------------------------
//  deviceRedeemClient.ts - the UNAUTHENTICATED web client for redeeming and
//  refreshing a family-device token (accounts-identity/09, AC-02).
//
//  WHY UNAUTHENTICATED: a kid's device is never signed in (README section 6) -
//  it never holds `PurchaserSession`'s in-memory purchaser credential. Both
//  calls here send NO Authorization header (a bare code / token in the request
//  body IS the credential for this exchange, per the story's Technical Notes -
//  "the code travels in the request BODY, never a URL path segment").
//
//  Mirrors publishTale.ts / signInClient.ts: API base from
//  `import.meta.env.VITE_API_BASE_URL` (never hardcoded), fails GRACEFULLY -
//  a network error, non-OK status, or unparseable body resolves to a friendly
//  `{ ok: false }` rather than throwing, so the redeem screen / the app-launch
//  refresh call never crash the surrounding UI.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** Result of redeeming a parent-issued link code (POST /api/accounts/devices/redeem). */
export interface RedeemDeviceResult {
  ok: boolean;
  /** A friendly message: a success confirmation, or why the code did not work. */
  message: string;
  /** The newly minted family-device token, present only on ok:true. Persist via familyDeviceToken.ts. */
  token?: string;
  /** The short, random, non-identifying device label (e.g. "quiet fox"), present only on ok:true. */
  label?: string;
}

/** Result of the once-per-launch silent token refresh (POST /api/accounts/devices/refresh). */
export interface RefreshDeviceTokenResult {
  ok: boolean;
  /** The rotated replacement token, present only on ok:true. */
  token?: string;
  /**
   * True ONLY when the server was reached AND authoritatively reported the token is
   * dead (a 200 response whose body says ok:false - revoked / expired / unknown). The
   * caller clears the stored token ONLY on this signal. A transient failure (network
   * blip, 5xx, a 429 from the global throttle, an unparseable body) leaves this false
   * so a still-valid token is NEVER discarded on a passing hiccup at app launch (a kid's
   * device never signs in, so an accidental unlink would force a re-link - the exact
   * "buy once, link once" promise this must not break).
   */
  dead: boolean;
}

const UNAVAILABLE_MESSAGE = "We couldn't reach the game just now - please try again in a moment.";

function asRedeemResult(value: unknown): { ok: boolean; message: string; token?: string; label?: string } | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (typeof r.ok !== 'boolean' || typeof r.message !== 'string') return null;
  const token = typeof r.token === 'string' && r.token.length > 0 ? r.token : undefined;
  const label = typeof r.label === 'string' && r.label.length > 0 ? r.label : undefined;
  return { ok: r.ok, message: r.message, token, label };
}

function asRefreshResult(value: unknown): { ok: boolean; token?: string } | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (typeof r.ok !== 'boolean') return null;
  const token = typeof r.token === 'string' && r.token.length > 0 ? r.token : undefined;
  return { ok: r.ok, token };
}

/**
 * Redeems a parent-issued link code for a new family-device token (AC-02).
 * The code travels in the request body, never a URL path segment. Resolves a
 * friendly failure on any transport/parse error - never throws. On ok:true the
 * caller (RedeemDevice.tsx) persists `token` via familyDeviceToken.ts.
 */
export async function redeemDeviceLinkCode(code: string): Promise<RedeemDeviceResult> {
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/accounts/devices/redeem`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code }),
    });

    if (!response.ok) {
      return { ok: false, message: UNAVAILABLE_MESSAGE };
    }

    const body: unknown = await response.json();
    const parsed = asRedeemResult(body);
    if (!parsed) {
      return { ok: false, message: UNAVAILABLE_MESSAGE };
    }
    return parsed;
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE };
  }
}

/**
 * Silently rotates a stored family-device token (keeping the rolling TTL alive),
 * called once per app launch by useGameHub.ts when a token is present and no
 * purchaser is signed in. Never throws. The result distinguishes:
 *   - ok:true            -> rotated; the caller persists the new `token`.
 *   - ok:false, dead:true  -> the server was reached and said the token is dead
 *                             (revoked / expired / unknown); the caller clears it.
 *   - ok:false, dead:false -> a TRANSIENT failure (network, 5xx, a 429 from the
 *                             global throttle, an unparseable body); the caller
 *                             KEEPS the token and simply tries again next launch.
 * Only a definitive 200-with-ok:false is treated as dead, so a passing hiccup never
 * unlinks a valid device (WR-001 - a kid's device cannot re-sign-in, so an accidental
 * unlink would force a manual re-link).
 */
export async function refreshFamilyDeviceToken(token: string): Promise<RefreshDeviceTokenResult> {
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/accounts/devices/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token }),
    });

    if (!response.ok) {
      // Transport-level failure (5xx, a 429 from the redeem/refresh throttle, etc.) -
      // NOT a signal that the token is dead. Keep it.
      return { ok: false, dead: false };
    }

    const body: unknown = await response.json();
    const parsed = asRefreshResult(body);
    if (!parsed) {
      // Reached the server but could not parse the body - treat as transient, keep the token.
      return { ok: false, dead: false };
    }
    if (parsed.ok && parsed.token) {
      return { ok: true, token: parsed.token, dead: false };
    }
    // A parsed 200 body with ok:false is the ONLY definitive dead-token signal.
    return { ok: false, dead: true };
  } catch {
    // Network error / offline at launch - transient, keep the token.
    return { ok: false, dead: false };
  }
}
