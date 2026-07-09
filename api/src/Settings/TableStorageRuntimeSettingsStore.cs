// ----------------------------------------------------------------------------
//  TableStorageRuntimeSettingsStore - the Azure Table Storage store for runtime
//  settings overrides (control-plane/01, issue #197). Mirrors the shape and posture of
//  TableStorageActiveStripeModeStore (the reference pattern) - generalized from that
//  store's SINGLE fixed-key row to ONE ROW PER KEY:
//    - Azure.Data.Tables (already a project dependency - NO new NuGet).
//    - PartitionKey = a fixed constant ("setting"), RowKey = the settings key itself,
//      so a read is a point lookup (same as the Stripe-mode row).
//    - A CreateIfNotExists-once guard on the first write.
//    - The same "404 -> no override, not an error" handling (AC-07): a missing row /
//      table means "no override", which the service resolves to the code default.
//
//  SCHEMA: one string Value column (the wire form - the catalog's declared type parses
//  it back, keeping this store type-agnostic), plus ChangedBy + ChangedAtUtc (AC-03).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The Azure Table Storage runtime-settings override store (control-plane/01). One row per
/// overridden key (RowKey = the key), so a read is a point lookup and a PUT is one upsert. A
/// missing row / table resolves to "no override" (the service then returns the code default,
/// AC-07). Used only when a storage connection string is configured (else
/// <see cref="InMemoryRuntimeSettingsStore"/>). Reuses the same storage account as the grant /
/// Stripe-mode stores - no new resource.
/// </summary>
public sealed class TableStorageRuntimeSettingsStore : IRuntimeSettingsStore
{
    /// <summary>The table name the override rows land in (created on first write if absent).</summary>
    public const string TableName = "RuntimeSettings";

    // A fixed partition - there are few settings keys and they are always read together for the
    // service cache, so one partition keeps GetAll a single partition scan and each key a point
    // lookup (RowKey = the key), exactly like the Stripe-mode single-row read.
    private const string FixedPartitionKey = "setting";
    private const string ValueColumn = "Value";
    private const string ChangedByColumn = "ChangedBy";
    private const string ChangedAtColumn = "ChangedAtUtc";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageRuntimeSettingsStore> _logger;

    // Ensure-once guard (same rationale as the other Table stores): CreateIfNotExists is a
    // round-trip we only need on the FIRST write. A benign race is harmless.
    private volatile bool _tableEnsured;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration, NEVER a
    /// committed literal - see Program.cs). The table is created lazily on first write.
    /// </summary>
    public TableStorageRuntimeSettingsStore(string connectionString, ILogger<TableStorageRuntimeSettingsStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingOverride>> GetAllOverridesAsync(CancellationToken ct = default)
    {
        var results = new List<SettingOverride>();
        try
        {
            // All override rows share the one partition, so this is a single-partition scan.
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == FixedPartitionKey,
                cancellationToken: ct);
            await foreach (var entity in query)
            {
                var projected = Project(entity);
                if (projected is not null)
                {
                    results.Add(projected);
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table does not exist yet (no override ever written) - the safe empty state,
            // not an error. Every key resolves to its code default (AC-01 / AC-07).
            _logger.LogDebug(ex, "Runtime-settings table read returned 404 (never written); no overrides.");
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<SettingOverride?> GetOverrideAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>(FixedPartitionKey, key, cancellationToken: ct);
            return Project(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No override for this key (row / table absent) - the safe default, not an error.
            // The service resolves it to the code default (AC-07). Trace it (no PII) so a real
            // storage misconfig is not fully invisible.
            _logger.LogDebug(ex, "Runtime-settings read for a key returned 404 (no override); defaulting.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetOverrideAsync(string key, string value, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        var entity = new TableEntity(FixedPartitionKey, key)
        {
            [ValueColumn] = value,
            [ChangedByColumn] = changedBy,
            [ChangedAtColumn] = changedAtUtc,
        };

        // UPSERT (Replace): a PUT overwrites the single override row for this key (AC-02).
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    /// <inheritdoc />
    public async Task DeleteOverrideAsync(string key, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default)
    {
        try
        {
            // Drop the row -> the key reverts to its code default (AC-04). ChangedBy /
            // ChangedAtUtc are part of the seam's symmetry (a future soft-delete could record
            // them); a hard delete does not persist them - the action log records the clear.
            await _table.DeleteEntityAsync(FixedPartitionKey, key, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Deleting a key with no override is a harmless no-op (nothing to clear).
            _logger.LogDebug(ex, "Runtime-settings delete for a key returned 404 (no override); no-op.");
        }
    }

    // Projects a stored row into a SettingOverride, or null when the required Value column is
    // missing (a drifted / hand-edited row) - degrading to "no override" (AC-07) rather than
    // throwing, the same posture as the Stripe-mode store's unparseable handling.
    private SettingOverride? Project(TableEntity entity)
    {
        var value = entity.GetString(ValueColumn);
        if (value is null)
        {
            _logger.LogWarning("Runtime-settings row {Key} has no Value column; treating as no override.", entity.RowKey);
            return null;
        }

        var changedBy = entity.GetString(ChangedByColumn) ?? string.Empty;
        var changedAt = entity.GetDateTimeOffset(ChangedAtColumn) ?? DateTimeOffset.MinValue;
        return new SettingOverride(entity.RowKey, value, changedBy, changedAt);
    }

    // Create the table ONCE (lazy); after the first success the guard skips the extra round-trip
    // on every subsequent write.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }
}
