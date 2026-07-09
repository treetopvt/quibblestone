// ----------------------------------------------------------------------------
//  stripeModeClient.ts - the OPERATOR-CONSOLE web client for the Stripe live/test
//  mode surface (sysadmin-console/04). A thin REST client, NOT the feature: the
//  mode-aware credential resolution, the persisted active-mode flag, and the
//  guarded flip all live server-side (StripeModeController, behind story 01's
//  "Operator" policy). This module only GETs the current mode and POSTs a flip.
//
//  ONE CONSOLE, ONE AUTH (sysadmin-console/04): this REPLACES the interim
//  billing/stripeModeClient.ts (deleted), which carried an X-Operator-Secret shared
//  secret. There is NO secret header any longer: the operator is authenticated the
//  SAME way every other admin screen is - the in-memory operator credential is
//  presented as `Authorization: Bearer` (the PRIMARY path on a cross-ORIGIN
//  deployment, where the HttpOnly SameSite cookie is never sent) AND
//  `credentials: 'include'` keeps a same-site cookie riding along. Mirrors
//  purchasersClient.ts / reportedTalesClient.ts exactly.
//
//  SEPARATE ADMIN BUNDLE (from story 01): this file lives in the admin bundle and
//  imports NOTHING from the kid app (pages / signalr / gallery / engine /
//  components), and the kid app imports nothing from here. The API base URL comes
//  from `import.meta.env.VITE_API_BASE_URL` (never hardcoded, never a secret). Every
//  call FAILS GRACEFULLY - a network error, non-OK status, or unparseable body
//  resolves to a friendly typed result rather than throwing, so the panel never
//  shows a raw error and never guesses/defaults a mode silently.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** The two Stripe modes the app can be running against. */
export type StripeMode = 'test' | 'live';

/** The distinct outcomes a call against the admin mode endpoints can resolve to. */
export type StripeModeOutcome = 'ok' | 'unauthorized' | 'error';

/** Result of reading the current active mode (GET /api/admin/stripe-mode). */
export interface StripeModeStatusResult {
  outcome: StripeModeOutcome;
  /** Present only when outcome is 'ok'. */
  activeMode?: StripeMode;
  /** Present only when outcome is 'ok'. Null when the mode has never been changed. */
  lastChangedUtc?: string | null;
  /** Present only when outcome is 'ok': whether billing is configured at all. */
  enabled?: boolean;
  /** A friendly message for a non-'ok' outcome. */
  message?: string;
}

/** Result of flipping the active mode (POST /api/admin/stripe-mode). */
export interface StripeModeSwitchResult {
  outcome: StripeModeOutcome;
  /** Present only when outcome is 'ok'. */
  activeMode?: StripeMode;
  /** Present only when outcome is 'ok'. */
  lastChangedUtc?: string;
  /** A friendly message for a non-'ok' outcome. */
  message?: string;
}

/** Friendly fallback shown when the endpoint cannot be reached or the body is unparseable. */
const UNAVAILABLE_MESSAGE = 'We could not reach the billing-mode service just now - please try again in a moment.';

/** Friendly message for a 401 (no valid operator session). */
const UNAUTHORIZED_MESSAGE = 'Your operator session was not accepted - please sign in again.';

/** The API base URL, from a NON-secret VITE_ var (the allowlist / keys are never here). */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;

/** The mode strings the server may return; anything else is treated as an error rather than trusted. */
const KNOWN_MODES: readonly StripeMode[] = ['test', 'live'];

function asStripeMode(value: unknown): StripeMode | null {
  return typeof value === 'string' ? KNOWN_MODES.find((known) => known === value) ?? null : null;
}

/** Narrows an unknown parsed body from the GET status endpoint. */
function asStatusBody(
  value: unknown,
): { activeMode: StripeMode; lastChangedUtc: string | null; enabled: boolean } | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  const activeMode = asStripeMode(record.activeMode);
  if (!activeMode || typeof record.enabled !== 'boolean') return null;
  const lastChangedUtc = typeof record.lastChangedUtc === 'string' ? record.lastChangedUtc : null;
  return { activeMode, lastChangedUtc, enabled: record.enabled };
}

/** Narrows an unknown parsed body from the POST switch endpoint. */
function asSwitchBody(value: unknown): { activeMode: StripeMode; lastChangedUtc: string } | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  const activeMode = asStripeMode(record.activeMode);
  if (!activeMode || typeof record.lastChangedUtc !== 'string') return null;
  return { activeMode, lastChangedUtc: record.lastChangedUtc };
}

/** Builds the operator-credential headers shared by every call (bearer + optional content type). */
function buildHeaders(credential: string | null, withBody: boolean): HeadersInit | undefined {
  const headers: Record<string, string> = {};
  if (withBody) headers['Content-Type'] = 'application/json';
  if (credential) headers.Authorization = `Bearer ${credential}`;
  return Object.keys(headers).length > 0 ? headers : undefined;
}

/**
 * Reads the currently active Stripe mode (GET /api/admin/stripe-mode), presenting the
 * operator credential as a bearer (cross-ORIGIN path) with the cookie riding along.
 * Resolves 'unauthorized' on a 401 (never a guessed/blank mode), 'error' on any other
 * failure (network, non-OK status, or an unparseable body), and 'ok' with the parsed
 * status otherwise. Never throws.
 */
export async function fetchStripeMode(credential: string | null): Promise<StripeModeStatusResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/admin/stripe-mode`, {
      headers: buildHeaders(credential, false),
      credentials: 'include',
    });
    if (response.status === 401) {
      return { outcome: 'unauthorized', message: UNAUTHORIZED_MESSAGE };
    }
    if (!response.ok) {
      return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
    }
    const parsed = asStatusBody(await response.json().catch(() => null));
    if (!parsed) {
      return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
    }
    return { outcome: 'ok', ...parsed };
  } catch {
    return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
  }
}

/**
 * Switches the active Stripe mode (POST /api/admin/stripe-mode), presenting the
 * operator credential as a bearer and `{ mode }` as the body. This is the confirmed
 * flip itself - the caller shows the confirmation step BEFORE calling this. Resolves
 * 'unauthorized' on a 401, 'error' on any other failure (including a 400 for an invalid
 * mode), and 'ok' with the new mode + timestamp otherwise. Never throws.
 */
export async function setStripeMode(credential: string | null, mode: StripeMode): Promise<StripeModeSwitchResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/admin/stripe-mode`, {
      method: 'POST',
      headers: buildHeaders(credential, true),
      credentials: 'include',
      body: JSON.stringify({ mode }),
    });
    if (response.status === 401) {
      return { outcome: 'unauthorized', message: UNAUTHORIZED_MESSAGE };
    }
    if (!response.ok) {
      return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
    }
    const parsed = asSwitchBody(await response.json().catch(() => null));
    if (!parsed) {
      return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
    }
    return { outcome: 'ok', ...parsed };
  } catch {
    return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
  }
}
