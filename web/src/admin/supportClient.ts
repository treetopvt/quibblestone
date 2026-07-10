// ----------------------------------------------------------------------------
//  supportClient.ts - the web client for the Support job's account lookup + the four
//  content/account-plane verbs the AccountSupportController exposes (sysadmin-console/07,
//  issue #243). A thin REST client, NOT the feature: the account lookup, the count-only
//  projections, the verbs, and the operator authorization all live server-side (api/src/
//  Admin/AccountSupportController behind the Operator Support scope). This module GETs the
//  unified account summary and POSTs the resend-link / extend-TTL / restore-keepsake /
//  resync verbs, and shapes their responses.
//
//  THE COMP/EXTEND-ENTITLEMENT VERB IS NOT HERE (AC-06): it reuses story 02's EXACT grant
//  plumbing - the SupportLookup screen calls purchasersClient.grantEntitlement /
//  revokeEntitlement (POST/DELETE /api/admin/purchasers/...), the SAME write path the
//  session-creation gate reads. There is no second grant write path on this client.
//
//  SEPARATE ADMIN BUNDLE (AC-05, from story 01): this file lives in the admin bundle and
//  imports NOTHING from the kid app. The API base URL comes from
//  `import.meta.env.VITE_API_BASE_URL` (never hardcoded, never a secret). Every call
//  presents the operator credential as `Authorization: Bearer` (the shell holds it in
//  memory - the PRIMARY path on a cross-ORIGIN deployment) AND keeps
//  `credentials: 'include'` so a same-site cookie still rides along. Every call FAILS
//  GRACEFULLY - a network error, non-OK status, or unparseable body resolves to a friendly
//  result rather than throwing.
//
//  THE CROSS-PLANE FIREWALL (AC-08): the wire shapes here are account-plane + content-plane
//  facts ONLY - an account summary (id, email, created-at, grants, subscription metadata,
//  and COUNTS), and verb results carrying a slug + expiry / an outcome. There is no player
//  nickname, room, session, tale byline, tale timestamp, or per-tale list field on the wire,
//  so none can leak into the client. A claim code and a public-tale slug are NEVER search
//  inputs - the lookup takes an email or an AccountId only.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import type { PurchaserGrant } from './purchasersClient';

/** A dependency-tolerant count section: available with a bare integer, or the "not available yet" state. */
export interface SupportCountSection {
  /** True when a real count resolved; false = the "not available yet" state. */
  available: boolean;
  /** The count when available; null otherwise. */
  count: number | null;
}

/** The subscription-state section, derived from grant metadata (billing-entitlements/08). */
export interface SupportSubscriptionSection {
  /** True when the account holds at least one subscription-sourced grant. */
  hasSubscription: boolean;
  /** The plan / product id, or null. */
  plan: string | null;
  /** "active" / "lapsed", or null with no subscription. */
  status: string | null;
  /** The subscription lease end (ISO), or null. */
  validThrough: string | null;
  /** The Stripe subscription id, or null. */
  stripeSubscriptionId: string | null;
  /** The Stripe mode ("test" / "live"), or null. */
  mode: string | null;
}

/** The unified account summary (AC-01/AC-02) - account-plane + content-plane facts only. */
export interface SupportAccountSummary {
  /** True when an account resolves for the query; false = the clear not-found state. */
  accountExists: boolean;
  /** The stable AccountId, or null when not found. */
  accountId: string | null;
  /** The canonical email (the account's when found, else the normalized input). */
  email: string;
  /** When the account was created (ISO), or null when not found. */
  createdUtc: string | null;
  /** The account's current capability leases (the SAME shape story 02 returns). */
  grants: PurchaserGrant[];
  /** The subscription-state section. */
  subscription: SupportSubscriptionSection;
  /** The aggregate vault/tale COUNT section (count-only). */
  vaultTales: SupportCountSection;
  /** The linked-devices COUNT section (count-only). */
  linkedDevices: SupportCountSection;
}

/** The result of an account lookup. */
export interface SupportLookupResult {
  /** True when the call reached the server and returned OK. */
  ok: boolean;
  /** The account summary (null only on a transport / auth failure). */
  summary: SupportAccountSummary | null;
  /** A friendly message to show on failure. */
  message: string;
}

/** The result of a verb action (resend / extend-TTL / restore / resync). */
export interface SupportActionResult {
  /** True when the call reached the server and returned OK (a 429 debounce/cap resolves to false). */
  ok: boolean;
  /** A friendly server (or fallback) message to show. */
  message: string;
}

/** Friendly fallback shown when the endpoints cannot be reached or parsed. */
const UNAVAILABLE_MESSAGE =
  'We could not reach the support console just now - please try again in a moment.';

/** The API base URL, from a NON-secret VITE_ var (the allowlist / keys are never here). */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL;

/** The grant sources the server may return; anything else is treated as an operator grant for display. */
const KNOWN_SOURCES: readonly PurchaserGrant['source'][] = ['Subscription', 'OneTime', 'Operator'];

/** Narrows one unknown grant into a PurchaserGrant, or null if malformed. */
function asGrant(value: unknown): PurchaserGrant | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.capabilityKey !== 'string' || typeof record.label !== 'string') return null;
  if (typeof record.active !== 'boolean') return null;
  const validThrough = typeof record.validThrough === 'string' ? record.validThrough : null;
  const source = KNOWN_SOURCES.find((known) => known === record.source) ?? 'Operator';
  return { capabilityKey: record.capabilityKey, label: record.label, validThrough, source, active: record.active };
}

/** Narrows a count section, defaulting to the "unavailable" state on anything malformed. */
function asCountSection(value: unknown): SupportCountSection {
  if (typeof value !== 'object' || value === null) return { available: false, count: null };
  const record = value as Record<string, unknown>;
  const available = record.available === true;
  const count = typeof record.count === 'number' ? record.count : null;
  return { available: available && count !== null, count: available ? count : null };
}

/** Narrows the subscription section, defaulting to "no subscription" on anything malformed. */
function asSubscription(value: unknown): SupportSubscriptionSection {
  const empty: SupportSubscriptionSection = {
    hasSubscription: false,
    plan: null,
    status: null,
    validThrough: null,
    stripeSubscriptionId: null,
    mode: null,
  };
  if (typeof value !== 'object' || value === null) return empty;
  const record = value as Record<string, unknown>;
  return {
    hasSubscription: record.hasSubscription === true,
    plan: typeof record.plan === 'string' ? record.plan : null,
    status: typeof record.status === 'string' ? record.status : null,
    validThrough: typeof record.validThrough === 'string' ? record.validThrough : null,
    stripeSubscriptionId: typeof record.stripeSubscriptionId === 'string' ? record.stripeSubscriptionId : null,
    mode: typeof record.mode === 'string' ? record.mode : null,
  };
}

/** Narrows the unknown account-summary body, or null if malformed. */
function asSummary(value: unknown): SupportAccountSummary | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (typeof record.accountExists !== 'boolean' || typeof record.email !== 'string') return null;
  const rawGrants = Array.isArray(record.grants) ? record.grants : [];
  const grants: PurchaserGrant[] = [];
  for (const raw of rawGrants) {
    const grant = asGrant(raw);
    if (grant) grants.push(grant);
  }
  return {
    accountExists: record.accountExists,
    accountId: typeof record.accountId === 'string' ? record.accountId : null,
    email: record.email,
    createdUtc: typeof record.createdUtc === 'string' ? record.createdUtc : null,
    grants,
    subscription: asSubscription(record.subscription),
    vaultTales: asCountSection(record.vaultTales),
    linkedDevices: asCountSection(record.linkedDevices),
  };
}

/** Bearer + same-site-cookie headers, matching the other admin clients' auth shape. */
function authHeaders(credential: string | null | undefined, json: boolean): Record<string, string> {
  return {
    ...(json ? { 'Content-Type': 'application/json' } : {}),
    ...(credential ? { Authorization: `Bearer ${credential}` } : {}),
  };
}

/**
 * Looks an account up by email OR AccountId (GET /api/admin/support/accounts/{query}). A claim
 * code or a public-tale slug is NEVER a valid search input - the server resolves only an email or a
 * GUID, so a bridge input simply returns the clear not-found state. Fails gracefully - never throws.
 */
export async function lookupAccount(query: string, credential?: string | null): Promise<SupportLookupResult> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/admin/support/accounts/${encodeURIComponent(query)}`, {
      method: 'GET',
      headers: authHeaders(credential, false),
      credentials: 'include',
    });
    if (!response.ok) {
      return { ok: false, summary: null, message: UNAVAILABLE_MESSAGE };
    }
    const summary = asSummary(await response.json());
    if (!summary) {
      return { ok: false, summary: null, message: UNAVAILABLE_MESSAGE };
    }
    return { ok: true, summary, message: '' };
  } catch {
    return { ok: false, summary: null, message: UNAVAILABLE_MESSAGE };
  }
}

/** Shared POST for the verbs: sends the JSON body, narrows { message } / { ok, message }, never throws. */
async function postVerb(
  path: string,
  body: unknown,
  credential: string | null | undefined,
): Promise<SupportActionResult> {
  try {
    const response = await fetch(`${API_BASE_URL}${path}`, {
      method: 'POST',
      credentials: 'include',
      headers: authHeaders(credential, true),
      body: JSON.stringify(body),
    });
    const parsed: unknown = await response.json().catch(() => null);
    const message =
      typeof parsed === 'object' && parsed !== null && typeof (parsed as Record<string, unknown>).message === 'string'
        ? ((parsed as Record<string, unknown>).message as string)
        : response.ok
          ? ''
          : UNAVAILABLE_MESSAGE;
    return { ok: response.ok, message: message || (response.ok ? '' : UNAVAILABLE_MESSAGE) };
  } catch {
    return { ok: false, message: UNAVAILABLE_MESSAGE };
  }
}

/** Resends a purchaser magic link to an account email (AC-03). */
export async function resendMagicLink(email: string, credential?: string | null): Promise<SupportActionResult> {
  return postVerb('/api/admin/support/resend-link', { email }, credential);
}

/** Extends a public tale's TTL by its slug (AC-04) - the slug is a DIRECT content input, never a search key. */
export async function extendTaleTtl(slug: string, credential?: string | null): Promise<SupportActionResult> {
  return postVerb('/api/admin/support/tales/extend-ttl', { slug }, credential);
}

/**
 * Restores a user's own deleted keepsake by its DIRECT (vaultId, taleId) content identifiers (AC-05),
 * with the single light confirmation the lower-friction courtesy verb requires.
 */
export async function restoreKeepsake(
  vaultId: string,
  taleId: string,
  credential?: string | null,
): Promise<SupportActionResult> {
  return postVerb('/api/admin/support/vault/restore', { vaultId, taleId, confirm: true }, credential);
}

/** Resyncs an account's subscription grants from Stripe by AccountId (AC-07) - debounced per account server-side. */
export async function resyncSubscription(accountId: string, credential?: string | null): Promise<SupportActionResult> {
  return postVerb('/api/admin/support/resync', { accountId }, credential);
}
