// ----------------------------------------------------------------------------
//  IFamilyDeviceTokenStore - the persistence contract for linked family devices
//  (accounts-identity/09, issue #229).
//
//  TWO IMPLEMENTATIONS, chosen once at startup by whether a storage connection
//  string is configured (Program.cs), mirroring the account store's config-presence
//  split with a WORKING in-memory fallback (NOT a disabled no-op): the redeem ->
//  resolve -> list -> revoke flow must be exercisable end-to-end locally with ZERO
//  Azure setup, and a linked device is deliberately long-lived, so a real store is
//  needed rather than a no-op.
//    - TableStorageFamilyDeviceTokenStore : the durable Azure Table Storage impl,
//      used when Accounts:StorageConnectionString is present. PartitionKey = the
//      family AccountId, RowKey = the DeviceTokenId (the ADR's prescribed schema),
//      so listing a family's devices is a single-partition query and resolving one
//      device is a point read.
//    - InMemoryFamilyDeviceTokenStore : the working fallback used with NO connection
//      string (local dev / CI / a fresh clone).
//
//  KEYING / RESOLUTION: a device row is addressed by (AccountId, DeviceTokenId). The
//  raw token embeds both non-secret ids (AC-05 permits "an opaque device-token id and
//  the AccountId it resolves to") plus a secret, so FamilyDeviceLinkService parses the
//  ids out of a presented token, point-reads the row here, and verifies the token HASH
//  (never the raw value, AC-05). The store itself never sees a raw token - it stores
//  and compares hashes only.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Stores linked family-device rows (accounts-identity/09), keyed by
/// <see cref="FamilyDeviceToken.AccountId"/> + <see cref="FamilyDeviceToken.DeviceTokenId"/>.
/// One implementation writes to Azure Table Storage (deployed); the other is a working
/// in-memory store used when no storage connection string is configured (local dev /
/// CI). Holds only the token HASH, never the raw secret (AC-05), and no PII beyond the
/// AccountId it resolves to.
/// </summary>
public interface IFamilyDeviceTokenStore
{
    /// <summary>
    /// Persists a freshly minted device row at redeem time (AC-02). The row already
    /// carries <see cref="FamilyDeviceToken.IsAdultConfirmedDevice"/> = false (the
    /// SAFE default) and a rolling <see cref="FamilyDeviceToken.ExpiresUtc"/>.
    /// </summary>
    Task AddAsync(FamilyDeviceToken token, CancellationToken ct = default);

    /// <summary>
    /// Point-reads a device row by its owning account + device id, or null when absent.
    /// The resolve path (CreateRoom) and refresh path both parse these ids out of the
    /// presented raw token, then confirm the token hash matches this row (AC-03/AC-05).
    /// </summary>
    Task<FamilyDeviceToken?> GetAsync(Guid accountId, Guid deviceTokenId, CancellationToken ct = default);

    /// <summary>
    /// Lists every device linked to <paramref name="accountId"/> (AC-04), for the
    /// Account page's linked-devices list. Includes revoked rows so a just-revoked
    /// device still reads as handled. A single-partition read.
    /// </summary>
    Task<IReadOnlyList<FamilyDeviceToken>> ListByAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the row in place (rotate, touch, revoke, or adult-confirm toggle - all
    /// plain property updates on the SAME row, never a new record, AC-02/AC-04/AC-07).
    /// Returns false when the row no longer exists (revoked-then-deleted races degrade
    /// to a clean no-op rather than an error). The caller passes the full desired row.
    /// </summary>
    Task<bool> UpdateAsync(FamilyDeviceToken token, CancellationToken ct = default);
}
