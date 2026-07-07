// ----------------------------------------------------------------------------
//  cloudGalleryClient.ts - the web client for the PURCHASER cloud keepsake
//  gallery (keepsake-gallery/05, issue #154). A thin REST client, NOT the
//  feature: the real per-purchaser storage, the entitlement read
//  (`gallery.cloudSync`, AC-04), and the server-side content re-vet all live in
//  api/ under /api/account/gallery. This module only shapes the four calls the
//  signed-in Account surface makes and their responses.
//
//  Mirrors ../account/entitlementsClient.ts and ../account/signInClient.ts:
//    - API base from `import.meta.env.VITE_API_BASE_URL` (never hardcoded, never
//      a secret in a VITE_ var, CLAUDE.md section 4).
//    - Presents the short-lived purchaser credential as `Authorization: Bearer`
//      (the SPA holds it in memory in Account's signed-in state), with
//      `credentials: 'include'` so a same-site cookie rides along too.
//    - Every call FAILS GRACEFULLY - a network error, unparseable body, or
//      non-OK status resolves to a friendly result rather than throwing, so the
//      Account surface never shows a raw error. A 401 resolves to 'signed-out'
//      (the credential expired - prompt to sign in again), never to leaked data.
//
//  AUTH BOUNDARY (AC-01/AC-02): this is a PURCHASER-only client, imported ONLY by
//  the Account page (a Home-reachable, adult-facing surface) where the in-memory
//  credential is in scope - NEVER by the join-code / lobby / word-entry / reveal
//  flow, and never by the SignalR hook. Anonymous players never reach any code
//  path here; the device-local gallery (keepsake-gallery/03) remains everyone's
//  free default.
//
//  CHILD SAFETY (AC-05): a synced tale carries ONLY already-filtered display
//  content (the flattened title/parts/byline the reveal already showed) - no
//  purchaser email or PII is ever attached. The server re-vets on save (a
//  rejected save returns 400, surfaced here as 'error').
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/**
 * One flattened display part of a tale, as the API stores/returns it: a literal
 * text run or a coral-highlighted filled word. Mirrors the local gallery's
 * `TalePart` (web/src/gallery/localGallery.ts) and the API's camelCase
 * `{ isWord, text }` shape.
 */
export interface CloudTalePart {
  isWord: boolean;
  text: string;
}

/** One cloud-stored tale as the API returns it (GET /api/account/gallery). */
export interface CloudTale {
  taleId: string;
  title: string;
  parts: CloudTalePart[];
  bylineNames: string;
  /** ISO-8601 UTC creation timestamp, minted server-side. */
  createdUtc: string;
}

/** The status of any cloud-gallery call: ok, the credential expired (sign in again), or a transport/parse/re-vet failure. */
export type CloudGalleryStatus = 'ok' | 'signed-out' | 'error';

/** Result of listing the purchaser's cloud tales. */
export interface CloudGalleryListResult {
  status: CloudGalleryStatus;
  tales: CloudTale[];
}

/** Result of a save (upload) of one tale to the cloud gallery. */
export interface CloudSaveResult {
  status: CloudGalleryStatus;
  /** The server-minted tale id (present only on 'ok') - stamped back onto the local tale to dedupe. */
  taleId?: string;
}

/** Result of a delete-one or revoke-all mutation (no body to return). */
export interface CloudMutationResult {
  status: CloudGalleryStatus;
}

/** The payload uploaded when syncing a local tale to the cloud (POST body). AC-05: display content only, no PII. */
export interface CloudSavePayload {
  title: string;
  parts: CloudTalePart[];
  bylineNames: string;
}

const GALLERY_URL = (): string => `${import.meta.env.VITE_API_BASE_URL}/api/account/gallery`;

/** Narrows one unknown part item; returns null (dropped) if it is not the expected shape. */
function asCloudTalePart(value: unknown): CloudTalePart | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (typeof r.isWord !== 'boolean' || typeof r.text !== 'string') return null;
  return { isWord: r.isWord, text: r.text };
}

/** Narrows one unknown tale item from the list body; returns null if malformed (dropped rather than trusted). */
function asCloudTale(value: unknown): CloudTale | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (typeof r.taleId !== 'string' || typeof r.title !== 'string') return null;
  if (typeof r.bylineNames !== 'string' || typeof r.createdUtc !== 'string') return null;
  if (!Array.isArray(r.parts)) return null;
  const parts = r.parts.map(asCloudTalePart).filter((p): p is CloudTalePart => p !== null);
  return { taleId: r.taleId, title: r.title, parts, bylineNames: r.bylineNames, createdUtc: r.createdUtc };
}

/**
 * Lists the signed-in purchaser's cloud tales (GET /api/account/gallery),
 * presenting `credential` as a bearer. Resolves 'signed-out' on a 401 (AC-04:
 * the effective gate is a valid purchaser credential), 'error' on any other
 * failure, 'ok' with the tales otherwise. Never throws. Ordering/search/sort
 * are done client-side over this bounded per-purchaser set (AC-03).
 */
export async function fetchCloudGallery(credential: string): Promise<CloudGalleryListResult> {
  try {
    const response = await fetch(GALLERY_URL(), {
      headers: { Authorization: `Bearer ${credential}` },
      credentials: 'include',
    });

    if (response.status === 401) return { status: 'signed-out', tales: [] };
    if (!response.ok) return { status: 'error', tales: [] };

    const body: unknown = await response.json();
    if (typeof body !== 'object' || body === null) return { status: 'error', tales: [] };
    const list = (body as Record<string, unknown>).tales;
    if (!Array.isArray(list)) return { status: 'error', tales: [] };

    const tales = list.map(asCloudTale).filter((t): t is CloudTale => t !== null);
    return { status: 'ok', tales };
  } catch {
    return { status: 'error', tales: [] };
  }
}

/**
 * Uploads one local tale to the purchaser cloud gallery (POST
 * /api/account/gallery). The server re-vets the content (AC-05); a rejected
 * save (400) resolves to 'error' here so the UI can say "that one could not be
 * synced" without crashing the batch. Resolves 'signed-out' on a 401, 'ok' with
 * the minted `taleId` on success. Never throws.
 */
export async function saveCloudTale(credential: string, payload: CloudSavePayload): Promise<CloudSaveResult> {
  try {
    const response = await fetch(GALLERY_URL(), {
      method: 'POST',
      headers: { Authorization: `Bearer ${credential}`, 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(payload),
    });

    if (response.status === 401) return { status: 'signed-out' };
    if (!response.ok) return { status: 'error' };

    const body: unknown = await response.json();
    if (typeof body !== 'object' || body === null) return { status: 'error' };
    const taleId = (body as Record<string, unknown>).taleId;
    if (typeof taleId !== 'string' || taleId.length === 0) return { status: 'error' };
    return { status: 'ok', taleId };
  } catch {
    return { status: 'error' };
  }
}

/**
 * Deletes one cloud tale by id (DELETE /api/account/gallery/{taleId}). Resolves
 * 'ok' on a 2xx (the API returns 204), 'signed-out' on a 401, 'error'
 * otherwise. Never throws.
 */
export async function deleteCloudTale(credential: string, taleId: string): Promise<CloudMutationResult> {
  try {
    const response = await fetch(`${GALLERY_URL()}/${encodeURIComponent(taleId)}`, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${credential}` },
      credentials: 'include',
    });

    if (response.status === 401) return { status: 'signed-out' };
    if (!response.ok) return { status: 'error' };
    return { status: 'ok' };
  } catch {
    return { status: 'error' };
  }
}

/**
 * Revokes cloud sync - deletes ALL of the purchaser's cloud tales (DELETE
 * /api/account/gallery, AC-06). Resolves 'ok' on a 2xx, 'signed-out' on a 401,
 * 'error' otherwise. Never throws. Per the API contract a genuine server
 * failure can return non-2xx even after partially removing rows: the caller
 * treats a non-'ok' (non-'signed-out') result as "try again" and re-issues /
 * re-lists until the gallery reports empty.
 */
export async function revokeCloudGallery(credential: string): Promise<CloudMutationResult> {
  try {
    const response = await fetch(GALLERY_URL(), {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${credential}` },
      credentials: 'include',
    });

    if (response.status === 401) return { status: 'signed-out' };
    if (!response.ok) return { status: 'error' };
    return { status: 'ok' };
  } catch {
    return { status: 'error' };
  }
}
