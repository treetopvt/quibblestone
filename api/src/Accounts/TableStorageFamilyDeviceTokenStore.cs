// ----------------------------------------------------------------------------
//  TableStorageFamilyDeviceTokenStore - the durable Azure Table Storage store for
//  linked family devices (accounts-identity/09, issue #229). Mirrors the shape and
//  posture of TableStorageAccountStore (the reference pattern): Azure.Data.Tables
//  (already a project dependency - NO new NuGet), a CreateIfNotExists-once guard, and
//  the same config-presence split (the "absent" half is InMemoryFamilyDeviceTokenStore
//  rather than a no-op, because the device-link flow needs a working local store).
//
//  SCHEMA (ADR 0003 Technical Notes): PartitionKey = the family AccountId, RowKey =
//  the DeviceTokenId - so listing a family's devices (AC-04) is a single-partition
//  query and resolving one device (AC-03) is a point read. Properties: a HASH of the
//  token value (AC-05 - never the raw secret), a short non-identifying label (AC-04),
//  created-at, last-used-at (AC-04's relative last-seen), the rolling ExpiresUtc, the
//  revoked flag, and IsAdultConfirmedDevice (AC-07, the adult-unlock signal). NO PII
//  beyond the AccountId it resolves to.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The Azure Table Storage linked-device store (accounts-identity/09). PartitionKey =
/// the family AccountId, RowKey = the DeviceTokenId, holding the token HASH (never the
/// raw secret, AC-05) + label + timestamps + rolling expiry + revoked + adult-confirm
/// flag. Used only when a storage connection string is configured (else
/// InMemoryFamilyDeviceTokenStore).
/// </summary>
public sealed class TableStorageFamilyDeviceTokenStore : IFamilyDeviceTokenStore
{
    /// <summary>The table name linked devices land in (created on first write if absent).</summary>
    public const string TableName = "FamilyDeviceTokens";

    private const string TokenHashColumn = "TokenHash";
    private const string LabelColumn = "Label";
    private const string CreatedUtcColumn = "CreatedUtc";
    private const string LastUsedUtcColumn = "LastUsedUtc";
    private const string ExpiresUtcColumn = "ExpiresUtc";
    private const string RevokedColumn = "Revoked";
    private const string AdultConfirmedColumn = "IsAdultConfirmedDevice";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageFamilyDeviceTokenStore> _logger;

    // Ensure-once guard (same rationale as TableStorageAccountStore).
    private volatile bool _tableEnsured;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration, NEVER
    /// a committed literal - see Program.cs). The table is created lazily on first write.
    /// </summary>
    public TableStorageFamilyDeviceTokenStore(string connectionString, ILogger<TableStorageFamilyDeviceTokenStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AddAsync(FamilyDeviceToken token, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);
        // A fresh DeviceTokenId is unique, so Add (not upsert) keeps "one row per device".
        await _table.AddEntityAsync(ToEntity(token), ct);
    }

    /// <inheritdoc />
    public async Task<FamilyDeviceToken?> GetAsync(Guid accountId, Guid deviceTokenId, CancellationToken ct = default)
    {
        var entity = await PointReadAsync(accountId.ToString(), deviceTokenId.ToString(), ct);
        return entity is null ? null : FromEntity(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FamilyDeviceToken>> ListByAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var results = new List<FamilyDeviceToken>();
        try
        {
            var partition = accountId.ToString();
            await foreach (var entity in _table.QueryAsync<TableEntity>(e => e.PartitionKey == partition, cancellationToken: ct))
            {
                results.Add(FromEntity(entity));
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table itself does not exist yet (no device ever linked) - a clean miss.
            _logger.LogDebug(ex, "Family-device table query returned 404 (table not yet created); treating as no devices.");
            return Array.Empty<FamilyDeviceToken>();
        }

        return results
            .OrderByDescending(row => row.CreatedUtc)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(FamilyDeviceToken token, CancellationToken ct = default)
    {
        try
        {
            // Unconditional replace of the whole row (rotate / touch / revoke / toggle).
            // ETag.All keeps this a simple last-write-wins update - the mutating endpoints
            // are all serialized behind the account holder's own credential, so there is
            // no contended concurrent-writer scenario to guard with an optimistic ETag.
            await _table.UpdateEntityAsync(ToEntity(token), ETag.All, TableUpdateMode.Replace, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 404)
        {
            // The row (or table) is gone - a clean no-op the caller maps to "nothing to update".
            _logger.LogDebug(ex, "Family-device row update found no existing row (404); treating as a no-op.");
            return false;
        }
    }

    // Point-read one device row, treating a table-not-yet-created 404 as an ordinary miss.
    private async Task<TableEntity?> PointReadAsync(string partitionKey, string rowKey, CancellationToken ct)
    {
        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
            return response.HasValue ? response.Value : null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug(ex, "Family-device table read returned 404 (table not yet created); treating as a miss.");
            return null;
        }
    }

    // Create the table ONCE (lazy); after the first success the guard skips the round-trip.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(ct);
            _tableEnsured = true;
        }
    }

    private static TableEntity ToEntity(FamilyDeviceToken token) =>
        new(token.AccountId.ToString(), token.DeviceTokenId.ToString())
        {
            [TokenHashColumn] = token.TokenHash,
            [LabelColumn] = token.Label,
            [CreatedUtcColumn] = token.CreatedUtc,
            [LastUsedUtcColumn] = token.LastUsedUtc,
            [ExpiresUtcColumn] = token.ExpiresUtc,
            [RevokedColumn] = token.Revoked,
            [AdultConfirmedColumn] = token.IsAdultConfirmedDevice,
        };

    // Rebuild the domain record from a stored entity. Defensive on the stored fields so a
    // partially-written / legacy row degrades to safe values (expired + revoked-ish) rather
    // than throwing - a malformed row must never resolve to unlocked content.
    private static FamilyDeviceToken FromEntity(TableEntity entity) =>
        new(
            AccountId: Guid.TryParse(entity.PartitionKey, out var accountId) ? accountId : Guid.Empty,
            DeviceTokenId: Guid.TryParse(entity.RowKey, out var deviceTokenId) ? deviceTokenId : Guid.Empty,
            TokenHash: entity.GetString(TokenHashColumn) ?? string.Empty,
            Label: entity.GetString(LabelColumn) ?? string.Empty,
            CreatedUtc: entity.GetDateTimeOffset(CreatedUtcColumn) ?? DateTimeOffset.UtcNow,
            LastUsedUtc: entity.GetDateTimeOffset(LastUsedUtcColumn),
            // A missing expiry reads as already-expired (DateTimeOffset.MinValue) so a
            // malformed row is dead rather than eternally live.
            ExpiresUtc: entity.GetDateTimeOffset(ExpiresUtcColumn) ?? DateTimeOffset.MinValue,
            // A missing adult-confirm flag reads as false - the SAFE default (AC-07).
            IsAdultConfirmedDevice: entity.GetBoolean(AdultConfirmedColumn) ?? false,
            // A missing revoked flag reads as false (a normal live row); expiry above is
            // the fail-safe for a wholly malformed row.
            Revoked: entity.GetBoolean(RevokedColumn) ?? false);
}
