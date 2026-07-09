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
/// The outcome of a <see cref="IVaultStore.RedeemClaimCodeAsync"/> call
/// (keepsake-vault/03, AC-02/AC-03): the calling device's vault id was aliased to the
/// claimed vault, or the code did not redeem (unknown / expired / burned). The
/// controller maps both to a uniform response so redemption is not an oracle -
/// success is only ever useful to a holder of a valid code (AC-06).
/// </summary>
public enum VaultRedeemOutcome
{
    /// <summary>The code was valid and unexpired; the calling device's vault id is now an alias for the claimed vault (AC-02).</summary>
    Redeemed,

    /// <summary>The code was unknown, expired, or burned - nothing was aliased (AC-03/AC-07).</summary>
    InvalidOrExpired,
}

/// <summary>
/// Stores and lists tales in the anonymous server-side keepsake vault
/// (keepsake-vault/01), keyed by an opaque, random vault id (AC-01), and - since
/// keepsake-vault/03 (#230) - carries a vault's CLAIM state (family-account claim +
/// recovery claim code) as a tiny companion row and resolves device-alias links
/// created by claim-code redemption. One implementation writes to Azure Table Storage
/// (PartitionKey = vaultId, RowKey = taleId, plus sentinel-keyed claim / alias / code
/// -index rows); the other is a working in-memory store used when no storage
/// connection string is configured (local dev / CI). Holds no PII beyond the byline
/// nickname(s) and the non-PII family AccountId GUID on a claim (AC-04/AC-06), and no
/// room / player / kid-profile reference, so it is consumable without importing
/// anything from api/src/Rooms. This is the stable seam keepsake-vault/02-04 and
/// sysadmin-console/07 build on.
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
    /// Lists all LIVE tales for one vault id (AC-02/AC-03), a single-partition query
    /// (PartitionKey = vaultId) - the vault has no single-slug read path; every read
    /// is a partition list. Exclusions are applied HERE as the partition is enumerated:
    ///   - TTL expiry (keepsake-vault/01, AC-03): a row past its computed
    ///     <see cref="VaultTale.IsExpired"/> (CreatedUtc + TtlDays) is omitted and
    ///     best-effort deleted to reclaim it.
    ///   - SOFT-DELETE (keepsake-vault/04, AC-01): a soft-deleted row
    ///     (<see cref="VaultTale.IsDeleted"/>) is omitted immediately so it stops
    ///     appearing in the vault's own GET and the merged gallery view. A
    ///     soft-deleted row whose restore window has fully elapsed
    ///     (<see cref="VaultTale.IsRestoreWindowElapsed"/>, AC-03) is ALSO best-effort
    ///     deleted to reclaim it - within the window it is retained (omitted, not
    ///     purged) so <see cref="RestoreAsync"/> can still recover it.
    /// A vault with no live tales gets an empty list, never an error.
    ///
    /// keepsake-vault/03 (#230): the passed id is first resolved through any
    /// device-ALIAS link (created by claim-code redemption, AC-02) to the canonical
    /// claimed vault, so a recovered device reading under its OWN id sees the claimed
    /// vault's tales. And a CLAIMED vault's tales NEVER TTL-expire (AC-05): when the
    /// resolved vault is claimed, the TTL filter is skipped entirely - claiming is
    /// the durability upgrade the whole feature exists to offer. The soft-delete
    /// exclusion still applies to a claimed vault (claiming exempts the passive TTL,
    /// not a deliberate delete).
    /// </summary>
    Task<IReadOnlyList<VaultTale>> ListAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// SOFT-deletes one tale in a vault (keepsake-vault/04, AC-01): the tale is
    /// marked deleted (DeletedUtc stamped, server-side) and stops appearing in
    /// <see cref="ListAsync"/> immediately, rather than being removed - so it stays
    /// recoverable within the restore window (AC-02). Idempotent: soft-deleting an
    /// already-soft-deleted (still-within-window) tale is a no-op success. Returns
    /// false for a tale that does not exist, has TTL-expired, or is already past its
    /// restore window (genuinely gone - nothing to soft-delete). This is the
    /// player-facing delete the DELETE /api/vault/tales/{taleId} endpoint calls; it
    /// requires NO confirmation marker - undoing a family's own delete of content
    /// only that family saw is materially lower-risk than a moderation-takedown
    /// restore, which is why the two restore paths do not share a shape (AC-07).
    /// </summary>
    /// <param name="vaultId">The owning vault id (the partition key, a bearer credential).</param>
    /// <param name="taleId">The tale id to soft-delete (the row key within the vault partition).</param>
    Task<bool> SoftDeleteAsync(string vaultId, string taleId, CancellationToken ct = default);

    /// <summary>
    /// RESTORES a soft-deleted tale within its restore window (keepsake-vault/04,
    /// AC-02/AC-06): clears the deletion marker so the tale reappears in
    /// <see cref="ListAsync"/> exactly as it was before deletion - EXACTLY the
    /// already-filtered content that existed before, unmodified, with no re-vet and
    /// no content mutation (AC-05/AC-06). Returns the restored (now-live) tale, or
    /// null when the tale does not exist, has TTL-expired, or is past its restore
    /// window (genuinely gone per AC-03 - un-deleting a lapsed tale is out of scope).
    /// Restoring an already-live tale is a harmless no-op that returns it unchanged.
    ///
    /// DELIBERATELY takes NO confirmation argument (AC-07): a player restoring their
    /// own accidental delete only re-exposes content their own family already saw.
    /// The higher-risk path - restoring a moderation TAKEDOWN, which re-exposes
    /// content an operator confirmed hidden - is
    /// <see cref="PublishedTales.IPublishedTaleStore.RestoreFromTakedownAsync"/>,
    /// whose signature DOES require a confirmation marker this one has no equivalent
    /// of. This story ships the STORE method only; the operator console verb that
    /// calls it is sysadmin-console/07 (there is no public endpoint for it here).
    /// </summary>
    /// <param name="vaultId">The owning vault id (the partition key, a bearer credential).</param>
    /// <param name="taleId">The tale id to restore (the row key within the vault partition).</param>
    Task<VaultTale?> RestoreAsync(string vaultId, string taleId, CancellationToken ct = default);

    /// <summary>
    /// Claims a vault into a family account (keepsake-vault/03, AC-01): associates the
    /// vault (resolved through any alias) with the stable, non-PII
    /// <paramref name="accountId"/> and mints a fresh recovery claim code (returning
    /// the resulting <see cref="VaultClaim"/>). The vault's tales become permanent
    /// (TTL no longer applies, AC-05) and reachable from any device signed into the
    /// same account. Idempotent-friendly: re-claiming a vault already claimed by the
    /// SAME account rotates the code and preserves the original ClaimedUtc; the store
    /// does NOT consolidate multiple vaults and does NOT transfer a vault claimed by a
    /// DIFFERENT account (out of scope) - it simply re-associates and re-mints. NEVER
    /// keyed to a kid profile / seat preset (AC-04, ADR 0003 Decision 1).
    /// </summary>
    /// <param name="vaultId">The vault id to claim (resolved through any alias first).</param>
    /// <param name="accountId">The stable family account id (accounts-identity/05) - non-PII.</param>
    Task<VaultClaim> ClaimAsync(string vaultId, Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Explicitly revokes and regenerates a claimed vault's recovery code (AC-07):
    /// mints a fresh code with a fresh validity window, resets the failed-attempt
    /// count, and immediately invalidates the prior code. Callable by ANY device
    /// already holding (or aliased to) the vault id - no account required, since this
    /// is the account-free recovery path's own revoke action. Returns the regenerated
    /// <see cref="VaultClaim"/>, or null when the vault has never been claimed (there
    /// is no code to regenerate - the controller maps null to a 404).
    /// </summary>
    /// <param name="vaultId">The vault id whose code to regenerate (resolved through any alias first).</param>
    Task<VaultClaim?> RegenerateClaimCodeAsync(string vaultId, CancellationToken ct = default);

    /// <summary>
    /// Redeems a recovery claim code from a NEW device (keepsake-vault/03, AC-02).
    /// When <paramref name="claimCode"/> (already normalized to canonical form by the
    /// caller) matches a claimed vault's CURRENT, unexpired, non-burned code, the
    /// server records <paramref name="callingDeviceVaultId"/> as an ALIAS for that
    /// claimed vault - so a later fetch under the calling device's own id resolves the
    /// same tales, WITHOUT the device ever learning (or needing) the canonical vault
    /// id or an account (AC-06). NOT single-use: the same still-valid code may be
    /// redeemed by more than one device within its window (AC-07). A FAILED redemption
    /// that RESOLVES to a vault (a code matched to a vault but rejected - expired, or a
    /// just-retired code hammered from a new device) increments that vault's
    /// per-code failed-attempt count and, at the burn threshold (AC-03.3), invalidates
    /// the code and mints a fresh one. A code that resolves to NO vault is a blind miss
    /// bounded by the per-IP limiter + the global ceiling (AC-03.1/AC-03.2), not
    /// attributable to any one code. A successful redemption resets the count.
    /// </summary>
    /// <param name="claimCode">The submitted code, ALREADY normalized to canonical form (ClaimCodeGenerator.Normalize).</param>
    /// <param name="callingDeviceVaultId">The redeeming device's own well-formed vault id, aliased to the claimed vault on success.</param>
    Task<VaultRedeemOutcome> RedeemClaimCodeAsync(string claimCode, string callingDeviceVaultId, CancellationToken ct = default);

    /// <summary>
    /// Reads a vault's current claim state (keepsake-vault/03) for the gallery to
    /// surface the live code + expiry (AC-02/AC-07) and for the TTL exemption (AC-05).
    /// Resolves the id through any alias first. AUTO-ROTATION (AC-07): if the current
    /// code is expired (or already burned), a fresh code is minted and persisted here
    /// before returning, so the family always sees a live, working code the moment
    /// they open the gallery - without needing to notice expiry. Returns null when the
    /// vault has never been claimed.
    /// </summary>
    /// <param name="vaultId">The vault id to read claim state for (resolved through any alias first).</param>
    Task<VaultClaim?> GetClaimAsync(string vaultId, CancellationToken ct = default);
}
