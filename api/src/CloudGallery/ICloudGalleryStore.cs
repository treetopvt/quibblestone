// ----------------------------------------------------------------------------
//  ICloudGalleryStore - the storage contract for a purchaser's cloud-synced
//  keepsake gallery (keepsake-gallery/05, issue #154).
//
//  THE AUTH BOUNDARY THIS STORE SITS BEHIND: every method here is keyed by an
//  ownerKey - since accounts-identity/05 (#195) the PURCHASER account's STABLE id
//  (account.Id.ToString(), a random GUID), no longer a hash of the mutable email.
//  The controller (CloudGalleryController) resolves the signed-in purchaser from
//  their credential and passes the owner key; this store never sees a raw email, a
//  nickname, a room, or a player. It imports NOTHING from api/src/Rooms and never
//  touches GameHub or the round lifecycle - the same isolation precedent
//  keepsake-gallery/04's published-tale store set, and the child-facing game flow
//  never touches a purchaser credential (AC-01, the auth-boundary invariant).
//
//  NO PII (AC-05): the stored CloudTale carries only the byline nickname(s) and
//  the already-filtered story - no email, no real name. The purchaser identity is
//  the OPAQUE account-id owner key, so an operator listing keys sees ids, not inboxes.
//
//  TWO IMPLEMENTATIONS, chosen once at startup by whether a storage connection
//  string is configured (see Program.cs), MIRRORING the account store's split
//  (NOT the published-tale "disabled no-op"):
//    - TableStorageCloudGalleryStore : the real Azure Table Storage impl, used
//      when CloudGallery:StorageConnectionString is present (a deployed
//      environment). PartitionKey = ownerKey, RowKey = taleId, so list-by-owner is
//      a single-partition query.
//    - InMemoryCloudGalleryStore     : the WORKING thread-safe fallback used with
//      NO connection string (local dev, CI, a fresh clone). UNLIKE the
//      published-tale disabled no-op, this genuinely stores/lists/deletes so the
//      whole save -> list -> delete flow is exercisable with ZERO Azure setup.
//      IsEnabled is true.
//
//  <see cref="IsEnabled"/> exists for parity with the published-tale store; both
//  implementations here return true (there is no disabled no-op), so the gate that
//  actually decides availability is the entitlement seam + a valid purchaser
//  credential (AC-04), not this flag.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.CloudGallery;

/// <summary>
/// Stores, lists, and deletes a purchaser's cloud-synced keepsake tales
/// (keepsake-gallery/05), keyed by an opaque owner key (the account's stable id, a
/// GUID - accounts-identity/05). One implementation writes to Azure Table Storage
/// (PartitionKey = ownerKey, RowKey = taleId); the other is a working in-memory
/// store used when no storage connection string is configured (local dev / CI).
/// Holds no PII beyond the byline nickname(s) and no room / player reference
/// (AC-05), so it is consumable without importing anything from api/src/Rooms.
/// </summary>
public interface ICloudGalleryStore
{
    /// <summary>
    /// Whether the store is available. Both implementations return true (the
    /// in-memory fallback is a WORKING store, not a no-op), so availability is
    /// gated by the entitlement seam + a valid purchaser credential, not this flag
    /// (AC-04). Kept for parity with the published-tale store's contract.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Persists one already-vetted, already-filtered tale under its owner key +
    /// tale id (AC-01/AC-05). The caller mints the tale id (see SlugGenerator),
    /// derives the owner key (the account's stable id, accounts-identity/05), and
    /// re-vets every part + the byline BEFORE calling this - the store only stores.
    /// </summary>
    Task SaveAsync(CloudTale tale, CancellationToken ct = default);

    /// <summary>
    /// Lists all tales for one owner (AC-01/AC-03), a single-partition query
    /// (PartitionKey = ownerKey). Search / filter / sort run client-side over this
    /// bounded per-purchaser result set (the datastore decision), so this returns
    /// the whole set for an owner rather than a query surface. An owner with no
    /// tales gets an empty list, never an error.
    /// </summary>
    Task<IReadOnlyList<CloudTale>> ListByOwnerAsync(string ownerKey, CancellationToken ct = default);

    /// <summary>
    /// Deletes one tale from an owner's gallery (AC-06). Idempotent: deleting an
    /// unknown / already-gone tale id (or one that is not this owner's) is a no-op,
    /// never an error - a point delete scoped to the owner partition so a purchaser
    /// can only ever delete their own tale.
    /// </summary>
    Task DeleteAsync(string ownerKey, string taleId, CancellationToken ct = default);

    /// <summary>
    /// Removes EVERY tale for one owner (AC-06, revoke-cloud-sync / delete-account):
    /// the purchaser's cloud-stored tales are gone within a bounded window. A
    /// single-partition delete scoped to the owner - it never touches another
    /// owner's tales. Idempotent: revoking an empty gallery succeeds. NOT
    /// best-effort-silent: a genuine storage failure mid-sweep PROPAGATES (so the
    /// caller surfaces a non-2xx and the client retries until the gallery reads
    /// empty) rather than reporting success with rows still present - the "bounded
    /// window" AC-06 promises is honored via retry, not a silent partial revoke.
    /// </summary>
    Task DeleteAllForOwnerAsync(string ownerKey, CancellationToken ct = default);
}
