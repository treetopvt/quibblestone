// ----------------------------------------------------------------------------
//  vaultId.ts - mints (or reads) the durable device-held keepsake-vault id
//  (keepsake-vault/01, ADR 0003 Decision 2 / "Handles are secrets", issue #196).
//
//  The vault id is the device's handle to its anonymous, server-side keepsake
//  vault: every completed reveal auto-saves under it (see vaultClient.ts), and any
//  future vault-aware surface (keepsake-vault/02's gallery) reads it. It is
//  persisted DURABLY on-device in localStorage under a single key and REUSED for
//  every future save from that device (AC-01), mirroring the opaque,
//  cryptographically random, never-identity-derived handle pattern the API's
//  Room.NewReconnectToken() establishes.
//
//  THE VAULT ID IS A BEARER CREDENTIAL, so the entropy floor is non-negotiable
//  (AC-01, ADR 0003 "Handles are secrets"):
//    - The ONLY accepted client-side mint path is `crypto.randomUUID()`. Unlike
//      localGallery.ts's `generateTaleId()` - which deliberately falls back to a
//      Math.random-based string because a local gallery ROW id is not a credential
//      - this module must NOT copy that fallback: a weak vault id is a forgeable
//      bearer credential.
//    - When `crypto.randomUUID` is unavailable, we call the server's
//      `POST /api/vault/mint` for a RandomNumberGenerator-backed id instead of
//      generating a weak one locally. If that call also fails (offline, no server),
//      we return null and the caller simply skips the save - never a Math.random id.
//    - The server independently enforces a length/format floor on every vault
//      endpoint, so even a forged id would be rejected there; this is defence in
//      depth, not the only line.
//
//  Idempotent: once minted, the id is read straight back from localStorage on
//  every later call, so the SAME device keeps the SAME vault forever (until its
//  storage is cleared - expected for device-local, account-free storage, exactly
//  like the local gallery).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/** The single localStorage key the durable vault id is persisted under. */
export const VAULT_ID_STORAGE_KEY = 'quibblestone.vaultId';

// The client-side mirror of the server's VaultId.IsWellFormed floor (AC-01): a
// UUID's 36 chars minimum, a generous upper bound, and the random-token charset
// (ASCII letters, digits, hyphen). Kept in lock-step with api/src/Vault/VaultId.cs
// so a value this module persists / reads is one the server would also accept.
const MIN_VAULT_ID_LENGTH = 36;
const MAX_VAULT_ID_LENGTH = 200;
const VAULT_ID_SHAPE = /^[A-Za-z0-9-]+$/;

/**
 * True when a candidate id meets the same length/format floor the server enforces
 * on every vault endpoint. Used to reject a corrupt / weak / truncated stored value
 * (user or devtools tampering) so it is re-minted rather than presented forever - a
 * malformed id would otherwise fail the server floor on every save and silently
 * brick auto-save until storage is cleared.
 */
function isWellFormedVaultId(candidate: string): boolean {
  return (
    candidate.length >= MIN_VAULT_ID_LENGTH &&
    candidate.length <= MAX_VAULT_ID_LENGTH &&
    VAULT_ID_SHAPE.test(candidate)
  );
}

/** Narrows an unknown parsed mint response into the server-minted id, or null. */
function readMintedId(value: unknown): string | null {
  if (typeof value !== 'object' || value === null) return null;
  const record = value as Record<string, unknown>;
  return typeof record.vaultId === 'string' && record.vaultId.length > 0 ? record.vaultId : null;
}

/**
 * Reads the persisted vault id, swallowing any storage-access failure (private
 * mode, blocked). Returns the stored value ONLY when it still meets the floor
 * (isWellFormedVaultId) - a corrupt / weak / truncated value reads as absent so the
 * caller re-mints a valid one rather than presenting a bricked id forever.
 */
function readStored(): string | null {
  try {
    const stored = localStorage.getItem(VAULT_ID_STORAGE_KEY);
    return stored !== null && isWellFormedVaultId(stored) ? stored : null;
  } catch {
    return null;
  }
}

/** Persists the vault id durably, swallowing any storage-access failure. */
function writeStored(vaultId: string): void {
  try {
    localStorage.setItem(VAULT_ID_STORAGE_KEY, vaultId);
  } catch {
    // Storage unavailable (private mode, quota, blocked): the id still works for
    // this session's saves; it just will not survive a reload. Never throw.
  }
}

/** Asks the server to mint a CSPRNG-backed vault id (AC-01 fallback). Resolves null on any failure. */
async function mintFromServer(): Promise<string | null> {
  try {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/vault/mint`, {
      method: 'POST',
    });
    if (!response.ok) return null;
    const body: unknown = await response.json();
    return readMintedId(body);
  } catch {
    return null;
  }
}

/**
 * Returns this device's durable vault id, minting and persisting one on first use
 * (AC-01). Idempotent: a second call returns the SAME stored id. Mints ONLY with
 * `crypto.randomUUID()`; when that is unavailable it asks the server
 * (`POST /api/vault/mint`) - there is NO Math.random fallback, because the vault id
 * is a bearer credential. Resolves null only when the device has no id, cannot
 * mint one locally, AND the server mint failed (offline) - the caller then simply
 * skips the fire-and-forget save.
 */
export async function getVaultId(): Promise<string | null> {
  const existing = readStored();
  if (existing !== null) return existing;

  // The only accepted local mint path (a real CSPRNG). `crypto`/`randomUUID` may be
  // absent on an insecure origin or an old engine - guard rather than assume.
  const cryptoObj: Crypto | undefined = typeof crypto !== 'undefined' ? crypto : undefined;
  if (cryptoObj !== undefined && typeof cryptoObj.randomUUID === 'function') {
    const minted = cryptoObj.randomUUID();
    writeStored(minted);
    return minted;
  }

  // No crypto.randomUUID: the server mints a strong id (never a weak local one).
  const serverMinted = await mintFromServer();
  if (serverMinted !== null) {
    writeStored(serverMinted);
    return serverMinted;
  }

  // No id, no local mint, no server: skip the save this time (a later call retries).
  return null;
}
