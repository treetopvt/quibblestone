// ----------------------------------------------------------------------------
//  stripeModeClient.ts - the web client for the OPERATOR-ONLY Stripe live/test
//  mode surface (billing-entitlements/07, the UI half of story 06's endpoint).
//  A thin REST client, NOT the feature: the mode-aware credential resolution,
//  the persisted active-mode flag, and the guarded flip all live server-side
//  (story 06 - api/src/Billing + a new admin-scoped controller). This module
//  only GETs the current mode and POSTs a flip, both carrying the operator
//  secret as the `X-Operator-Secret` header.
//
//  INTERIM GATE (temporary, pending sysadmin-console/01 / #135): there is no
//  operator login yet. Story 06 documents a thin server-side shared secret,
//  compared with a constant-time check, required as a header on both admin
//  endpoints. This client carries that secret ONLY in memory, for the one
//  request it is making - the caller (AdminBillingMode.tsx) holds it in
//  component state for the session and never persists it (no localStorage, no
//  cookie, never a `VITE_*` var - CLAUDE.md section 4). Once the real operator
//  auth boundary (#135) ships, this header goes away in favor of the real
//  session credential - a client-side swap, not a rewrite (mirrors story 06's
//  own "swappable IOperatorGate" plan).
//
//  Mirrors web/src/billing/billingClient.ts + web/src/account/signInClient.ts:
//  the API base URL comes from `import.meta.env.VITE_API_BASE_URL` (never
//  hardcoded), and every call FAILS GRACEFULLY - a network error, non-OK
//  status, or unparseable body resolves to a friendly typed result rather than
//  throwing, so the operator screen never shows a raw error.
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

/** Friendly message for a 401 (missing/wrong operator secret). */
const UNAUTHORIZED_MESSAGE = 'That operator secret was not accepted.';

const base = () => import.meta.env.VITE_API_BASE_URL;

/** The mode strings the server may return; anything else is treated as an error rather than trusted. */
const KNOWN_MODES: readonly StripeMode[] = ['test', 'live'];

function asStripeMode(value: unknown): StripeMode | null {
  return typeof value === 'string' ? KNOWN_MODES.find((known) => known === value) ?? null : null;
}

/** Narrows an unknown parsed body from the GET status endpoint. */
function asStatusBody(value: unknown): { activeMode: StripeMode; lastChangedUtc: string | null; enabled: boolean } | null {
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

/**
 * Reads the currently active Stripe mode (GET /api/admin/stripe-mode), sending
 * `secret` as the `X-Operator-Secret` header. Resolves 'unauthorized' on a 401
 * (never a guessed/blank mode - AC-01), 'error' on any other failure (network,
 * non-OK status, or an unparseable body), and 'ok' with the parsed status
 * otherwise. Never throws.
 */
export async function fetchStripeMode(secret: string): Promise<StripeModeStatusResult> {
  try {
    const response = await fetch(`${base()}/api/admin/stripe-mode`, {
      headers: { 'X-Operator-Secret': secret },
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
 * Switches the active Stripe mode (POST /api/admin/stripe-mode), sending
 * `secret` as the `X-Operator-Secret` header and `{ mode }` as the body. This
 * is the confirmed flip itself (AC-02) - the caller is responsible for showing
 * the confirmation step BEFORE calling this. Resolves 'unauthorized' on a 401,
 * 'error' on any other failure (including a 400 for an invalid mode), and 'ok'
 * with the new mode + timestamp otherwise. Never throws.
 */
export async function setStripeMode(secret: string, mode: StripeMode): Promise<StripeModeSwitchResult> {
  try {
    const response = await fetch(`${base()}/api/admin/stripe-mode`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Operator-Secret': secret },
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
