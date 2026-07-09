// ----------------------------------------------------------------------------
//  signInClient.ts - the web client for the account sign-in / sign-up flow
//  (accounts-identity/03, issue #69; accounts-identity/07, issue #211). A thin
//  REST client, NOT the feature: the real magic-link issue/verify, the account
//  lookup/create-or-get, and the credential all live server-side in
//  api/src/Controllers/AccountsController.cs. This module only POSTs the two
//  sign-in steps and shapes their responses.
//
//  Mirrors web/src/gallery/publishTale.ts and web/src/safety/checkWord.ts: the
//  API base URL comes from `import.meta.env.VITE_API_BASE_URL` (never hardcoded,
//  never a secret in a VITE_ var, CLAUDE.md section 4), and every call FAILS
//  GRACEFULLY - a network error, non-OK status, or unparseable body resolves to a
//  friendly result rather than throwing, so the Account surface never shows a raw
//  error (AC-06).
//
//  INTENT (accounts-identity/07): both calls take an optional `intent` -
//  `'signup'` selects the free family-account path (a create-or-get on verify
//  for an email with no existing account), while omitting it (or any other
//  value) keeps the legacy purchaser sign-in copy and behavior. The response
//  shape is UNCHANGED either way - the server picks copy/behavior server-side.
//
//  AUTH BOUNDARY (AC-03/AC-04): this is an account-surface client. It is
//  imported ONLY by the Account page (a Home-reachable, adult-facing surface) -
//  never by the join-code / lobby / word-entry / reveal flow, never by the
//  SignalR hook. Nothing about free play depends on it; a player who never
//  signs in plays the full free tier untouched.
//
//  NO ENUMERATION (AC-05): the request step returns the SAME neutral message
//  whether or not an account exists (the server does not branch on existence),
//  for either intent. The verify step is where a real account holder learns
//  they are signed in, a free sign-up is created, or a non-account sign-in is
//  guided to purchase.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** Result of requesting a sign-in link (the neutral acknowledgement step). */
export interface SignInRequestResult {
  /** True when the request was accepted (the neutral "check your inbox" state). */
  ok: boolean;
  /** A friendly, neutral message to show - never reveals whether an account exists. */
  message: string;
  /**
   * DEV ONLY: the raw magic-link token, echoed by the API in the Development
   * environment so the flow is walkable locally with no email provider. Undefined
   * in any deployed environment (the link arrives by email there). Lets the
   * Account page offer a "continue" affordance in local dev.
   */
  devToken?: string;
}

/** The distinct outcomes of following (verifying) a magic link. */
export type SignInOutcome = 'signed-in' | 'no-account' | 'link-invalid' | 'error';

/** Result of verifying a followed magic link (completing sign-in). */
export interface SignInVerifyResult {
  /** Which of the server's outcomes occurred (or 'error' on a transport failure). */
  outcome: SignInOutcome;
  /** A friendly message matching the outcome (guide-to-purchase, signed-in, etc.). */
  message: string;
  /** The signed-in purchaser email (present only on 'signed-in'), for the "signed in as X" UI. */
  email?: string;
  /**
   * The short-lived purchaser credential (present only on 'signed-in'). The SPA holds it
   * in memory and presents it as a bearer to the restore/manage endpoint
   * (billing-entitlements/05) - the cross-origin-friendly path, since the HttpOnly cookie
   * is same-site only. Not persisted; not displayed.
   */
  credential?: string;
}

/** Friendly fallback shown when the sign-in endpoint cannot be reached or parsed (AC-06). */
const UNAVAILABLE_MESSAGE = 'We could not reach sign-in just now - please try again in a moment.';

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
const KNOWN_OUTCOMES: readonly SignInOutcome[] = ['signed-in', 'no-account', 'link-invalid'];

/** Narrows an unknown parsed body from the verify endpoint. */
function asVerifyResult(
  value: unknown,
): { outcome: SignInOutcome; message: string; email?: string; credential?: string } | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.outcome !== 'string' || typeof record.message !== 'string') return null;
  const outcome = KNOWN_OUTCOMES.find((known) => known === record.outcome);
  if (!outcome) return null;
  const email = typeof record.email === 'string' && record.email.length > 0 ? record.email : undefined;
  const credential =
    typeof record.credential === 'string' && record.credential.length > 0 ? record.credential : undefined;
  return { outcome, message: record.message, email, credential };
}

/**
 * Requests a magic-link sign-in email for `email` (POST
 * /api/accounts/signin/request). Resolves a neutral acknowledgement on success
 * and a friendly fallback on any failure - never throws (AC-06). The message is
 * deliberately the same whether or not an account exists (AC-05). Pass
 * `intent: 'signup'` for the free family-account path (accounts-identity/07);
 * omit it (or pass 'signin') to keep the legacy purchaser copy.
 */
export async function requestSignInLink(
  email: string,
  intent?: 'signin' | 'signup',
): Promise<SignInRequestResult> {
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/accounts/signin/request`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, intent }),
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
 * Completes sign-in by verifying a followed magic-link token (POST
 * /api/accounts/signin/verify). Resolves one of the server outcomes, or
 * `{ outcome: 'error' }` on any transport failure - never throws (AC-06). The
 * short-lived credential is set by the server (an HttpOnly cookie) and also
 * returned in the body; this client surfaces it (plus the outcome + email) so
 * the restore view (billing-entitlements/05) can present it as a bearer. Pass
 * the SAME `intent` used for the request step - on `'signup'`, a valid token
 * for an email with no existing account creates a free family account
 * (accounts-identity/07) via the server's idempotent create-or-get before
 * returning 'signed-in'; an existing account signs in normally either way.
 */
export async function verifySignIn(
  token: string,
  intent?: 'signin' | 'signup',
): Promise<SignInVerifyResult> {
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/accounts/signin/verify`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      // Include credentials so the server's HttpOnly credential cookie is stored
      // for a same-origin deployment; harmless cross-origin in dev (the body is
      // the primary path there).
      credentials: 'include',
      body: JSON.stringify({ token, intent }),
    });

    if (!response.ok) {
      return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
    }

    const body: unknown = await response.json();
    const parsed = asVerifyResult(body);
    if (!parsed) {
      return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
    }
    return { outcome: parsed.outcome, message: parsed.message, email: parsed.email, credential: parsed.credential };
  } catch {
    return { outcome: 'error', message: UNAVAILABLE_MESSAGE };
  }
}
