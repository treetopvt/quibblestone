// ----------------------------------------------------------------------------
//  errorBeacon.ts - the web client's anonymous unhandled-error beacon
//  (platform-devops/04, AC-06).
//
//  BUNDLE-SIZE TRADEOFF (CLAUDE.md section 10): we DELIBERATELY do NOT ship the
//  Application Insights JS SDK - it is heavy for a PWA. Instead this is a tiny,
//  hand-rolled beacon (near-zero bytes, no dependency) that installs window
//  'error' + 'unhandledrejection' handlers and fire-and-forgets a minimal payload
//  to the API, which forwards it to App Insights server-side (see
//  api/src/Controllers/ClientErrorController.cs). That keeps the App Insights
//  connection string SERVER-SIDE (never in a VITE_ var, never in the browser -
//  AC-05) and routes the client error through the SAME PII scrubber as all other
//  telemetry (AC-04).
//
//  NO PII (AC-04, README section 6): the payload carries ONLY a message, a stack,
//  and a NORMALIZED route path - NEVER the query string (which could carry a
//  nickname / code), NEVER a nickname, NEVER a room code, NEVER any identity. The
//  path is reduced to its TOP-LEVEL route segment (normalizeRoutePath below)
//  BEFORE it leaves the browser, because a deep-link route like "/join/:code"
//  carries the join CODE in its pathname (web/src/App.tsx) - shipping the raw
//  pathname would leak it. So "/join/ABCD" becomes "/join" and no dynamic tail can
//  ever ride the beacon. The pure payload-building function below is the guarantee:
//  it reads ONLY the error's own message/stack plus that normalized route, so there
//  is no field through which PII could travel.
//
//  FIRE-AND-FORGET / NEVER BLOCKS (like serveLog.ts): sendBeacon (or a fetch
//  keepalive fallback) is best-effort - a failure is swallowed, there is no retry,
//  and nothing here can wedge the app. Config: the API base URL comes from
//  import.meta.env.VITE_API_BASE_URL (the existing pattern in
//  signalr/useGameHub.ts and telemetry/serveLog.ts) - never hardcoded.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** The anonymous client-error payload. These three fields are the WHOLE shape -
 *  there is intentionally no field that could carry PII (AC-04). Mirrors the
 *  server's ClientErrorRequest (api/src/Controllers/ClientErrorController.cs). */
export interface ClientErrorPayload {
  /** The error message (e.g. "TypeError: x is undefined"). */
  message: string;
  /** The error stack when available, else null. */
  stack: string | null;
  /** The TOP-LEVEL route segment ONLY (e.g. "/join") - never a dynamic tail (the
   *  join code in "/join/:code"), never a query string. See normalizeRoutePath. */
  path: string;
}

/** The API path the beacon posts to (joined onto VITE_API_BASE_URL). */
const CLIENT_ERRORS_ENDPOINT = '/api/client-errors';

/**
 * Reduces a pathname to its TOP-LEVEL route segment so no dynamic, per-session
 * identifier can ride the beacon (AC-04, non-negotiable). The SPA has a deep-link
 * route "/join/:code" (web/src/App.tsx) whose pathname is "/join/ABCD" where ABCD
 * is the JOIN CODE - a room identifier that telemetry must never carry. Keeping
 * ONLY the first segment collapses that (and any future dynamic route) to a stable
 * route template: "/join/ABCD" -> "/join", "/solo" -> "/solo", "/" or "" -> "/".
 * PURE (no window access) so the guarantee is unit-testable.
 */
export function normalizeRoutePath(pathname: string): string {
  if (typeof pathname !== 'string' || pathname.length === 0) {
    return '/';
  }
  const firstSegment = pathname.split('/').find((segment) => segment.length > 0);
  return firstSegment === undefined ? '/' : `/${firstSegment}`;
}

/**
 * Builds the anonymous payload from an unknown thrown value + a pathname. PURE
 * and testable (this is the AC-04 guarantee): it reads ONLY the error's message
 * and stack and the caller-supplied pathname - never window.location.search,
 * never any nickname / code / identity - and it NORMALIZES the pathname to its
 * top-level route segment (normalizeRoutePath) so a deep-link join code can never
 * leak. An unknown (non-Error) reason is narrowed defensively to a string message
 * with no stack.
 */
export function buildClientErrorPayload(reason: unknown, pathname: string): ClientErrorPayload {
  const path = normalizeRoutePath(pathname);

  if (reason instanceof Error) {
    return {
      message: reason.message.length > 0 ? reason.message : reason.name,
      stack: typeof reason.stack === 'string' ? reason.stack : null,
      path,
    };
  }

  // A non-Error rejection/throw (a string, an object, undefined): stringify
  // defensively without pulling in anything identity-bearing.
  return {
    message: typeof reason === 'string' && reason.length > 0 ? reason : 'Unknown client error',
    stack: null,
    path,
  };
}

/**
 * Fire-and-forget: sends the payload to the API via navigator.sendBeacon, with a
 * fetch keepalive fallback. Never awaited, never retried, every failure swallowed
 * - a beacon must never surface to a player or wedge the app.
 */
function sendClientError(payload: ClientErrorPayload): void {
  const url = `${import.meta.env.VITE_API_BASE_URL}${CLIENT_ERRORS_ENDPOINT}`;
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

/** Guard so the handlers are installed at most once (main.tsx calls it once, but
 *  StrictMode / HMR could re-run module code in dev). */
let installed = false;

/**
 * Installs the window 'error' + 'unhandledrejection' handlers ONCE, so any
 * unhandled client-side error fire-and-forgets an anonymous beacon (AC-06). Safe
 * to call more than once (idempotent) and a no-op outside a browser (no window).
 * Reads location.pathname at report time so the beacon never carries the query
 * string (AC-04).
 */
export function installErrorBeacon(): void {
  if (installed || typeof window === 'undefined') {
    return;
  }
  installed = true;

  window.addEventListener('error', (event: ErrorEvent) => {
    const reason = event.error ?? event.message;
    sendClientError(buildClientErrorPayload(reason, window.location.pathname));
  });

  window.addEventListener('unhandledrejection', (event: PromiseRejectionEvent) => {
    sendClientError(buildClientErrorPayload(event.reason, window.location.pathname));
  });
}
