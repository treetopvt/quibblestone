// ----------------------------------------------------------------------------
//  TableStorageSeatPresetStore - the Azure Table Storage store for kid seat presets
//  (accounts-identity/08, issue #228). Mirrors the shape and posture of
//  TableStorageAccountStore (the reference pattern): Azure.Data.Tables (already a
//  project dependency - NO new NuGet), a CreateIfNotExists-once guard, and the same
//  config-presence split (the "absent" half is InMemorySeatPresetStore, a working
//  store rather than a no-op, because the presets manager + join picker need a
//  working local store).
//
//  KEY / SCHEMA DESIGN (AC-01/AC-05, per the story's Technical Notes):
//    - PartitionKey = the owning family AccountId (the GUID as a string). Every
//      preset for one family shares a partition, so ListAsync is a single
//      partition query and every write is scoped to one account - an adult can
//      never reach another family's presets.
//    - RowKey = the stable preset id (a fresh GUID per preset).
//    - Properties: Nickname + Variant ONLY. There is NO history, gallery,
//      entitlement, login, or PII column, and NO room / player / nickname-of-a-
//      co-player reference (the kid-profile boundary, AC-03/AC-05). The nickname
//      stored here has ALREADY passed the same length cap + content-safety filter
//      a manual display name does (applied by AccountsController before this
//      store is called, AC-04/AC-07).
//    - A CreatedOrder Int64 column stamps insertion order so ListAsync returns a
//      stable creation order (Table Storage orders by RowKey otherwise, which is a
//      random GUID); it is a store-internal ordering detail, never part of the
//      SeatPreset domain record.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The Azure Table Storage kid-seat-preset store (accounts-identity/08). Stores one
/// entity per preset, PartitionKey = the owning family AccountId, RowKey = the stable
/// preset id, holding Nickname + Variant only (AC-01/AC-05). No room / player
/// reference (AC-03). Used only when a storage connection string is configured (else
/// InMemorySeatPresetStore).
/// </summary>
public sealed class TableStorageSeatPresetStore : ISeatPresetStore
{
    /// <summary>The table name presets land in (created on first write if absent).</summary>
    public const string TableName = "SeatPresets";

    private const string NicknameColumn = "Nickname";
    private const string VariantColumn = "Variant";
    // A store-internal insertion-order stamp so List returns a stable creation order
    // (Table Storage sorts by RowKey, a random GUID, otherwise). Never a domain field.
    private const string CreatedOrderColumn = "CreatedOrder";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageSeatPresetStore> _logger;

    // Ensure-once guard (same rationale as TableStorageAccountStore): CreateIfNotExists
    // is a round-trip only needed on the FIRST write; a benign race is harmless.
    private volatile bool _tableEnsured;

    // A monotonic sequence for the CreatedOrder stamp, SEEDED from the current unix-ms
    // time at construction so it keeps increasing ACROSS restarts (Copilot review): a
    // fresh process starts counting from "now", always greater than any stamp a prior
    // process wrote, so a preset created after a restart never sorts BEFORE an older
    // one. Interlocked.Increment past the seed keeps same-process creates ordered too.
    // (A plain process-local counter reset to 0 on each restart, and new rows would
    // wrongly sort ahead of pre-restart rows.)
    private long _sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration,
    /// NEVER a committed literal - see Program.cs). The connection is resolved once at
    /// startup; the table is created lazily on the first preset create.
    /// </summary>
    public TableStorageSeatPresetStore(string connectionString, ILogger<TableStorageSeatPresetStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SeatPreset>> ListAsync(Guid accountId, CancellationToken ct = default)
    {
        var partition = accountId.ToString();
        var results = new List<(long Order, SeatPreset Preset)>();
        try
        {
            // A single-partition query: every preset for this family. A not-yet-created
            // table (no preset ever written) surfaces as a 404 and lists as empty.
            await foreach (var entity in _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == partition, cancellationToken: ct))
            {
                if (TryFromEntity(entity, out var preset, out var order))
                {
                    results.Add((order, preset));
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug(ex, "Seat-preset table query returned 404 (table not yet created); treating as empty.");
            return [];
        }

        return results
            .OrderBy(item => item.Order)
            .Select(item => item.Preset)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SeatPreset> CreateAsync(Guid accountId, string nickname, string variant, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        var preset = new SeatPreset(Guid.NewGuid(), nickname, variant);
        var entity = new TableEntity(accountId.ToString(), preset.Id.ToString())
        {
            [NicknameColumn] = preset.Nickname,
            [VariantColumn] = preset.Variant,
            [CreatedOrderColumn] = Interlocked.Increment(ref _sequence),
        };
        // A fresh RowKey never conflicts; a UNIQUE insert (Add) keeps "one row per id".
        await _table.AddEntityAsync(entity, ct);
        return preset;
    }

    /// <inheritdoc />
    public async Task<SeatPreset?> UpdateAsync(Guid accountId, Guid presetId, string nickname, string variant, CancellationToken ct = default)
    {
        // Read the existing row FIRST, scoped to this account's partition: an id that
        // belongs to another family (or is stale) misses and maps to a 404 - never a
        // create. Preserve the original CreatedOrder so an edit keeps the preset's place.
        var existing = await PointReadAsync(accountId, presetId, ct);
        if (existing is null)
        {
            return null;
        }

        existing[NicknameColumn] = nickname;
        existing[VariantColumn] = variant;
        try
        {
            // Conditional update on the row's ETag: if a concurrent edit changed it
            // underneath us, retry once from a fresh read rather than clobber blindly.
            await _table.UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            _logger.LogDebug(ex, "Seat-preset update hit a concurrent-edit ETag mismatch; re-reading and retrying once.");
            var fresh = await PointReadAsync(accountId, presetId, ct);
            if (fresh is null)
            {
                return null;
            }
            fresh[NicknameColumn] = nickname;
            fresh[VariantColumn] = variant;
            await _table.UpdateEntityAsync(fresh, fresh.ETag, TableUpdateMode.Replace, ct);
        }

        return new SeatPreset(presetId, nickname, variant);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid accountId, Guid presetId, CancellationToken ct = default)
    {
        try
        {
            var response = await _table.DeleteEntityAsync(accountId.ToString(), presetId.ToString(), ETag.All, ct);
            // Table Storage returns 404 as a thrown RequestFailedException (caught
            // below), so reaching here means a row was actually removed.
            return !response.IsError;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone / never existed / a cross-account id: an idempotent no-op.
            _logger.LogDebug(ex, "Seat-preset delete found no matching row (already gone / cross-account); treating as a no-op.");
            return false;
        }
    }

    // Point-read one preset row under an account's partition, or null if absent (incl.
    // a not-yet-created table). Returns the raw TableEntity so Update can reuse its ETag.
    private async Task<TableEntity?> PointReadAsync(Guid accountId, Guid presetId, CancellationToken ct)
    {
        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(
                accountId.ToString(), presetId.ToString(), cancellationToken: ct);
            return response.HasValue ? response.Value : null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug(ex, "Seat-preset read returned 404 (table not yet created); treating as a miss.");
            return null;
        }
    }

    // Create the table ONCE (lazy); after the first success the guard skips the extra
    // round-trip on every subsequent create.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(ct);
            _tableEnsured = true;
        }
    }

    // Rebuild the domain record from a stored entity. Defensive on the fields so a
    // partially-written / legacy row degrades to sane values rather than throwing; a
    // row missing its RowKey-as-GUID is skipped (returns false).
    private static bool TryFromEntity(TableEntity entity, out SeatPreset preset, out long order)
    {
        preset = null!;
        order = 0;
        if (!Guid.TryParse(entity.RowKey, out var presetId))
        {
            return false;
        }
        preset = new SeatPreset(
            presetId,
            entity.GetString(NicknameColumn) ?? string.Empty,
            entity.GetString(VariantColumn) ?? SeatPresetRules.DefaultVariant);
        order = entity.GetInt64(CreatedOrderColumn) ?? 0;
        return true;
    }
}
