// ----------------------------------------------------------------------------
//  purchasersClient.ts - the web client for the OPERATOR grant / revoke of a
//  purchaser entitlement by email (sysadmin-console/02, issue #136). A thin REST
//  client, NOT the feature: the account lookup, the grant store, and the operator
//  authorization all live server-side (api/src/Admin/AdminEntitlementsController
//  behind story 01's "Operator" policy). This module only GETs the purchaser lookup
//  and POSTs / DELETEs the grant + revoke actions, and shapes their responses.
//
//  SEPARATE ADMIN BUNDLE (AC-05, from story 01): this file lives in the admin bundle
//  and imports NOTHING from the kid app (pages / signalr / gallery / engine /
//  components), and the kid app imports nothing from here. The API base URL comes
//  from `import.meta.env.VITE_API_BASE_URL` (never hardcoded, never a secret). Every
//  call sends credentials so the HttpOnly operator cookie rides along, and FAILS
//  GRACEFULLY - a network error, non-OK status, or unparseable body resolves to a
//  friendly result rather than throwing.
//
//  ANONYMITY FIREWALL (AC-04, non-negotiable): this client speaks SOLELY in purchaser
//  email + capability keys / leases. It neither requests nor exposes any player
//  nickname, room code, or session - there is no such field on the wire and no way to
//  navigate from a purchaser to gameplay data.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** How a grant was obtained (matches the server GrantSource enum, serialized as a string). */
export type GrantSource = 'Subscription' | 'OneTime' | 'Operator';

/** One capability lease held by a purchaser (matches the server PurchaserGrantDto). */
export interface PurchaserGrant {
  /** The catalog capability key (e.g. "library.full", "pack.spooky"). */
  capabilityKey: string;
  /** A friendly display name (server-provided). */
  label: string;
  /** The lease end as an ISO string, or null for "no expiry" (a one-time pack). */
  validThrough: string | null;
  /** How the grant was obtained (subscription / one-time / operator). */
  source: GrantSource;
  /** Whether the lease is active right now (server-computed). */
  active: boolean;
}

/** The purchaser lookup view: whether an account exists, the email, and its grants. */
export interface PurchaserLookup {
  /** True when an account exists for this email; false = the clear not-found state. */
  accountExists: boolean;
  /** The canonical email the lookup resolved. */
  email: string;
  /** The purchaser's current capability leases (empty when no account or none held). */
  grants: PurchaserGrant[];
}

/** The result of a lookup / grant / revoke call. */
export interface PurchaserResult {
  /** True when the call reached the server and returned OK. */
  ok: boolean;
  /** The purchaser view (null only on a transport / auth failure). */
  purchaser: PurchaserLookup | null;
  /** A friendly message to show (a server message on success, a fallback on failure). */
  message: string;
}

/** Friendly fallback shown when the endpoints cannot be reached or parsed. */
const UNAVAILABLE_MESSAGE =
  'We could not reach the purchaser console just now - please try again in a moment.';

/** The API base URL, from a NON-secret VITE_ var (the allowlist / keys are never here). */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;

/** The grant sources the server may return; anything else is treated as an operator grant for display. */
const KNOWN_SOURCES: readonly GrantSource[] = ['Subscription', 'OneTime', 'Operator'];

/** Narrows one unknown grant into a PurchaserGrant, or null if malformed. */
function asGrant(value: unknown): PurchaserGrant | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.capabilityKey !== 'string' || typeof record.label !== 'string') return null;
  if (typeof record.active !== 'boolean') return null;
  const validThrough = typeof record.validThrough === 'string' ? record.validThrough : null;
  const source = KNOWN_SOURCES.find((known) => known === record.source) ?? 'Operator';
  return {
    capabilityKey: record.capabilityKey,
    label: record.label,
    validThrough,
    source,
    active: record.active,
  };
}

/** Narrows an unknown purchaser-lookup body, or null if malformed. */
function asLookup(value: unknown): PurchaserLookup | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.accountExists !== 'boolean' || typeof record.email !== 'string') return null;
  const rawGrants = Array.isArray(record.grants) ? record.grants : [];
  const grants: PurchaserGrant[] = [];
  for (const raw of rawGrants) {
    const grant = asGrant(raw);
    if (grant) grants.push(grant);
  }
  return { accountExists: record.accountExists, email: record.email, grants };
}

/** Narrows an unknown action body ({ purchaser, message }), or null if malformed. */
function asActionResult(value: unknown): { purchaser: PurchaserLookup; message: string } | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  const purchaser = asLookup(record.purchaser);
  if (!purchaser) return null;
  const message = typeof record.message === 'string' ? record.message : '';
  return { purchaser, message };
}

/** Builds the base URL for one purchaser's entitlement endpoints (email path-encoded). */
function purchaserBase(email: string): string {
  return `${API_BASE_URL}/api/admin/purchasers/${encodeURIComponent(email)}`;
}

/**
 * Looks a purchaser up by email (GET /api/admin/purchasers/{email}). Resolves the
 * account + its grants (or the clear not-found state) on success, or a friendly
 * failure on any transport / auth error - never throws. Sends credentials so the
 * operator cookie is included.
 */
export async function lookupPurchaser(email: string): Promise<PurchaserResult> {
  try {
    const response = await fetch(purchaserBase(email), { method: 'GET', credentials: 'include' });
    if (!response.ok) {
      return { ok: false, purchaser: null, message: UNAVAILABLE_MESSAGE };
    }
    const body: unknown = await response.json();
    const purchaser = asLookup(body);
    if (!purchaser) {
      return { ok: false, purchaser: null, message: UNAVAILABLE_MESSAGE };
    }
    return { ok: true, purchaser, message: '' };
  } catch {
    return { ok: false, purchaser: null, message: UNAVAILABLE_MESSAGE };
  }
}

/**
 * Grants a capability to a purchaser (POST /api/admin/purchasers/{email}/entitlements).
 * `validThrough` is an ISO date string, or null for "no expiry" (a one-time-pack-shaped
 * grant). Resolves the refreshed purchaser view on success, or a friendly failure -
 * never throws.
 */
export async function grantEntitlement(
  email: string,
  capabilityKey: string,
  validThrough: string | null,
): Promise<PurchaserResult> {
  return postAction(`${purchaserBase(email)}/entitlements`, 'POST', { capabilityKey, validThrough });
}

/**
 * Revokes a capability from a purchaser (DELETE
 * /api/admin/purchasers/{email}/entitlements/{key}). The next session-creation check
 * reads the capability as locked. Resolves the refreshed purchaser view on success, or
 * a friendly failure - never throws.
 */
export async function revokeEntitlement(email: string, capabilityKey: string): Promise<PurchaserResult> {
  return postAction(
    `${purchaserBase(email)}/entitlements/${encodeURIComponent(capabilityKey)}`,
    'DELETE',
    undefined,
  );
}

/** Shared write for the grant (POST) + revoke (DELETE) actions, both returning the refreshed view. */
async function postAction(
  url: string,
  method: 'POST' | 'DELETE',
  body: { capabilityKey: string; validThrough: string | null } | undefined,
): Promise<PurchaserResult> {
  try {
    const response = await fetch(url, {
      method,
      credentials: 'include',
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!response.ok) {
      return { ok: false, purchaser: null, message: UNAVAILABLE_MESSAGE };
    }
    const parsed = asActionResult(await response.json());
    if (!parsed) {
      return { ok: false, purchaser: null, message: UNAVAILABLE_MESSAGE };
    }
    return { ok: true, purchaser: parsed.purchaser, message: parsed.message };
  } catch {
    return { ok: false, purchaser: null, message: UNAVAILABLE_MESSAGE };
  }
}

/**
 * The fixed, grantable capability catalog offered in the operator's grant control. These
 * are the SAME keys billing-entitlements/01's EntitlementCatalog defines (never a rival
 * catalog) - the server re-validates every key, so this list only shapes the picker. The
 * open-ended pack family (pack.<id>) is entered as free text alongside these fixed keys.
 */
export const GRANTABLE_CAPABILITIES: readonly { key: string; label: string }[] = [
  { key: 'library.full', label: 'Full Library' },
  { key: 'play.remote', label: 'Remote Play' },
  { key: 'play.largeGroup', label: 'Large Groups' },
  { key: 'ai.onDemand', label: 'AI Word Bank' },
];

/** The prefix for the open-ended add-on pack family (matches EntitlementCatalog.PackPrefix). */
export const PACK_PREFIX = 'pack.';
