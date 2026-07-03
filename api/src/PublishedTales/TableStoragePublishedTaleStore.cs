// ----------------------------------------------------------------------------
//  TableStoragePublishedTaleStore - the Azure Table Storage store for published
//  shareable tales (keepsake-gallery/04, AC-05). Mirrors the shape and posture of
//  TableStorageTelemetrySink (the reference pattern): Azure.Data.Tables (already
//  a project dependency - NO new NuGet), a CreateIfNotExists-once guard, and the
//  same connection-string-at-startup / NoOp-when-absent split (the "absent" half
//  is DisabledPublishedTaleStore).
//
//  KEY / SCHEMA DESIGN (AC-05):
//    - PartitionKey = RowKey = the slug. The read side is a public page hit that
//      only ever knows the slug, so a SINGLE-partition point read (GetEntity by
//      partition+row) is the whole access pattern - never a scan or a cross-
//      partition query. Slugs are unguessable (SlugGenerator), so partitioning on
//      them also spreads writes evenly.
//    - The ordered body parts serialize to ONE JSON string property (PartsJson).
//      A tale is a short story, so this is well under Table Storage's 32KB string-
//      property ceiling; storing it as one blob keeps the read a single entity
//      fetch with no per-part row fan-out.
//    - Only anonymous, already-vetted fields land here (Title, PartsJson,
//      BylineNames, CreatedUtc, ExpiresUtc). There is NO PII property - no IP, no
//      session id, no real name (AC-03).
//
//  PUBLISH vs READ posture:
//    - PublishAsync is NOT fire-and-forget: the caller needs to know the tale
//      actually landed before it hands back a link, so a write failure PROPAGATES
//      (the controller catches it and returns a clear "not available" rather than
//      a link to a tale that was never stored).
//    - GetAsync is the public read path: a MISSING slug is an ordinary null (the
//      page 404s), and expiry is applied lazily on read (an expired tale reads as
//      GONE, AC-05) with an opportunistic best-effort delete to reclaim the row.
//    - RevokeAsync is idempotent and low-ceremony (AC-07): deleting an unknown /
//      already-gone slug is a no-op, never an error.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.PublishedTales;

/// <summary>
/// The Azure Table Storage published-tale store (keepsake-gallery/04). Stores one
/// entity per published tale, keyed PartitionKey = RowKey = slug for a single-
/// lookup public read (AC-05). Carries NO PII (AC-03). Used only when a storage
/// connection string is configured (else DisabledPublishedTaleStore is registered).
/// </summary>
public sealed class TableStoragePublishedTaleStore : IPublishedTaleStore
{
    /// <summary>The table name published tales land in (created on first write if absent).</summary>
    public const string TableName = "PublishedTales";

    private readonly TableClient _table;
    private readonly ILogger<TableStoragePublishedTaleStore> _logger;

    // Ensure-once guard (same rationale as TableStorageTelemetrySink): CreateIfNotExists
    // is a network round-trip we only need on the FIRST write. A benign race is harmless
    // (the call is idempotent); a failed create leaves the flag false so the next write retries.
    private volatile bool _tableEnsured;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration,
    /// NEVER a committed literal - see Program.cs). The connection is resolved once
    /// at startup; the table is created lazily on the first publish.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (supplied per-environment).</param>
    /// <param name="logger">Logs read/revoke failures server-side.</param>
    public TableStoragePublishedTaleStore(string connectionString, ILogger<TableStoragePublishedTaleStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(PublishedTale tale, CancellationToken cancellationToken = default)
    {
        // Ensure the table exists ONCE (lazy); after the first success the guard
        // skips the extra round-trip on every subsequent publish.
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken);
            _tableEnsured = true;
        }

        var entity = new TableEntity(tale.Slug, tale.Slug)
        {
            // Anonymous, already-vetted fields only - the tale shape has no PII (AC-03).
            ["Title"] = tale.Title,
            ["PartsJson"] = JsonSerializer.Serialize(tale.Parts),
            ["BylineNames"] = tale.BylineNames,
            ["CreatedUtc"] = tale.CreatedUtc,
            ["ExpiresUtc"] = tale.ExpiresUtc,
        };

        // A fresh slug is unique (unguessable, minted per publish), so Add is right;
        // a failure PROPAGATES so the caller never hands back a link to an unstored tale.
        await _table.AddEntityAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PublishedTale?> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(slug, slug, cancellationToken: cancellationToken);
            if (!response.HasValue || response.Value is null)
            {
                return null;
            }

            var tale = FromEntity(response.Value);

            // Lazy expiry-on-read (AC-05): a tale at or past its expiry reads as GONE.
            // Opportunistically delete the stale row to reclaim it (best-effort - a
            // delete failure must not turn a clean 404 into a 500).
            if (tale.IsExpired(DateTimeOffset.UtcNow))
            {
                await TryDeleteAsync(slug, cancellationToken);
                return null;
            }

            return tale;
        }
        catch (Exception ex)
        {
            // A storage blip on a PUBLIC read should degrade to "not found" rather
            // than surface an error page to a visitor; log it server-side.
            _logger.LogWarning(ex, "Published-tale read failed for a slug (served as not-found).");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        await TryDeleteAsync(slug, cancellationToken);
    }

    // Delete a slug's row, swallowing a not-found (idempotent revoke, AC-07) and
    // logging any other failure - never throws to the caller.
    private async Task TryDeleteAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            await _table.DeleteEntityAsync(slug, slug, ETag.All, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone - a revoke / expiry-delete is idempotent.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Published-tale delete failed for a slug (swallowed).");
        }
    }

    // Rebuild the domain record from a stored entity. Defensive on the parts JSON:
    // a malformed / empty blob yields an empty body rather than throwing on a
    // public read.
    private static PublishedTale FromEntity(TableEntity entity)
    {
        var partsJson = entity.GetString("PartsJson");
        IReadOnlyList<TalePart> parts;
        try
        {
            parts = string.IsNullOrWhiteSpace(partsJson)
                ? []
                : JsonSerializer.Deserialize<List<TalePart>>(partsJson) ?? [];
        }
        catch (JsonException)
        {
            parts = [];
        }

        return new PublishedTale(
            Slug: entity.RowKey,
            Title: entity.GetString("Title") ?? string.Empty,
            Parts: parts,
            BylineNames: entity.GetString("BylineNames") ?? string.Empty,
            CreatedUtc: entity.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow,
            ExpiresUtc: entity.GetDateTimeOffset("ExpiresUtc") ?? DateTimeOffset.UtcNow);
    }
}
