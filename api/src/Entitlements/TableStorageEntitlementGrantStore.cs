// ----------------------------------------------------------------------------
//  TableStorageEntitlementGrantStore - the Azure Table Storage store for purchaser
//  entitlement grants (billing-entitlements/01, issue #70). Mirrors the shape and
//  posture of TableStorageAccountStore (the reference pattern): Azure.Data.Tables
//  (already a project dependency - NO new NuGet), a CreateIfNotExists-once guard,
//  and the same config-presence split (the "absent" half is
//  InMemoryEntitlementGrantStore rather than a no-op, because the gate + stories
//  03-05 need a working local store).
//
//  KEY / SCHEMA DESIGN (AC-05):
//    - PartitionKey = a SHA-256 HEX hash of the NORMALIZED purchaser email (the
//      SAME AccountIdentity.KeyFor scheme the account store uses), so ALL of a
//      purchaser's grants share ONE partition and the session-creation read is a
//      single partition query - never a scan, never cross-partition. The raw email
//      is never the key and the key is not guessable.
//    - RowKey = the capability key (e.g. "library.full", "pack.spooky"). One row
//      per (purchaser, capability), so a subscription renewal UPSERTS - extends the
//      lease in place - rather than piling up rows.
//    - Stored PROPERTIES: ValidThrough (nullable - null = a permanent one-time
//      pack) and Source (the GrantSource name). No PII, no player / room reference.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// The Azure Table Storage purchaser-grant store (billing-entitlements/01). Stores
/// one entity per (purchaser, capability), PartitionKey = SHA-256 hash of the
/// normalized purchaser email + RowKey = capability key, so a purchaser's whole
/// grant set is one partition query (AC-05), persisting only ValidThrough + Source.
/// Used only when a storage connection string is configured (else
/// InMemoryEntitlementGrantStore).
/// </summary>
public sealed class TableStorageEntitlementGrantStore : IEntitlementGrantStore
{
    /// <summary>The table name grants land in (created on first write if absent).</summary>
    public const string TableName = "EntitlementGrants";

    private const string ValidThroughColumn = "ValidThrough";
    private const string SourceColumn = "Source";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageEntitlementGrantStore> _logger;

    // Ensure-once guard (same rationale as TableStorageAccountStore): CreateIfNotExists
    // is a round-trip we only need on the FIRST write. A benign race is harmless.
    private volatile bool _tableEnsured;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration,
    /// NEVER a committed literal - see Program.cs). The table is created lazily on
    /// the first grant write.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (supplied per-environment).</param>
    /// <param name="logger">Logs storage failures server-side (never a purchaser secret).</param>
    public TableStorageEntitlementGrantStore(string connectionString, ILogger<TableStorageEntitlementGrantStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntitlementGrant>> GetGrantsAsync(string purchaserIdentity, CancellationToken ct = default)
    {
        var partition = AccountIdentity.KeyFor(purchaserIdentity);
        var grants = new List<EntitlementGrant>();
        try
        {
            // Single-partition query (AC-05): all of this purchaser's grants, no scan.
            var query = _table.QueryAsync<TableEntity>(e => e.PartitionKey == partition, cancellationToken: ct);
            await foreach (var entity in query)
            {
                grants.Add(FromEntity(entity));
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table does not exist yet (no grant has ever been written) - that is
            // simply an empty result, not an error. Trace it (no PII) so a real
            // storage misconfig is not fully invisible.
            _logger.LogDebug(ex, "Grant table read returned 404 (table not yet created); treating as no grants.");
        }
        return grants;
    }

    /// <inheritdoc />
    public async Task PutGrantAsync(string purchaserIdentity, EntitlementGrant grant, CancellationToken ct = default)
    {
        var partition = AccountIdentity.KeyFor(purchaserIdentity);
        await EnsureTableAsync(ct);

        var entity = new TableEntity(partition, grant.CapabilityKey)
        {
            // Only the lease + source (AC-05). No PII, no player / room reference.
            [ValidThroughColumn] = grant.ValidThrough,
            [SourceColumn] = grant.Source.ToString(),
        };

        // UPSERT (Replace): one row per capability, so a renewal extends the lease in
        // place rather than adding a duplicate (story 03's invoice.paid path).
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    // Create the table ONCE (lazy); after the first success the guard skips the
    // extra round-trip on every subsequent write.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }

    // Rebuild the domain record from a stored entity. Defensive on the stored fields
    // so a partially-written / legacy row degrades to sane values rather than throwing:
    // a missing/unparseable source falls back to OneTime (the most conservative -
    // permanent-shaped - source is never assumed; the lease still governs activeness).
    private static EntitlementGrant FromEntity(TableEntity entity)
    {
        var source = Enum.TryParse<GrantSource>(entity.GetString(SourceColumn), ignoreCase: true, out var parsed)
            ? parsed
            : GrantSource.OneTime;
        return new EntitlementGrant(
            CapabilityKey: entity.RowKey,
            ValidThrough: entity.GetDateTimeOffset(ValidThroughColumn),
            Source: source);
    }
}
