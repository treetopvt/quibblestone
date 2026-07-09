// ----------------------------------------------------------------------------
//  seatPresetsClient.ts - the web client for the kid-seat-preset REST surface
//  (accounts-identity/08, issue #228). A thin REST client over the account-plane
//  preset endpoints on AccountsController: it presents the family credential (from
//  sign-in) as a bearer and shapes presets for the Account-page manager + the
//  join-flow picker.
//
//  Mirrors entitlementsClient.ts: API base from `import.meta.env.VITE_API_BASE_URL`
//  (never hardcoded), and every call fails GRACEFULLY (never throws) so a transport
//  hiccup can never break a screen. A 401 means "not signed in" (the picker simply
//  shows nothing; the manager prompts to sign in).
//
//  THE BOUNDARY (AC-03): this client talks ONLY to the account-plane preset
//  endpoints. It has nothing to do with joining a room - selecting a preset in the
//  UI fills the SAME display-name / variant fields and submits through the SAME hub
//  invoke. A preset is a { id, nickname, variant } tuple and NOTHING else (AC-05):
//  no history, gallery, entitlement, or PII field is ever read or sent here.
//
//  SAFETY (AC-04): the server is authoritative. This client never pre-approves a
//  nickname - it just sends the candidate; the server vets it through the same
//  content-safety filter as any display name and returns a friendly message on a
//  rejection, surfaced to the manager UI.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { toGuardianVariant, type GuardianVariant } from '../components';

/** One kid seat preset (accounts-identity/08). A pure tuple - no history / gallery / PII (AC-05). */
export interface SeatPreset {
  /** The stable preset id (used to edit / delete). */
  id: string;
  /** The preset's display name - doubles as its label in the manager. */
  nickname: string;
  /** The Guardian variant, narrowed to a known value. */
  variant: GuardianVariant;
}

/** Result of loading a family's presets. */
export interface SeatPresetsResult {
  /** 'ok' with the list, 'signed-out' (401 - no family credential), or 'error' (transport). */
  status: 'ok' | 'signed-out' | 'error';
  presets: SeatPreset[];
}

/** Result of a create / update write: the saved preset, or a friendly rejection message. */
export type SeatPresetWriteResult =
  | { status: 'ok'; preset: SeatPreset }
  | { status: 'invalid'; message: string }
  | { status: 'signed-out' }
  | { status: 'error' };

/** Result of a delete: whether it succeeded (an idempotent no-op still reads as done). */
export type SeatPresetDeleteResult = 'ok' | 'signed-out' | 'error';

/** Narrows one unknown preset item off the wire into a typed SeatPreset (or null). */
function asPreset(value: unknown): SeatPreset | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (typeof r.id !== 'string' || typeof r.nickname !== 'string' || typeof r.variant !== 'string') {
    return null;
  }
  // Narrow the variant to a known Guardian value at the boundary (the server also
  // normalizes it, so this is defense-in-depth for the render layer).
  return { id: r.id, nickname: r.nickname, variant: toGuardianVariant(r.variant) };
}

/** The preset endpoints, resolved off the configured API base (never hardcoded). */
function presetsUrl(path = ''): string {
  return `${import.meta.env.VITE_API_BASE_URL}/api/accounts/presets${path}`;
}

/** Bearer + cookie auth headers, matching entitlementsClient (cross-origin dev + same-site prod). */
function authHeaders(credential: string, json = false): HeadersInit {
  const headers: Record<string, string> = { Authorization: `Bearer ${credential}` };
  if (json) headers['Content-Type'] = 'application/json';
  return headers;
}

/**
 * Loads the signed-in family's saved presets, presenting `credential` as a bearer.
 * Resolves 'signed-out' on a 401, 'error' on any transport/parse failure, 'ok' with
 * the (possibly empty) list otherwise. Never throws.
 */
export async function fetchPresets(credential: string): Promise<SeatPresetsResult> {
  try {
    const response = await fetch(presetsUrl(), {
      headers: authHeaders(credential),
      credentials: 'include',
    });
    if (response.status === 401) return { status: 'signed-out', presets: [] };
    if (!response.ok) return { status: 'error', presets: [] };

    const body: unknown = await response.json();
    if (typeof body !== 'object' || body === null) return { status: 'error', presets: [] };
    const list = (body as Record<string, unknown>).presets;
    if (!Array.isArray(list)) return { status: 'error', presets: [] };

    const presets = list.map(asPreset).filter((p): p is SeatPreset => p !== null);
    return { status: 'ok', presets };
  } catch {
    return { status: 'error', presets: [] };
  }
}

/** Shared body-carrying write (POST create / PUT update) with the common result mapping. */
async function writePreset(
  method: 'POST' | 'PUT',
  url: string,
  credential: string,
  nickname: string,
  variant: string,
): Promise<SeatPresetWriteResult> {
  try {
    const response = await fetch(url, {
      method,
      headers: authHeaders(credential, true),
      credentials: 'include',
      body: JSON.stringify({ nickname, variant }),
    });
    if (response.status === 401) return { status: 'signed-out' };
    if (response.status === 400) {
      // The server rejected the nickname (length / safety) - surface its friendly
      // message (AC-04). Fall back to a neutral note if the body is unshaped.
      const body: unknown = await response.json().catch(() => null);
      const message =
        typeof body === 'object' && body !== null && typeof (body as Record<string, unknown>).message === 'string'
          ? ((body as Record<string, unknown>).message as string)
          : 'That name cannot be used - please try another.';
      return { status: 'invalid', message };
    }
    if (!response.ok) return { status: 'error' };

    const preset = asPreset(await response.json());
    return preset ? { status: 'ok', preset } : { status: 'error' };
  } catch {
    return { status: 'error' };
  }
}

/** Creates a new preset under the signed-in family account. Never throws. */
export function createPreset(credential: string, nickname: string, variant: string): Promise<SeatPresetWriteResult> {
  return writePreset('POST', presetsUrl(), credential, nickname, variant);
}

/** Updates an existing preset (by id) under the signed-in family account. Never throws. */
export function updatePreset(
  credential: string,
  id: string,
  nickname: string,
  variant: string,
): Promise<SeatPresetWriteResult> {
  return writePreset('PUT', presetsUrl(`/${encodeURIComponent(id)}`), credential, nickname, variant);
}

/** Deletes a preset (by id) under the signed-in family account. Idempotent; never throws. */
export async function deletePreset(credential: string, id: string): Promise<SeatPresetDeleteResult> {
  try {
    const response = await fetch(presetsUrl(`/${encodeURIComponent(id)}`), {
      method: 'DELETE',
      headers: authHeaders(credential),
      credentials: 'include',
    });
    if (response.status === 401) return 'signed-out';
    // 204 (deleted) and 404 (already gone / cross-account) are both a settled state
    // from the UI's point of view - the preset is not there anymore either way.
    if (response.ok || response.status === 404) return 'ok';
    return 'error';
  } catch {
    return 'error';
  }
}
