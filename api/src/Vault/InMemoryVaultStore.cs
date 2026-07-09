// ----------------------------------------------------------------------------
//  InMemoryVaultStore - the WORKING fallback keepsake-vault store used when NO
//  storage connection string is configured (keepsake-vault/01, local dev / CI /
//  a fresh clone).
//
//  This is DELIBERATELY NOT a no-op (unlike DisabledPublishedTaleStore, LIKE
//  InMemoryCloudGalleryStore). The vault is default-on for EVERY anonymous player,
//  so the whole save -> list flow this and later stories build on must be
//  exercisable END TO END on a laptop with zero Azure. The moment
//  Vault:StorageConnectionString is present (a deployed environment), Program.cs
//  registers TableStorageVaultStore instead and tales persist across restarts; the
//  semantics of BOTH stores are identical (same cap, same computed TTL-on-list),
//  only durability differs.
//
//  KEYING: tales are held in a nested map (vaultId -> taleId -> tale), exactly the
//  PartitionKey (vaultId) / RowKey (taleId) shape the Table store uses, so a list
//  is a single vault lookup and vault isolation is structural (a caller can only
//  ever see rows under the vault id it presents). A ConcurrentDictionary at both
//  levels makes concurrent saves safe without an explicit lock.
//
//  TTL (AC-03) and the per-vault CAP (AC-07) are enforced here IDENTICALLY to the
//  Table store, so local dev / CI exercises the same bounds: SaveAsync rejects a
//  save that would exceed MaxTalesPerVault, and ListAsync omits + best-effort
//  removes tales past their computed CreatedUtc + TtlDays expiry.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Vault;

/// <summary>
/// A thread-safe, in-memory <see cref="IVaultStore"/> (keepsake-vault/01),
/// registered when no storage connection string is configured. Fully functional
/// (save with the per-vault cap + list with computed TTL expiry) so the vault flow
/// is testable with zero Azure setup - it just does not survive a process restart.
/// Holds only the byline nickname(s) and already-filtered story (AC-04), keyed by
/// the opaque random vault id so vaults are isolated by construction.
/// </summary>
public sealed class InMemoryVaultStore : IVaultStore
{
    // vaultId -> (taleId -> tale). The outer partition is the vault (mirrors the
    // Table PartitionKey), the inner key is the tale id (the RowKey), so a vault's
    // tales are a self-contained partition and a list can never reach across
    // vaults. Both levels are ConcurrentDictionary for lock-free safety.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, VaultTale>> _byVault =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public Task<VaultSaveOutcome> SaveAsync(VaultTale tale, CancellationToken ct = default)
    {
        var partition = _byVault.GetOrAdd(tale.VaultId, _ => new ConcurrentDictionary<string, VaultTale>(StringComparer.Ordinal));

        // AC-07: reject a save that would push the vault past the cap. A fresh
        // taleId is not yet present, so Count is the current stored total; at or
        // above the cap, refuse without storing (no eviction - a durable archive).
        // The tiny check-then-add race under concurrency can only ever admit a
        // handful of extra rows far below any storage concern, so an explicit lock
        // is not warranted for a family toy (AC-07's intent is a coarse bound).
        if (partition.Count >= IVaultStore.MaxTalesPerVault)
        {
            return Task.FromResult(VaultSaveOutcome.RejectedCapExceeded);
        }

        partition[tale.TaleId] = tale;
        return Task.FromResult(VaultSaveOutcome.Saved);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VaultTale>> ListAsync(string vaultId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultId) || !_byVault.TryGetValue(vaultId, out var partition))
        {
            // A miss (vault with no tales) is an ordinary empty list, never an error.
            return Task.FromResult<IReadOnlyList<VaultTale>>([]);
        }

        // AC-03: apply the computed TTL while enumerating the partition - omit any
        // expired row from the result and best-effort remove it to reclaim it (no
        // stored ExpiresUtc; expiry is CreatedUtc + TtlDays computed at read time).
        var now = DateTimeOffset.UtcNow;
        var live = new List<VaultTale>();
        foreach (var (taleId, tale) in partition)
        {
            if (tale.IsExpired(now))
            {
                partition.TryRemove(taleId, out _);
                continue;
            }
            live.Add(tale);
        }

        return Task.FromResult<IReadOnlyList<VaultTale>>(live);
    }
}
