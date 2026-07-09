// ----------------------------------------------------------------------------
//  settingsClient.ts - the OPERATOR-CONSOLE web client for the Operations tab's
//  read-only runtime-settings view (sysadmin-console/05, AC-04). This is
//  DEPENDENCY-TOLERANT of `control-plane/01`'s settings endpoint (ADR 0003 Layer
//  1), which has NOT been built yet as of this story: any network failure,
//  non-2xx status, or a body that does not parse as a JSON array collapses to
//  `{ outcome: 'unavailable' }` - never a thrown error, never a guessed value.
//  This module only GETs the settings list; there is no editor here (that stays
//  future scope - AC-07 of this story explicitly parks any settings-editing UI).
//
//  SEPARATE ADMIN BUNDLE (from story 01): this file lives in the admin bundle and
//  imports NOTHING from the kid app (pages / signalr / gallery / engine /
//  components). The API base URL comes from `import.meta.env.VITE_API_BASE_URL`
//  (never hardcoded, never a secret). Authenticated the SAME way every other
//  admin call is - the in-memory operator credential presented as
//  `Authorization: Bearer` (the PRIMARY path cross-origin) with
//  `credentials: 'include'` carrying a same-site cookie along. Mirrors
//  stripeModeClient.ts exactly.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** One runtime setting as shown to the operator (read-only view, no editor yet). */
export interface AdminSettingView {
  key: string;
  description?: string;
  effectiveValue: unknown;
}

/** The distinct outcomes a call against the settings endpoint can resolve to. */
export type SettingsOutcome = 'available' | 'unavailable';

/** Result of reading the runtime settings list (GET /api/admin/settings). */
export interface AdminSettingsResult {
  outcome: SettingsOutcome;
  /** Present only when outcome is 'available'. */
  settings?: AdminSettingView[];
  /** A friendly message for the 'unavailable' outcome. */
  message?: string;
}

/** Friendly message shown when the settings endpoint is not reachable, missing, or not yet built. */
const UNAVAILABLE_MESSAGE = 'Runtime settings are not wired up yet.';

/** The API base URL, from a NON-secret VITE_ var (the allowlist / keys are never here). */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;

/**
 * Narrows one unparsed array entry to an {@link AdminSettingView}. Accepts ANY
 * object carrying a string `key` and a present `effectiveValue` - the server may
 * also send `type`, `codeDefault`, `override`, `bounds`, `requiresConfirmation`,
 * but this read-only view only needs key / description / effectiveValue, so
 * unrecognized extra fields are simply ignored rather than rejected (a future
 * server field must not break this defensive parse).
 */
function asSettingView(value: unknown): AdminSettingView | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.key !== 'string' || record.key.length === 0) return null;
  if (!('effectiveValue' in record) || record.effectiveValue === undefined) return null;
  const description = typeof record.description === 'string' ? record.description : undefined;
  return { key: record.key, description, effectiveValue: record.effectiveValue };
}

/** Builds the operator-credential headers shared by the call (bearer, no body on a GET). */
function buildHeaders(credential: string | null): HeadersInit | undefined {
  const headers: Record<string, string> = {};
  if (credential) headers.Authorization = `Bearer ${credential}`;
  return Object.keys(headers).length > 0 ? headers : undefined;
}

/**
 * Reads the runtime settings list (GET /api/admin/settings), presenting the
 * operator credential as a bearer with the cookie riding along. THE DEPENDENCY-
 * TOLERANCE RULE (AC-04): a network throw, ANY non-2xx status (401, 404, 500,
 * ...), or a 2xx body that does not parse as a JSON array all collapse to
 * `{ outcome: 'unavailable' }` - only a 2xx whose body is a JSON array yields
 * `{ outcome: 'available', settings }`. Never throws.
 */
export async function fetchAdminSettings(credential: string | null): Promise<AdminSettingsResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/admin/settings`, {
      headers: buildHeaders(credential),
      credentials: 'include',
    });
    if (!response.ok) {
      return { outcome: 'unavailable', message: UNAVAILABLE_MESSAGE };
    }
    const body: unknown = await response.json().catch(() => null);
    if (!Array.isArray(body)) {
      return { outcome: 'unavailable', message: UNAVAILABLE_MESSAGE };
    }
    const settings = body
      .map(asSettingView)
      .filter((setting): setting is AdminSettingView => setting !== null);
    return { outcome: 'available', settings };
  } catch {
    return { outcome: 'unavailable', message: UNAVAILABLE_MESSAGE };
  }
}
