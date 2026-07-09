// ----------------------------------------------------------------------------
//  vaultClaimClient.ts - the web client for keepsake-vault/03's claim + recovery
//  surface (issue #230). A thin REST client, NOT the feature: the claim/redeem
//  logic, the claim-code generator, the anti-brute-force controls (AC-03), and
//  the AccountId association all live server-side in api/src/Vault/. This
//  module only shapes the four calls the Gallery screen makes and their
//  responses.
//
//  Mirrors ../vault/vaultClient.ts and ../gallery/cloudGalleryClient.ts:
//    - API base from `import.meta.env.VITE_API_BASE_URL` (never hardcoded,
//      CLAUDE.md section 4).
//    - THE VAULT ID IS A BEARER CREDENTIAL, CARRIED IN A HEADER (AC-02, ADR
//      0003 "Handles are secrets"): every call sends it in the `X-Vault-Id`
//      request HEADER, never a URL path/query. There is no vault id in any
//      URL this module builds.
//    - THE CLAIM CODE IS ALSO A BEARER SECRET (AC-02): a human-typed recovery
//      code travels in the redemption request's JSON BODY, never a route
//      segment or query string.
//    - Every call FAILS GRACEFULLY - a network error, non-OK status, or an
//      unparseable body resolves null/false rather than throwing.
//
//  AUTH BOUNDARY (mirrors keepsake-gallery/05's invariant): `claimVault` is the
//  ONLY call here that touches a family credential, and it takes the
//  credential as a plain string param (like cloudGalleryClient.ts) rather than
//  reading any session context itself - the caller (Gallery.tsx) is the one
//  that decides whether a family credential is in scope. The child-facing
//  reveal / join / lobby flow never imports this module's claim path.
//
//  Child safety / no PII (AC-04/AC-06): a claim code carries no identity of
//  its own - it is an opaque, unguessable handle that only ever re-links a
//  vault id to whichever device redeems it. `redeemClaimCode` mints this
//  device's OWN vault id (via getVaultId, minting on first use if absent - the
//  alias needs a target to attach to) rather than accepting one from the
//  caller, so this module never accepts or surfaces a vault id from outside
//  its own storage.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { getVaultId } from './vaultId';

/** The request header carrying the vault-id bearer credential (never a URL path/query). */
const VAULT_ID_HEADER = 'X-Vault-Id';

const BASE_URL = (): string => `${import.meta.env.VITE_API_BASE_URL}/api/vault`;

/** The live claim-code view (GET /api/vault/claim, POST /claim, POST /claim-code/regenerate). */
export interface VaultClaimCode {
  /** The current active recovery code, already grouped for display (e.g. "K5Q-2NX-8CP"). */
  claimCode: string;
  /** When the current code stops working (AC-07); a fresh one auto-mints on the next gallery open. */
  claimCodeExpiresUtc: string;
}

/** Result of GET /api/vault/claim: whether the vault is claimed and, if so, its live code. */
export interface VaultClaimStatus {
  claimed: boolean;
  code: VaultClaimCode | null;
}

/** Narrows an unknown parsed body into a VaultClaimCode, or null if it does not match. */
function asClaimCode(value: unknown): VaultClaimCode | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (typeof r.claimCode !== 'string' || typeof r.claimCodeExpiresUtc !== 'string') return null;
  return { claimCode: r.claimCode, claimCodeExpiresUtc: r.claimCodeExpiresUtc };
}

/** Narrows an unknown parsed body into a VaultClaimStatus, or null if it does not match. */
function asClaimStatus(value: unknown): VaultClaimStatus | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  if (typeof r.claimed !== 'boolean') return null;
  if (!r.claimed) return { claimed: false, code: null };
  // A claimed vault MUST carry a well-formed code; a malformed code shape means an
  // unparseable body, which the contract resolves to null (never { claimed: true,
  // code: null }, which would leave the UI with neither a CTA nor a code).
  const code = asClaimCode(r.code);
  return code === null ? null : { claimed: true, code };
}

/**
 * GETs the given vault's claim status (GET /api/vault/claim, vault id in the
 * X-Vault-Id header). Resolves `{ claimed, code }` on success, or `null` on any
 * failure (network error, non-OK status, an unparseable body). Never throws.
 */
export async function getVaultClaim(vaultId: string): Promise<VaultClaimStatus | null> {
  if (vaultId.length === 0) return null;
  try {
    const response = await fetch(`${BASE_URL()}/claim`, {
      method: 'GET',
      headers: { [VAULT_ID_HEADER]: vaultId },
    });
    if (!response.ok) return null;
    const body: unknown = await response.json();
    return asClaimStatus(body);
  } catch {
    return null;
  }
}

/**
 * Claims the vault into the signed-in family account (POST /api/vault/claim,
 * vault id in the X-Vault-Id header, family credential as `Authorization:
 * Bearer`). Resolves the freshly minted claim code on success, or `null` on
 * any failure - including a 401 (not signed in / no account) or a 400 (bad
 * vault id). Never throws.
 */
export async function claimVault(vaultId: string, credential: string): Promise<VaultClaimCode | null> {
  if (vaultId.length === 0 || credential.length === 0) return null;
  try {
    const response = await fetch(`${BASE_URL()}/claim`, {
      method: 'POST',
      headers: {
        [VAULT_ID_HEADER]: vaultId,
        Authorization: `Bearer ${credential}`,
      },
    });
    if (!response.ok) return null;
    const body: unknown = await response.json();
    return asClaimCode(body);
  } catch {
    return null;
  }
}

/**
 * Explicitly revokes and regenerates the vault's current claim code (POST
 * /api/vault/claim-code/regenerate, vault id in the X-Vault-Id header, AC-07).
 * Any device already holding/aliased to the vault id may call this - no
 * account required. Resolves the freshly minted code on success, or `null` on
 * any failure (including a 404 when the vault was never claimed). Never
 * throws.
 */
export async function regenerateClaimCode(vaultId: string): Promise<VaultClaimCode | null> {
  if (vaultId.length === 0) return null;
  try {
    const response = await fetch(`${BASE_URL()}/claim-code/regenerate`, {
      method: 'POST',
      headers: { [VAULT_ID_HEADER]: vaultId },
    });
    if (!response.ok) return null;
    const body: unknown = await response.json();
    return asClaimCode(body);
  } catch {
    return null;
  }
}

/** Narrows an unknown parsed redeem-response body into its `redeemed` flag, or null if malformed. */
function asRedeemed(value: unknown): boolean | null {
  if (typeof value !== 'object' || value === null) return null;
  const r = value as Record<string, unknown>;
  return typeof r.redeemed === 'boolean' ? r.redeemed : null;
}

/**
 * Redeems a human-typed recovery code onto THIS device (POST
 * /api/vault/claim-code/redeem, AC-02). Resolves this device's own vault id
 * first (minting one on first use if absent - the redemption aliases that id
 * to the claimed vault, so it needs a target to attach), sends it in the
 * X-Vault-Id header, and carries the code in the request BODY (never a route
 * segment or query string - a claim code is a bearer secret). Resolves
 * `true` only when the server reports the code redeemed; `false` on any
 * failure (network error, non-OK status, a malformed/expired/wrong code, or
 * no vault id could be resolved). Never throws.
 */
export async function redeemClaimCode(code: string): Promise<boolean> {
  try {
    const vaultId = await getVaultId();
    if (vaultId === null) return false;
    const response = await fetch(`${BASE_URL()}/claim-code/redeem`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        [VAULT_ID_HEADER]: vaultId,
      },
      body: JSON.stringify({ code }),
    });
    if (!response.ok) return false;
    const body: unknown = await response.json();
    return asRedeemed(body) === true;
  } catch {
    return false;
  }
}
