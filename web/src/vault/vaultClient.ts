// ----------------------------------------------------------------------------
//  vaultClient.ts - the web client for the anonymous, server-side keepsake vault
//  (keepsake-vault/01, ADR 0003 Decision 2 / Layer 2, issue #196). A thin REST
//  client, NOT the feature: the real vault (re-vet, tale-id minting, storage, the
//  computed TTL, the per-vault cap) lives server-side in api/src/Vault/. This
//  module only POSTs the already-assembled tale and (for keepsake-vault/02) GETs
//  the vault's tales.
//
//  Mirrors web/src/gallery/publishTale.ts: the API base URL comes from
//  `import.meta.env.VITE_API_BASE_URL` (never hardcoded, CLAUDE.md section 4), and
//  it FAILS GRACEFULLY - a network error, a non-OK status, or an unparseable body
//  resolves null / false rather than throwing.
//
//  THE VAULT ID IS A BEARER CREDENTIAL, CARRIED IN A HEADER (AC-02, ADR 0003
//  "Handles are secrets"): every call sends the id in the `X-Vault-Id` request
//  HEADER, NEVER interpolated into a URL path or query string (a path segment would
//  leak the credential to access logs / Referer / history). There is no vault id in
//  any URL this module builds.
//
//  FIRE-AND-FORGET AUTO-SAVE (AC-02): `autoSaveTaleToVault` is the one call the
//  reveal screen makes. It resolves the durable vault id (minting on first use, see
//  vaultId.ts), then POSTs - swallowing EVERY failure so a vault/network problem
//  can never block, delay, or visibly degrade the reveal (mirrors localGallery's
//  `saveTale()` posture at Reveal.tsx's handleSaveImage). The caller invokes it as
//  `void autoSaveTaleToVault(...)` and never awaits it.
//
//  Child safety (AC-04): this client sends only the already-assembled, already-
//  filtered story parts and the in-session nicknames - never raw submissions, never
//  PII. The SERVER re-vets every part + the byline before it ever stores them; a
//  lying client cannot get unfiltered content into the vault.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { getVaultId } from './vaultId';

/** One ordered part of a tale body sent to / read from the vault: literal text or a coral word. */
export interface VaultTalePart {
  /** True for a player-supplied coral word (the server re-vets these), false for literal template text. */
  isWord: boolean;
  /** The part's text (a template run, or one already-vetted player word). */
  text: string;
}

/** Input to {@link autoSaveTaleToVault} / {@link saveTaleToVault}: the already-assembled tale plus its byline. */
export interface SaveVaultTaleInput {
  /** The tale title (already shown on the reveal). */
  title: string;
  /** The ordered body parts (from the reveal's assembled parts - literal text interleaved with coral words). */
  parts: VaultTalePart[];
  /** The joined in-session nicknames for the byline (e.g. "Sam & Mia"), or undefined for no crew. */
  bylineNames?: string;
}

/** One tale in the vault-list response (consumed by keepsake-vault/02). */
export interface VaultTaleView {
  /** The tale's server-minted id. */
  taleId: string;
  /** The tale title. */
  title: string;
  /** The ordered body parts. */
  parts: VaultTalePart[];
  /** The "carved by" byline nickname(s); may be empty. */
  bylineNames: string;
  /** When the tale was saved (server-stamped ISO instant). */
  createdUtc: string;
}

/** The request header carrying the vault-id bearer credential (never a URL path/query). */
const VAULT_ID_HEADER = 'X-Vault-Id';

/**
 * POSTs an already-assembled, already-filtered tale to the vault under the given
 * vault id (POST /api/vault/tales, vault id in the X-Vault-Id header). Resolves
 * `true` on success, `false` on any failure (network error, non-OK status such as
 * the 409 vault-full or a rejected re-vet, or an unparseable state) - never throws.
 */
export async function saveTaleToVault(vaultId: string, input: SaveVaultTaleInput): Promise<boolean> {
  if (vaultId.length === 0) return false;
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/vault/tales`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        [VAULT_ID_HEADER]: vaultId,
      },
      body: JSON.stringify({
        title: input.title,
        parts: input.parts,
        bylineNames: input.bylineNames ?? '',
        // NOTE: no createdUtc - the server stamps it (AC-02); a client value is ignored.
      }),
    });
    return response.ok;
  } catch {
    return false;
  }
}

/**
 * Auto-saves a completed reveal to the vault, fire-and-forget (AC-02): resolves the
 * durable device vault id (minting on first use), then POSTs. Swallows EVERY
 * failure - a missing/failed vault id or any network error resolves silently - so
 * this can never block, delay, or degrade the reveal. Call it as
 * `void autoSaveTaleToVault(...)`.
 */
export async function autoSaveTaleToVault(input: SaveVaultTaleInput): Promise<void> {
  try {
    const vaultId = await getVaultId();
    if (vaultId === null) return; // no strong id available this time - skip (a later save retries)
    await saveTaleToVault(vaultId, input);
  } catch {
    // Belt-and-braces: never let an auto-save fault reach the caller.
  }
}

/** Narrows an unknown parsed list body into a VaultTaleView[], or null if it does not match. */
function asVaultTales(value: unknown): VaultTaleView[] | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  if (!Array.isArray(record.tales)) return null;
  const tales: VaultTaleView[] = [];
  for (const raw of record.tales) {
    if (typeof raw !== 'object' || raw === null) continue;
    const t = raw as Record<string, unknown>;
    if (typeof t.taleId !== 'string' || typeof t.title !== 'string') continue;
    const parts: VaultTalePart[] = Array.isArray(t.parts)
      ? t.parts
          .filter((p): p is Record<string, unknown> => typeof p === 'object' && p !== null)
          .map((p) => ({ isWord: p.isWord === true, text: typeof p.text === 'string' ? p.text : '' }))
      : [];
    tales.push({
      taleId: t.taleId,
      title: t.title,
      parts,
      bylineNames: typeof t.bylineNames === 'string' ? t.bylineNames : '',
      createdUtc: typeof t.createdUtc === 'string' ? t.createdUtc : '',
    });
  }
  return tales;
}

/**
 * GETs the vault's non-expired tales (GET /api/vault/tales, vault id in the
 * X-Vault-Id header) - consumed by keepsake-vault/02's gallery merge. Resolves the
 * tale list on success, or `null` on any failure (network error, non-OK status, an
 * unparseable body) so the caller can fall back to the device-local gallery alone.
 * Never throws.
 */
export async function listVaultTales(vaultId: string): Promise<VaultTaleView[] | null> {
  if (vaultId.length === 0) return null;
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/vault/tales`, {
      method: 'GET',
      headers: { [VAULT_ID_HEADER]: vaultId },
    });
    if (!response.ok) return null;
    const body: unknown = await response.json();
    return asVaultTales(body);
  } catch {
    return null;
  }
}
