// ----------------------------------------------------------------------------
//  TableStorageVaultStore - the Azure Table Storage store for the anonymous
//  server-side keepsake vault (keepsake-vault/01, issue #196). Mirrors the shape
//  and posture of TableStorageCloudGalleryStore (the reference pattern):
//  Azure.Data.Tables (already a project dependency - NO new NuGet), a
//  CreateIfNotExists-once guard, and the same connection-string-at-startup split
//  (the "absent" half here is the WORKING InMemoryVaultStore, not a no-op).
//
//  KEY / SCHEMA DESIGN (AC-02/AC-04):
//    - PartitionKey = vaultId (a device-held random handle, never PII),
//      RowKey = taleId (a minted, unguessable id). So list is a SINGLE-partition
//      query (all rows with PartitionKey = vaultId). Vault isolation is
//      structural: a query only ever touches one vault's partition.
//    - The ordered body parts serialize to ONE JSON string property (PartsJson),
//      exactly like the sibling stores - a tale is a short story, well under Table
//      Storage's 32KB string-property ceiling, and one blob keeps a row a single
//      entity with no per-part fan-out.
//    - Only anonymous, already-vetted fields land here (Title, PartsJson,
//      BylineNames, CreatedUtc). There is NO PII property - no email, no real
//      name, no room / session id (AC-04). The vault is identified ONLY by the
//      opaque random partition key.
//
//  TTL (AC-03), COMPUTED NOT STORED: there is NO ExpiresUtc column. This is
//  DELIBERATELY NOT a mirror of TableStoragePublishedTaleStore.GetAsync (which
//  stores ExpiresUtc and checks ONE row by slug - a single-link public-read shape,
//  not this feature's per-vault list). Expiry is computed as CreatedUtc + TtlDays
//  (VaultTale.IsExpired) WHILE enumerating the PartitionKey = vaultId query in
//  ListAsync: each expired row found is omitted from the result and best-effort
//  deleted to reclaim it. No stored expiry can be spoofed, and a TtlDays change
//  applies retroactively to every existing tale.
//
//  CAP (AC-07): SaveAsync counts the vault's partition before writing and rejects
//  a save that would exceed MaxTalesPerVault (no eviction - a durable archive).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Vault;

/// <summary>
/// The Azure Table Storage keepsake-vault store (keepsake-vault/01). Stores one
/// entity per saved tale, keyed PartitionKey = vaultId and RowKey = taleId, so a
/// list is a single-partition query and vaults are isolated by partition
/// (AC-02). Carries NO PII beyond the byline nickname(s) (AC-04). Enforces the
/// per-vault cap on save (AC-07) and the computed TTL on list (AC-03). Used only
/// when a storage connection string is configured (else InMemoryVaultStore).
/// </summary>
public sealed class TableStorageVaultStore : IVaultStore
{
    /// <summary>The table name vault tales land in (created on first write if absent).</summary>
    public const string TableName = "VaultTales";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageVaultStore> _logger;

    // Ensure-once guard (same rationale as the sibling Table stores): CreateIfNotExists
    // is a network round-trip we only need on the FIRST write. A benign race is harmless
    // (idempotent); a failed create leaves the flag false so the next write retries.
    private volatile bool _tableEnsured;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration,
    /// NEVER a committed literal - see Program.cs). The connection is resolved once
    /// at startup; the table is created lazily on the first save.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (supplied per-environment).</param>
    /// <param name="logger">Logs list / delete failures server-side (never any PII, never the vault id).</param>
    public TableStorageVaultStore(string connectionString, ILogger<TableStorageVaultStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<VaultSaveOutcome> SaveAsync(VaultTale tale, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        // AC-07: bound per-vault growth. Count the vault's partition first and
        // reject at or above the cap (no eviction - a durable archive). Counting
        // rows (RowKey only) before the write keeps this a cheap single-partition
        // scan at toy per-vault volume. The check-then-write race under concurrency
        // can only ever admit a handful of extra rows far below any storage
        // concern, which is acceptable for a coarse anti-bloat bound.
        if (await CountAsync(tale.VaultId, ct) >= IVaultStore.MaxTalesPerVault)
        {
            return VaultSaveOutcome.RejectedCapExceeded;
        }

        var entity = new TableEntity(tale.VaultId, tale.TaleId)
        {
            // Anonymous, already-vetted fields only - no PII beyond the byline (AC-04).
            // NOTE (AC-03): no ExpiresUtc column - expiry is computed from CreatedUtc
            // at read time, never stored.
            ["Title"] = tale.Title,
            ["PartsJson"] = JsonSerializer.Serialize(tale.Parts),
            ["BylineNames"] = tale.BylineNames,
            ["CreatedUtc"] = tale.CreatedUtc,
        };

        // Upsert so a re-save of the same (vaultId, taleId) is idempotent rather
        // than a 409 - a fresh taleId is unique per save anyway, so this is defensive.
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        return VaultSaveOutcome.Saved;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VaultTale>> ListAsync(string vaultId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultId))
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var tales = new List<VaultTale>();
        var expired = new List<string>();
        try
        {
            // Single-partition query (PartitionKey = vaultId): the whole access
            // pattern this feature is built for. Vault isolation is structural - a
            // query only ever returns this vault's rows. The strongly-typed
            // predicate overload (not a raw OData string) is injection-proof by
            // construction, matching the sibling stores' precedent.
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == vaultId,
                cancellationToken: ct);
            await foreach (var entity in query)
            {
                var tale = FromEntity(entity);
                // AC-03: apply the computed TTL as the partition is enumerated -
                // omit an expired row and mark it for a best-effort reclaim delete.
                if (tale.IsExpired(now))
                {
                    expired.Add(tale.TaleId);
                    continue;
                }
                tales.Add(tale);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table itself does not exist yet (nothing has ever been saved) - an
            // ordinary empty vault, not an error.
            _logger.LogDebug(ex, "Vault list returned 404 (table not yet created); treating as an empty vault.");
            return [];
        }
        // A genuine storage failure (non-404) PROPAGATES rather than degrading to an
        // empty list - the read endpoint then surfaces a non-2xx and the client can
        // retry, rather than silently reading as "no tales" on a transient fault.

        // Best-effort reclaim of the expired rows found above (AC-03). A failure to
        // delete never affects the read result - the rows are already omitted, and a
        // later list retries the reclaim.
        foreach (var taleId in expired)
        {
            await TryDeleteAsync(vaultId, taleId, ct);
        }

        return tales;
    }

    // Count the rows in one vault partition (RowKey only) for the per-vault cap
    // check (AC-07). A missing table (nothing ever saved) is zero rows.
    private async Task<int> CountAsync(string vaultId, CancellationToken ct)
    {
        var count = 0;
        try
        {
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == vaultId,
                select: ["RowKey"],
                cancellationToken: ct);
            await foreach (var _ in query)
            {
                count++;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return 0;
        }
        return count;
    }

    // Delete one (vault, tale) row for the expiry-reclaim path, swallowing a
    // not-found (idempotent) and logging any other failure - never throws to the
    // caller (the row is already omitted from the read result).
    private async Task TryDeleteAsync(string vaultId, string taleId, CancellationToken ct)
    {
        try
        {
            await _table.DeleteEntityAsync(vaultId, taleId, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone - the reclaim is idempotent.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vault expiry-reclaim delete failed for a tale (swallowed).");
        }
    }

    // Create the table ONCE (lazy); after the first success the guard skips the
    // extra round-trip on every subsequent save.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }

    // Rebuild the domain record from a stored entity. Defensive on the parts JSON:
    // a malformed / empty blob yields an empty body rather than throwing on a read.
    private static VaultTale FromEntity(TableEntity entity)
    {
        var partsJson = entity.GetString("PartsJson");
        IReadOnlyList<VaultTalePart> parts;
        try
        {
            parts = string.IsNullOrWhiteSpace(partsJson)
                ? []
                : JsonSerializer.Deserialize<List<VaultTalePart>>(partsJson) ?? [];
        }
        catch (JsonException)
        {
            parts = [];
        }

        return new VaultTale(
            VaultId: entity.PartitionKey,
            TaleId: entity.RowKey,
            Title: entity.GetString("Title") ?? string.Empty,
            Parts: parts,
            BylineNames: entity.GetString("BylineNames") ?? string.Empty,
            CreatedUtc: entity.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow);
    }
}
