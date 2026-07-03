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
//  It is keyed by the SAME normalized-email SHA-256 hash the Table store and the
//  account store use (see AccountIdentity.KeyFor), and holds at most ONE grant per
//  (purchaser, capability) so a renewal extends a lease rather than piling up rows.
//  Nested ConcurrentDictionaries make concurrent purchases / reads safe without an
//  explicit lock, and give the "all of one purchaser's grants" read as a single
//  inner-dictionary snapshot (the in-memory analog of a one-partition query).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;
using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// A thread-safe, in-memory <see cref="IEntitlementGrantStore"/> (billing-
/// entitlements/01), registered when no storage connection string is configured.
/// Fully functional (per-purchaser read + upsert-by-capability write) so stories
/// 03-05 and the session-creation gate are testable with zero Azure setup - it just
/// does not survive a process restart. Keyed by the shared normalized-email hash.
/// </summary>
public sealed class InMemoryEntitlementGrantStore : IEntitlementGrantStore
{
    // Outer key = the purchaser-identity hash (AccountIdentity.KeyFor), so the raw
    // email is never a dictionary key and lookups are case / whitespace insensitive.
    // Inner key = the capability key, giving at most one lease per capability (a
    // renewal replaces it). The inner dictionary IS a purchaser's whole "partition".
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, EntitlementGrant>> _byPurchaser =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IReadOnlyList<EntitlementGrant>> GetGrantsAsync(string purchaserIdentity, CancellationToken ct = default)
    {
        var key = AccountIdentity.KeyFor(purchaserIdentity);
        // A miss returns an empty list and NEVER creates a partition (TryGetValue
        // does not insert). Snapshot the values so the caller iterates a stable set.
        IReadOnlyList<EntitlementGrant> grants = _byPurchaser.TryGetValue(key, out var partition)
            ? partition.Values.ToArray()
            : [];
        return Task.FromResult(grants);
    }

    /// <inheritdoc />
    public Task PutGrantAsync(string purchaserIdentity, EntitlementGrant grant, CancellationToken ct = default)
    {
        var key = AccountIdentity.KeyFor(purchaserIdentity);
        // GetOrAdd the purchaser's partition, then upsert by capability key so a
        // re-grant of the same capability extends / replaces its lease (one row).
        var partition = _byPurchaser.GetOrAdd(key, _ => new ConcurrentDictionary<string, EntitlementGrant>(StringComparer.Ordinal));
        partition[grant.CapabilityKey] = grant;
        return Task.CompletedTask;
    }
}
