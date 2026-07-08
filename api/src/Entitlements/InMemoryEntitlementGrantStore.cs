// ----------------------------------------------------------------------------
//  InMemoryEntitlementGrantStore - the WORKING fallback grant store used when NO
//  storage connection string is configured (billing-entitlements/01, local dev /
//  CI / a fresh clone).
//
//  DELIBERATELY NOT a no-op (same reasoning as accounts-identity/02's
//  InMemoryAccountStore): the session-creation gate, the tip jar (02), the gated
//  purchase flow (04), and the restore view (05) must all be exercisable end to end
//  on a laptop with zero Azure. So this actually stores, extends, and returns
//  grants - just in process memory instead of Azure Table Storage. The moment
//  Entitlements:StorageConnectionString is present, Program.cs registers
//  TableStorageEntitlementGrantStore instead and grants persist across restarts;
//  the semantics of BOTH stores are identical, only durability differs.
//
//  It is keyed by the stable AccountId (accounts-identity/05) - the SAME durable id
//  the account store mints and the Table grant store partitions by - and holds at
//  most ONE grant per (account, capability) so a renewal extends a lease rather than
//  piling up rows. Nested ConcurrentDictionaries make concurrent writes / reads safe
//  without an explicit lock, and give the "all of one account's grants" read as a
//  single inner-dictionary snapshot (the in-memory analog of a one-partition query).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// A thread-safe, in-memory <see cref="IEntitlementGrantStore"/> (billing-
/// entitlements/01, re-keyed by accounts-identity/05), registered when no storage
/// connection string is configured. Fully functional (per-account read + upsert-by-
/// capability write) so stories 03-05 and the session-creation gate are testable
/// with zero Azure setup - it just does not survive a process restart. Keyed by the
/// stable AccountId.
/// </summary>
public sealed class InMemoryEntitlementGrantStore : IEntitlementGrantStore
{
    // Outer key = the stable AccountId (a GUID, unguessable already - no hashing).
    // Inner key = the capability key, giving at most one lease per capability (a
    // renewal replaces it). The inner dictionary IS an account's whole "partition".
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, EntitlementGrant>> _byAccount =
        new();

    /// <inheritdoc />
    public Task<IReadOnlyList<EntitlementGrant>> GetGrantsAsync(Guid accountId, CancellationToken ct = default)
    {
        // A miss returns an empty list and NEVER creates a partition (TryGetValue
        // does not insert). Snapshot the values so the caller iterates a stable set.
        IReadOnlyList<EntitlementGrant> grants = _byAccount.TryGetValue(accountId, out var partition)
            ? partition.Values.ToArray()
            : [];
        return Task.FromResult(grants);
    }

    /// <inheritdoc />
    public Task PutGrantAsync(Guid accountId, EntitlementGrant grant, CancellationToken ct = default)
    {
        // GetOrAdd the account's partition, then upsert by capability key so a re-grant
        // of the same capability extends / replaces its lease (one row).
        var partition = _byAccount.GetOrAdd(accountId, _ => new ConcurrentDictionary<string, EntitlementGrant>(StringComparer.Ordinal));
        partition[grant.CapabilityKey] = grant;
        return Task.CompletedTask;
    }
}
