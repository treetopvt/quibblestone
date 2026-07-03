// ----------------------------------------------------------------------------
//  TableStorageCloudGalleryStore - the Azure Table Storage store for a purchaser's
//  cloud-synced keepsake gallery (keepsake-gallery/05, issue #154). Mirrors the
//  shape and posture of TableStoragePublishedTaleStore / TableStorageAccountStore
//  (the reference patterns): Azure.Data.Tables (already a project dependency - NO
//  new NuGet), a CreateIfNotExists-once guard, and the same connection-string-at-
//  startup split (the "absent" half here is the WORKING InMemoryCloudGalleryStore,
//  not a no-op).
//
//  DATASTORE DECISION (RESOLVED 2026-07-03, story 05): stay on Azure Table Storage,
//  no new Azure resource. A family's tale count stays small, so partition-scoped
//  scans are cheap and search / sort run client-side over one owner's bounded set.
//
//  KEY / SCHEMA DESIGN (AC-01/AC-05):
//    - PartitionKey = ownerKey (the SHA-256 hash of account.Email, AccountIdentity.
//      KeyFor), RowKey = taleId (a minted, unguessable id). So list-by-owner is a
//      SINGLE-partition query (all rows with PartitionKey = ownerKey), delete-one
//      is a point delete (partition + row), and delete-all is a single-partition
//      sweep. Owner isolation is structural: a query / delete only ever touches
//      one owner's partition.
//    - The ordered body parts serialize to ONE JSON string property (PartsJson),
//      exactly like the published-tale store - a tale is a short story, well under
//      Table Storage's 32KB string-property ceiling, and one blob keeps a row a
//      single entity with no per-part fan-out.
//    - Only anonymous, already-vetted fields land here (Title, PartsJson,
//      BylineNames, CreatedUtc). There is NO PII property - no email, no real name,
//      no room / session id (AC-05). The purchaser is identified ONLY by the opaque
//      owner partition key.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.CloudGallery;

/// <summary>
/// The Azure Table Storage cloud-gallery store (keepsake-gallery/05). Stores one
/// entity per synced tale, keyed PartitionKey = ownerKey (a hash of the purchaser
/// email) and RowKey = taleId, so list-by-owner is a single-partition query and
/// owners are isolated by partition (AC-01). Carries NO PII beyond the byline
/// nickname(s) (AC-05). Used only when a storage connection string is configured
/// (else InMemoryCloudGalleryStore is registered).
/// </summary>
public sealed class TableStorageCloudGalleryStore : ICloudGalleryStore
{
    /// <summary>The table name cloud-gallery tales land in (created on first write if absent).</summary>
    public const string TableName = "CloudGalleryTales";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageCloudGalleryStore> _logger;

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
    /// <param name="logger">Logs list / delete failures server-side (never any PII).</param>
    public TableStorageCloudGalleryStore(string connectionString, ILogger<TableStorageCloudGalleryStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAsync(CloudTale tale, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        var entity = new TableEntity(tale.OwnerKey, tale.TaleId)
        {
            // Anonymous, already-vetted fields only - no PII beyond the byline (AC-05).
            ["Title"] = tale.Title,
            ["PartsJson"] = JsonSerializer.Serialize(tale.Parts),
            ["BylineNames"] = tale.BylineNames,
            ["CreatedUtc"] = tale.CreatedUtc,
        };

        // Upsert so a re-sync of the same (owner, taleId) is idempotent rather than a
        // 409 - a fresh taleId is unique per save anyway, so this is defensive.
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudTale>> ListByOwnerAsync(string ownerKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return [];
        }

        var tales = new List<CloudTale>();
        try
        {
            // Single-partition query (PartitionKey = ownerKey): the whole access
            // pattern this story is built for. Owner isolation is structural - a
            // query only ever returns this owner's rows. The strongly-typed
            // predicate overload (not a raw OData string) is injection-proof by
            // construction, matching TableStorageEntitlementGrantStore's precedent.
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == ownerKey,
                cancellationToken: ct);
            await foreach (var entity in query)
            {
                tales.Add(FromEntity(entity));
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table itself does not exist yet (nothing has ever been saved) - an
            // ordinary empty gallery, not an error.
            _logger.LogDebug(ex, "Cloud-gallery list returned 404 (table not yet created); treating as an empty gallery.");
            return [];
        }
        catch (Exception ex)
        {
            // A storage blip on the signed-in gallery read degrades to an empty list
            // rather than a 500; logged server-side (no PII).
            _logger.LogWarning(ex, "Cloud-gallery list failed for an owner (served as empty).");
            return [];
        }

        return tales;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string ownerKey, string taleId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(taleId))
        {
            return;
        }

        await TryDeleteAsync(ownerKey, taleId, ct);
    }

    /// <inheritdoc />
    public async Task DeleteAllForOwnerAsync(string ownerKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        try
        {
            // A single-partition sweep (AC-06): enumerate the owner's rows and delete
            // each. Only the RowKey is needed to delete, so project to it. The
            // injection-proof predicate overload keeps owner isolation structural and
            // never touches another owner's partition.
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == ownerKey,
                select: ["RowKey"],
                cancellationToken: ct);
            await foreach (var entity in query)
            {
                // Per-row: an already-gone row (404) is an idempotent no-op, but any
                // OTHER delete failure PROPAGATES (see below) - a revoke must not
                // report success while rows remain (AC-06 "removed within a bounded
                // window"). The caller surfaces the failure so the client retries
                // until the gallery reads empty.
                await DeleteRowThrowingOnRealFailureAsync(ownerKey, entity.RowKey, ct);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No table yet - nothing to revoke, an idempotent no-op.
            _logger.LogDebug(ex, "Cloud-gallery revoke-all returned 404 (table not yet created); nothing to remove.");
        }
    }

    // Delete one (owner, tale) row for the SINGLE-delete path, swallowing a not-found
    // (idempotent, AC-06) and logging any other failure - never throws to the caller.
    // A single delete is fire-and-forget from the UI's view; the row it targeted is
    // gone or was never there, and the client is not mid-sweep.
    private async Task TryDeleteAsync(string ownerKey, string taleId, CancellationToken ct)
    {
        try
        {
            await _table.DeleteEntityAsync(ownerKey, taleId, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone - a delete / revoke is idempotent.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud-gallery delete failed for a tale (swallowed).");
        }
    }

    // Delete one row during a REVOKE-ALL sweep: an already-gone row (404) is an
    // idempotent no-op, but any OTHER failure PROPAGATES so the sweep cannot report
    // success while rows remain (AC-06). The controller then surfaces a non-2xx and
    // the client retries until List reads empty (a bounded window via retry, not a
    // silent partial revoke).
    private async Task DeleteRowThrowingOnRealFailureAsync(string ownerKey, string taleId, CancellationToken ct)
    {
        try
        {
            await _table.DeleteEntityAsync(ownerKey, taleId, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone - idempotent; keep sweeping the rest.
        }
    }

    // Create the table ONCE (lazy); after the first success the guard skips the
    // extra round-trip on every subsequent save.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(ct);
            _tableEnsured = true;
        }
    }

    // Rebuild the domain record from a stored entity. Defensive on the parts JSON:
    // a malformed / empty blob yields an empty body rather than throwing on a read.
    private static CloudTale FromEntity(TableEntity entity)
    {
        var partsJson = entity.GetString("PartsJson");
        IReadOnlyList<CloudTalePart> parts;
        try
        {
            parts = string.IsNullOrWhiteSpace(partsJson)
                ? []
                : JsonSerializer.Deserialize<List<CloudTalePart>>(partsJson) ?? [];
        }
        catch (JsonException)
        {
            parts = [];
        }

        return new CloudTale(
            OwnerKey: entity.PartitionKey,
            TaleId: entity.RowKey,
            Title: entity.GetString("Title") ?? string.Empty,
            Parts: parts,
            BylineNames: entity.GetString("BylineNames") ?? string.Empty,
            CreatedUtc: entity.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow);
    }
}
