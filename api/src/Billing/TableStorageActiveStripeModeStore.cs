// ----------------------------------------------------------------------------
//  TableStorageActiveStripeModeStore - the Azure Table Storage store for the single
//  app-wide active Stripe mode (billing-entitlements/06). Mirrors the shape and
//  posture of TableStorageEntitlementGrantStore (the reference pattern):
//  Azure.Data.Tables (already a project dependency - NO new NuGet), a
//  CreateIfNotExists-once guard, and the same config-presence split (the "absent"
//  half is InMemoryActiveStripeModeStore, a working store - not a no-op).
//
//  SCHEMA: there is only ever ONE active-mode value for the whole app, so this is a
//  single fixed-key row (PartitionKey + RowKey both a constant). Stored properties:
//  Mode (the "test"/"live" wire value) and LastChangedUtc. No PII, no player / room
//  reference - this is an operator-only setting.
//
//  SAFE DEFAULT (AC-05): a missing table / missing row reads as Test, never Live.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The Azure Table Storage active-Stripe-mode store (billing-entitlements/06). Persists
/// the single active mode + last-changed time in one fixed-key row, so a read is a point
/// lookup and a flip is one upsert. A missing row/table resolves to Test (AC-05). Used
/// only when a storage connection string is configured (else InMemoryActiveStripeModeStore).
/// </summary>
public sealed class TableStorageActiveStripeModeStore : IActiveStripeModeStore
{
    /// <summary>The table name the active-mode row lands in (created on first write if absent).</summary>
    public const string TableName = "StripeMode";

    // A single fixed-key row - there is exactly one active-mode value for the whole app.
    private const string FixedPartitionKey = "mode";
    private const string FixedRowKey = "active";
    private const string ModeColumn = "Mode";
    private const string LastChangedColumn = "LastChangedUtc";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageActiveStripeModeStore> _logger;

    // Ensure-once guard (same rationale as the other Table stores): CreateIfNotExists is
    // a round-trip we only need on the FIRST write. A benign race is harmless.
    private volatile bool _tableEnsured;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration, NEVER
    /// a committed literal - see Program.cs). The table is created lazily on first write.
    /// </summary>
    public TableStorageActiveStripeModeStore(string connectionString, ILogger<TableStorageActiveStripeModeStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StripeModeState> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>(FixedPartitionKey, FixedRowKey, cancellationToken: ct);
            var entity = response.Value;
            // An unparseable / missing stored mode degrades to Test (AC-05) - never Live -
            // so schema drift can never silently arm real charges.
            var mode = StripeModeText.TryParse(entity.GetString(ModeColumn)) ?? StripeMode.Test;
            return new StripeModeState(mode, entity.GetDateTimeOffset(LastChangedColumn));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The row / table does not exist yet (mode never explicitly set) - the safe
            // default, not an error. Trace it (no PII) so a real storage misconfig is not
            // fully invisible.
            _logger.LogDebug(ex, "Active-mode read returned 404 (never set); defaulting to Test.");
            return new StripeModeState(StripeMode.Test, LastChangedUtc: null);
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(StripeMode mode, DateTimeOffset changedAtUtc, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        var entity = new TableEntity(FixedPartitionKey, FixedRowKey)
        {
            [ModeColumn] = mode.ToWire(),
            [LastChangedColumn] = changedAtUtc,
        };

        // UPSERT (Replace): there is only ever one active-mode row, so a flip overwrites it.
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    // Create the table ONCE (lazy); after the first success the guard skips the extra
    // round-trip on every subsequent write.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }
}
