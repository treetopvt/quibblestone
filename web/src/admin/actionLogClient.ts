// ----------------------------------------------------------------------------
//  actionLogClient.ts - the OPERATOR-CONSOLE web client for the Operations tab's
//  read-only action-log view (sysadmin-console/06, issue #233). Reads
//  `GET /api/admin/action-log`, which IS already built and Ops-scope-guarded -
//  unlike settingsClient's dependency, this endpoint exists today - but this
//  module keeps the SAME dependency-tolerant posture as every other admin client
//  in this bundle (mirrors settingsClient.ts exactly): a network throw, ANY
//  non-2xx status, or a body that does not parse as expected all collapse to
//  `{ outcome: 'unavailable', message }` - never a thrown error, never a guessed
//  value. The rows are already server-sorted newest-first and capped at 200; this
//  client does not re-sort or re-cap them.
//
//  SEPARATE ADMIN BUNDLE (from story 01): this file lives in the admin bundle and
//  imports NOTHING from the kid app (pages / signalr / gallery / engine /
//  components). The API base URL comes from `import.meta.env.VITE_API_BASE_URL`
//  (never hardcoded, never a secret). Authenticated the SAME way every other
//  admin call is - the in-memory operator credential presented as
//  `Authorization: Bearer` (the PRIMARY path cross-origin) with
//  `credentials: 'include'` carrying a same-site cookie along.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** One operator action-log row, as returned by GET /api/admin/action-log. */
export interface ActionLogRow {
  operatorEmail: string;
  action: string;
  target: string;
  note: string;
  timestampUtc: string;
}

/** The distinct outcomes a call against the action-log endpoint can resolve to. */
export type ActionLogOutcome = 'available' | 'unavailable';

/** Result of reading the action log (GET /api/admin/action-log). */
export interface ActionLogResult {
  outcome: ActionLogOutcome;
  /** Present only when outcome is 'available'. Already server-sorted newest-first. */
  rows?: ActionLogRow[];
  /** A friendly message for the 'unavailable' outcome. */
  message?: string;
}

/** Friendly message shown when the action-log endpoint is not reachable or returns something unexpected. */
const UNAVAILABLE_MESSAGE = 'The operator action log is not available right now.';

/** The API base URL, from a NON-secret VITE_ var (the allowlist / keys are never here). */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;

/**
 * Narrows one unparsed row to an {@link ActionLogRow}. Guards every field as a
 * string rather than trusting the server shape blindly - `note` may be an empty
 * string (per the contract) but must still be present as a string. Unknown extra
 * fields on the row are simply ignored rather than rejected.
 */
function asActionLogRow(value: unknown): ActionLogRow | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.operatorEmail !== 'string') return null;
  if (typeof record.action !== 'string') return null;
  if (typeof record.target !== 'string') return null;
  if (typeof record.note !== 'string') return null;
  if (typeof record.timestampUtc !== 'string') return null;
  return {
    operatorEmail: record.operatorEmail,
    action: record.action,
    target: record.target,
    note: record.note,
    timestampUtc: record.timestampUtc,
  };
}

/** Builds the operator-credential headers shared by the call (bearer, no body on a GET). */
function buildHeaders(credential: string | null): HeadersInit | undefined {
  const headers: Record<string, string> = {};
  if (credential) headers.Authorization = `Bearer ${credential}`;
  return Object.keys(headers).length > 0 ? headers : undefined;
}

/**
 * Reads the operator action log (GET /api/admin/action-log), presenting the
 * operator credential as a bearer with the cookie riding along. THE DEPENDENCY-
 * TOLERANCE RULE: a network throw, ANY non-2xx status (401, 404, 500, ...), or a
 * 2xx body that does not parse as `{ rows: [...] }` all collapse to
 * `{ outcome: 'unavailable' }` - only a 2xx whose body carries a `rows` array
 * yields `{ outcome: 'available', rows }`. Never throws.
 */
export async function fetchActionLog(credential: string | null): Promise<ActionLogResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/admin/action-log`, {
      headers: buildHeaders(credential),
      credentials: 'include',
    });
    if (!response.ok) {
      return { outcome: 'unavailable', message: UNAVAILABLE_MESSAGE };
    }
    const body: unknown = await response.json().catch(() => null);
    if (typeof body !== 'object' || body === null || !Array.isArray((body as Record<string, unknown>).rows)) {
      return { outcome: 'unavailable', message: UNAVAILABLE_MESSAGE };
    }
    const rawRows = (body as Record<string, unknown>).rows as unknown[];
    const rows = rawRows.map(asActionLogRow).filter((row): row is ActionLogRow => row !== null);
    return { outcome: 'available', rows };
  } catch {
    return { outcome: 'unavailable', message: UNAVAILABLE_MESSAGE };
  }
}
