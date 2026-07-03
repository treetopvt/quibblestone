// ----------------------------------------------------------------------------
//  operatorClient.ts - the web client for the OPERATOR back-office login flow
//  (sysadmin-console/01, issue #135). A thin REST client, NOT the feature: the
//  real magic-link issue/verify, the SEPARATE operator allowlist, and the operator
//  session credential all live server-side in api/src/Admin/OperatorLoginController.
//  This module only POSTs the two login steps and shapes their responses.
//
//  Mirrors web/src/account/signInClient.ts by shape, but is a DELIBERATELY SEPARATE
//  file in the SEPARATE admin bundle (AC-04): it imports nothing from the kid app
//  (pages / signalr / gallery / engine / components), and the kid app imports
//  nothing from here. The API base URL comes from `import.meta.env.VITE_API_BASE_URL`
//  (never hardcoded, never a secret in a VITE_ var - the allowlist and any signing
//  key are NEVER VITE_*, they live server-side only). Every call FAILS GRACEFULLY -
//  a network error, non-OK status, or unparseable body resolves to a friendly
//  result rather than throwing, so the login screen never shows a raw error.
//
//  ALLOWLIST AT VERIFY (AC-02): the request step returns the SAME neutral message
//  whether or not the email is an operator (the server never consults the allowlist
//  at issue time). Only after following a real link does an ALLOWLISTED operator get
//  "signed-in"; a non-operator gets "not-authorized".
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** Result of requesting an operator sign-in link (the neutral acknowledgement step). */
export interface OperatorRequestResult {
  /** True when the request was accepted (the neutral "check your inbox" state). */
  ok: boolean;
  /** A friendly, neutral message to show - never reveals whether the email is an operator. */
  message: string;
  /**
   * DEV ONLY: the raw magic-link token, echoed by the API in the Development
   * environment so the flow is walkable locally with no email provider. Undefined
   * in any deployed environment (the link arrives by email there).
   */
  devToken?: string;
}

/** The distinct outcomes of following (verifying) an operator magic link. */
export type OperatorOutcome = 'signed-in' | 'not-authorized' | 'link-invalid' | 'error';

/** Result of verifying a followed operator magic link (completing login). */
export interface OperatorVerifyResult {
  /** Which of the server's outcomes occurred (or 'error' on a transport failure). */
  outcome: OperatorOutcome;
  /** A friendly message matching the outcome. */
  message: string;
  /** The signed-in operator email (present only on 'signed-in'), for the confirmation UI. */
  email?: string;
}

/** Friendly fallback shown when the login endpoint cannot be reached or parsed. */
const UNAVAILABLE_MESSAGE = 'We could not reach the operator console just now - please try again in a moment.';

/** The API base URL, from a NON-secret VITE_ var (the allowlist / signing key are never here). */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;

/** Narrows an unknown parsed body from the request endpoint. */
function asRequestResult(value: unknown): { message: string; devToken?: string } | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.message !== 'string') return null;
  const devToken =
    typeof record.devToken === 'string' && record.devToken.length > 0 ? record.devToken : undefined;
  return { message: record.message, devToken };
}

/** The outcome strings the server may return; anything else is treated as an error. */
const KNOWN_OUTCOMES: readonly OperatorOutcome[] = ['signed-in', 'not-authorized', 'link-invalid'];

/** Narrows an unknown parsed body from the verify endpoint. */
function asVerifyResult(value: unknown): { outcome: OperatorOutcome; message: string; email?: string } | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.outcome !== 'string' || typeof record.message !== 'string') return null;
  const outcome = KNOWN_OUTCOMES.find((known) => known === record.outcome);
  if (!outcome) return null;
  const email = typeof record.email === 'string' && record.email.length > 0 ? record.email : undefined;
  return { outcome, message: record.message, email };
}

/**
 * Requests an operator magic-link email for `email` (POST
 * /api/admin/login/request). Resolves a neutral acknowledgement on success and a
 * friendly fallback on any failure - never throws. The message is deliberately the
 * same whether or not the email is an operator (AC-02).
 */
export async function requestOperatorLink(email: string): Promise<OperatorRequestResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/admin/login/request`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email }),
    });

    if (!response.ok) {
      return { ok: false, message: UNAVAILABLE_MESSAGE };
    }

    const body: unknown = await response.json();
    const parsed = asRequestResult(body);
    if (!parsed) {
      return { ok: false, message: UNAVAILABLE_MESSAGE };
    }
    return { ok: true, message: parsed.message, devToken: parsed.devToken };
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE };
  }
}

/**
 * Completes operator login by verifying a followed magic-link token (POST
 * /api/admin/login/verify). Resolves one of the server outcomes, or
 * `{ outcome: 'error' }` on any transport failure - never throws. The short-lived
 * operator credential is set by the server (an HttpOnly cookie) and also returned
 * in the body; only allowlisted operators reach the 'signed-in' outcome (AC-02).
 */
export async function verifyOperatorLink(token: string): Promise<OperatorVerifyResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/admin/login/verify`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      // Include credentials so the server's HttpOnly operator cookie is stored for a
      // same-origin deployment; harmless cross-origin in dev (the body is the
      // primary path there).
      credentials: 'include',
      body: JSON.stringify({ token }),
    });

    if (!response.ok) {
      return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
    }

    const body: unknown = await response.json();
    const parsed = asVerifyResult(body);
    if (!parsed) {
      return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
    }
    return { outcome: parsed.outcome, message: parsed.message, email: parsed.email };
  } catch {
    return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
  }
}
