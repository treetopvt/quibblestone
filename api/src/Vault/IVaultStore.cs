// ----------------------------------------------------------------------------
//  IVaultStore - the storage contract for the anonymous, server-side keepsake
//  vault (keepsake-vault/01, ADR 0003 Layer 2, issue #196).
//
//  Every method here is keyed by a VAULT ID - a device-held, cryptographically
//  random handle (AC-01), never an account, an email, or any PII (AC-04). The
//  controller (VaultController) reads the vault id from the X-Vault-Id request
//  HEADER (never a URL path segment - a path segment leaks the bearer credential
//  to App Insights / access logs / Referer / history) and passes it here; this
//  store never sees a raw email, a nickname-identity link, a room, or a player. It
//  imports NOTHING from api/src/Rooms and never touches GameHub or the round
//  lifecycle - the same isolation precedent the CloudGallery / PublishedTales
//  stores set.
//
//  TWO IMPLEMENTATIONS, chosen once at startup by whether a storage connection
//  string is configured (see Program.cs), MIRRORING the CloudGallery store split
//  (a genuinely WORKING in-memory fallback, NOT the PublishedTales disabled no-op)
//  - the vault is default-on for every anonymous player, so the whole save/list
//  flow this and later stories build on must be exercisable with ZERO Azure setup:
//    - TableStorageVaultStore : the real Azure Table Storage impl, used when
//      Vault:StorageConnectionString is present. PartitionKey = vaultId,
//      RowKey = taleId, so list is a single-partition query.
//    - InMemoryVaultStore     : the WORKING thread-safe fallback used with NO
//      connection string (local dev, CI, a fresh clone). Genuinely stores / lists
//      / expires; IsEnabled is true.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Vault;

/// <summary>
/// The outcome of a <see cref="IVaultStore.SaveAsync"/> call: the tale was stored,
/// or the save was rejected because the owning vault is already at its
/// <see cref="IVaultStore.MaxTalesPerVault"/> cap (AC-07). The controller maps a
/// rejection to a non-2xx; the client's fire-and-forget call simply fails silently
/// (AC-02's never-block posture).
/// </summary>
public enum VaultSaveOutcome
{
    /// <summary>The tale was persisted under its vault id + tale id.</summary>
    Saved,

    /// <summary>The owning vault is at the per-vault cap; the save was rejected without storing (AC-07).</summary>
    RejectedCapExceeded,
}

/// <summary>
/// Stores and lists tales in the anonymous server-side keepsake vault
/// (keepsake-vault/01), keyed by an opaque, random vault id (AC-01). One
/// implementation writes to Azure Table Storage (PartitionKey = vaultId,
/// RowKey = taleId); the other is a working in-memory store used when no storage
/// connection string is configured (local dev / CI). Holds no PII beyond the
/// byline nickname(s) and no room / player reference (AC-04), so it is consumable
/// without importing anything from api/src/Rooms. This is the stable seam
/// keepsake-vault/02-04 and sysadmin-console/07 build on.
/// </summary>
public interface IVaultStore
{
    /// <summary>
    /// The per-vault storage-bloat cap (AC-07): a single vault id holds at most
    /// this many stored tales. A save that would push a vault past this cap is
    /// rejected (no eviction - the vault is a durable archive, not a rolling cache
    /// like the device-local gallery). This bounds per-vault growth independent of
    /// the per-IP rate limiter (AC-06), which alone is defeated by an attacker
    /// rotating source IPs against ONE vault id. A settings-key candidate shipped
    /// as a code constant until control-plane/01 exists.
    /// </summary>
    const int MaxTalesPerVault = 500;

    /// <summary>
    /// Whether the store is available. Both implementations return true (the
    /// in-memory fallback is a WORKING store, not a no-op), so availability is
    /// never gated by this flag - it exists for parity with the sibling stores'
    /// contract. Access is gated by possession of the vault id + the AC-01
    /// length/format floor + the AC-06 rate limits.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Persists one already-vetted, already-filtered tale under its vault id + tale
    /// id (AC-02/AC-04), unless the vault is already at <see cref="MaxTalesPerVault"/>
    /// (AC-07). The caller mints the tale id (see SlugGenerator), presents the vault
    /// id, re-vets every part + the byline, and server-stamps CreatedUtc BEFORE
    /// calling this - the store only stores (and enforces the cap). Returns
    /// <see cref="VaultSaveOutcome.RejectedCapExceeded"/> without storing when the
    /// cap is reached.
    /// </summary>
    Task<VaultSaveOutcome> SaveAsync(VaultTale tale, CancellationToken ct = default);

    /// <summary>
    /// Lists all NON-EXPIRED tales for one vault id (AC-02/AC-03), a single-
    /// partition query (PartitionKey = vaultId) - the vault has no single-slug read
    /// path; every read is a partition list. Expiry is applied HERE: each row's
    /// computed expiry (<see cref="VaultTale.IsExpired"/>, CreatedUtc + TtlDays) is
    /// checked as the partition is enumerated, expired rows are omitted from the
    /// result, and each expired row found is best-effort deleted to reclaim it. A
    /// vault with no tales gets an empty list, never an error.
    /// </summary>
    Task<IReadOnlyList<VaultTale>> ListAsync(string vaultId, CancellationToken ct = default);
}
