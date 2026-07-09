// ----------------------------------------------------------------------------
//  InMemoryFamilyDeviceTokenStore - the WORKING fallback linked-device store used
//  when NO storage connection string is configured (accounts-identity/09, local dev /
//  CI / a fresh clone).
//
//  DELIBERATELY NOT a no-op (like InMemoryAccountStore, unlike the disabled published-
//  tale store): the redeem -> resolve -> list -> revoke -> adult-confirm flow must be
//  exercisable end-to-end on a laptop with zero Azure. The moment
//  Accounts:StorageConnectionString is present, Program.cs registers the Table Storage
//  store instead and rows survive restarts; the semantics of both stores are identical,
//  only the durability differs.
//
//  Holds ONLY the token HASH, never the raw secret (AC-05). Rows are keyed by
//  (AccountId, DeviceTokenId) to mirror the Table store's PartitionKey/RowKey shape,
//  so a by-id read is a point read and a list-by-account is a partition scan.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A thread-safe, in-memory <see cref="IFamilyDeviceTokenStore"/> (accounts-identity/09),
/// registered when no storage connection string is configured. Fully functional so the
/// device-link flow is testable with zero Azure setup; it just does not survive a
/// process restart. Keyed by (AccountId, DeviceTokenId), mirroring the Table store's
/// partition/row shape.
/// </summary>
public sealed class InMemoryFamilyDeviceTokenStore : IFamilyDeviceTokenStore
{
    // Keyed by (AccountId, DeviceTokenId) - the same addressing the Table store uses
    // (PartitionKey = AccountId, RowKey = DeviceTokenId). A ConcurrentDictionary keeps
    // reads/writes lock-free; the record is immutable, so an update swaps the whole value.
    private readonly ConcurrentDictionary<(Guid AccountId, Guid DeviceTokenId), FamilyDeviceToken> _rows = new();

    /// <inheritdoc />
    public Task AddAsync(FamilyDeviceToken token, CancellationToken ct = default)
    {
        _rows[(token.AccountId, token.DeviceTokenId)] = token;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<FamilyDeviceToken?> GetAsync(Guid accountId, Guid deviceTokenId, CancellationToken ct = default) =>
        Task.FromResult(_rows.TryGetValue((accountId, deviceTokenId), out var row) ? row : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<FamilyDeviceToken>> ListByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var devices = _rows.Values
            .Where(row => row.AccountId == accountId)
            .OrderByDescending(row => row.CreatedUtc)
            .ToArray();
        return Task.FromResult<IReadOnlyList<FamilyDeviceToken>>(devices);
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(FamilyDeviceToken token, CancellationToken ct = default)
    {
        var key = (token.AccountId, token.DeviceTokenId);
        // Only replace an existing row (never resurrect a deleted one); a missing row
        // is a clean no-op the caller maps to "nothing to update".
        if (!_rows.ContainsKey(key))
        {
            return Task.FromResult(false);
        }
        _rows[key] = token;
        return Task.FromResult(true);
    }
}
