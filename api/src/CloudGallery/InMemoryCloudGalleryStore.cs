// ----------------------------------------------------------------------------
//  InMemoryCloudGalleryStore - the WORKING fallback cloud-gallery store used when
//  NO storage connection string is configured (keepsake-gallery/05, local dev / CI
//  / a fresh clone).
//
//  This is DELIBERATELY NOT a no-op (unlike DisabledPublishedTaleStore, LIKE
//  InMemoryAccountStore). The published-tale public link can be switched fully off
//  because a missing tale is a harmless 404; but the purchaser cloud-sync flow
//  (save -> list -> delete -> revoke-all) must be exercisable END TO END on a
//  laptop with zero Azure, so this store actually saves, lists, and deletes tales -
//  just in process memory instead of Azure Table Storage. The moment
//  CloudGallery:StorageConnectionString is present (a deployed environment),
//  Program.cs registers TableStorageCloudGalleryStore instead and tales persist
//  across restarts; the semantics of BOTH stores are identical, only durability differs.
//
//  KEYING: tales are held in a nested map (ownerKey -> taleId -> tale), exactly the
//  PartitionKey (ownerKey) / RowKey (taleId) shape the Table store uses, so
//  list-by-owner is a single owner lookup and owner isolation is structural (an
//  owner can only ever see / delete rows under its own key). A ConcurrentDictionary
//  at both levels makes concurrent saves / deletes safe without an explicit lock.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.CloudGallery;

/// <summary>
/// A thread-safe, in-memory <see cref="ICloudGalleryStore"/> (keepsake-gallery/05),
/// registered when no storage connection string is configured. Fully functional
/// (save + list-by-owner + delete-one + delete-all) so the cloud-sync flow is
/// testable with zero Azure setup - it just does not survive a process restart.
/// Holds only the byline nickname(s) and already-filtered story (AC-05), keyed by
/// the opaque owner key so owners are isolated by construction.
/// </summary>
public sealed class InMemoryCloudGalleryStore : ICloudGalleryStore
{
    // ownerKey -> (taleId -> tale). The outer partition is the owner (mirrors the
    // Table PartitionKey), the inner key is the tale id (the RowKey), so an owner's
    // tales are a self-contained partition and a delete / list can never reach
    // across owners. Both levels are ConcurrentDictionary for lock-free safety.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CloudTale>> _byOwner =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public Task SaveAsync(CloudTale tale, CancellationToken ct = default)
    {
        var partition = _byOwner.GetOrAdd(tale.OwnerKey, _ => new ConcurrentDictionary<string, CloudTale>(StringComparer.Ordinal));
        partition[tale.TaleId] = tale;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CloudTale>> ListByOwnerAsync(string ownerKey, CancellationToken ct = default)
    {
        // A miss (owner with no tales) is an ordinary empty list, never an error.
        IReadOnlyList<CloudTale> tales = _byOwner.TryGetValue(ownerKey, out var partition)
            ? partition.Values.ToList()
            : [];
        return Task.FromResult(tales);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string ownerKey, string taleId, CancellationToken ct = default)
    {
        // Scoped to the owner partition, so a caller can only ever delete their own
        // tale. Idempotent - a missing owner / tale id is a no-op.
        if (_byOwner.TryGetValue(ownerKey, out var partition))
        {
            partition.TryRemove(taleId, out _);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAllForOwnerAsync(string ownerKey, CancellationToken ct = default)
    {
        // Drop the whole owner partition (AC-06). Idempotent - a missing owner is a
        // no-op. Never touches another owner's partition.
        _byOwner.TryRemove(ownerKey, out _);
        return Task.CompletedTask;
    }
}
