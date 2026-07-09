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

    // Moderation companion state (sysadmin-console/03, issue #137) lives in the SAME
    // table as the tale bodies but under a DEDICATED sentinel PartitionKey, so:
    //   - the tale body row is (PartitionKey = RowKey = slug), a 12-char slug pair;
    //   - the moderation row is (PartitionKey = ModerationPartitionKey, RowKey = slug).
    // The two can NEVER collide (a 12-char slug never equals the sentinel), and
    // holding ALL moderation rows in one partition makes the operator review-queue
    // read a single-partition query rather than a table scan. This keeps the report
    // signal a TINY separate row - a report never rewrites the immutable tale body.
    private const string ModerationPartitionKey = "moderation";
    private const string ReportCountColumn = "ReportCount";
    private const string HiddenColumn = "Hidden";

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

    // Ensure the table exists ONCE (lazy); after the first success the guard skips
    // the extra round-trip on every subsequent call. A benign race is harmless (the
    // call is idempotent); a failed create leaves the flag false so the next call
    // retries. Shared by publish AND the moderation methods.
    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken);
            _tableEnsured = true;
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(PublishedTale tale, CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken);

        // A fresh slug is unique (unguessable, minted per publish), so Add is right;
        // a failure PROPAGATES so the caller never hands back a link to an unstored tale.
        await _table.AddEntityAsync(ToEntity(tale), cancellationToken);
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
            var now = DateTimeOffset.UtcNow;

            // Lazy expiry-on-read (AC-05): a tale at or past its expiry reads as GONE.
            // Opportunistically delete the stale row to reclaim it (best-effort - a
            // delete failure must not turn a clean 404 into a 500).
            if (tale.IsExpired(now))
            {
                await TryDeleteAsync(slug, cancellationToken);
                return null;
            }

            // Moderation-takedown soft-delete (keepsake-vault/04, AC-04): a taken-down
            // tale reads as GONE to every public / report / queue caller (this GetAsync
            // is their single read), exactly as when confirm-hidden HARD-deleted it -
            // but the body row is retained for RestoreFromTakedownAsync. Past its
            // restore window it is reclaimed lazily on read (AC-03), the same purge-on-
            // read idiom as expiry above.
            if (tale.IsTakenDown)
            {
                if (tale.IsRestoreWindowElapsed(now))
                {
                    await TryDeleteAsync(slug, cancellationToken);
                }
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

    // ---- Moderation (sysadmin-console/03, issue #137) ------------------------
    // The report signal is a TINY companion row (PK = ModerationPartitionKey, RK =
    // slug) carrying only a count + a Hidden flag - NEVER a reporter identity, IP,
    // player, room, or session (AC-06). It NEVER re-runs the content-safety filter
    // (AC-04): a report is a human signal, reviewed by an operator, not a second
    // automated content check. The tale body row is never touched by a report.

    /// <inheritdoc />
    public async Task<TaleModerationState?> ReportAsync(string slug, int autoHideThreshold, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        // Only an actually-serving tale can be reported: GetAsync applies expiry /
        // missing so a report against a gone slug is a no-op (nothing to moderate).
        var tale = await GetAsync(slug, cancellationToken);
        if (tale is null)
        {
            return null;
        }

        await EnsureTableAsync(cancellationToken);

        // Read-modify-write of the tiny companion row. A concurrent-report race could
        // drop a single increment - harmless at toy scale, and the per-IP report rate
        // limit (ReportTalesRateLimit) + the small threshold bound the volume anyway
        // (AC-05). We never rewrite the immutable tale body here.
        var existing = await ReadModerationAsync(slug, cancellationToken);
        var count = existing.ReportCount + 1;
        // Once hidden, stays hidden until an operator restores - a later report never
        // "un-hides" it. Auto-hide the moment the count reaches the threshold (AC-02).
        var hidden = existing.IsHidden || count >= autoHideThreshold;

        var row = new TableEntity(ModerationPartitionKey, slug)
        {
            [ReportCountColumn] = count,
            [HiddenColumn] = hidden,
        };
        await _table.UpsertEntityAsync(row, TableUpdateMode.Replace, cancellationToken);

        return new TaleModerationState(slug, count, hidden);
    }

    /// <inheritdoc />
    public async Task<TaleModerationState> GetModerationAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return TaleModerationState.None(slug);
        }

        await EnsureTableAsync(cancellationToken);
        return await ReadModerationAsync(slug, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportedTaleView>> ListHiddenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken);

        // Single-partition query over the moderation rows only (never a table scan of
        // the tale bodies), filtered to the currently-hidden ones (AC-03).
        var filter = $"PartitionKey eq '{ModerationPartitionKey}' and {HiddenColumn} eq true";
        var views = new List<ReportedTaleView>();

        try
        {
            await foreach (var row in _table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
            {
                var slug = row.RowKey;
                var count = row.GetInt32(ReportCountColumn) ?? 0;

                // Pair the count with the stored tale CONTENT (a point read of the
                // body row). If the body is gone (e.g. it expired underneath a stale
                // hidden row), skip it rather than surface an empty queue entry.
                var tale = await GetAsync(slug, cancellationToken);
                if (tale is not null)
                {
                    views.Add(new ReportedTaleView(tale, count));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reported-tale queue read failed (served as empty).");
        }

        // Most-reported first (a review nicety - not a contract).
        return views.OrderByDescending(v => v.ReportCount).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmHiddenAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        await EnsureTableAsync(cancellationToken);
        var state = await ReadModerationAsync(slug, cancellationToken);
        if (!state.IsHidden)
        {
            // Nothing hidden under this slug - idempotent no-op.
            return false;
        }

        // Confirm-hidden: SOFT-delete the tale body (keepsake-vault/04, AC-04) instead
        // of hard-deleting it. Stamp DeletedUtc on the body row so the slug stops
        // serving (GetAsync then reads it as GONE, exactly as the old hard delete did)
        // while the content is retained for RestoreFromTakedownAsync within the restore
        // window. The content fields are never touched (AC-05) - only the marker flips.
        var tale = await ReadTaleRawAsync(slug, cancellationToken);
        if (tale is not null && !tale.IsTakenDown)
        {
            await _table.UpsertEntityAsync(
                ToEntity(tale with { DeletedUtc = DateTimeOffset.UtcNow }),
                TableUpdateMode.Replace,
                cancellationToken);
        }

        // Drop the moderation row so the tale leaves the review queue; a later
        // restore then resumes serving with a reset report count (the same clean-slate
        // the un-hide RestoreAsync gives), so stale reports never immediately re-hide it.
        await TryDeleteModerationAsync(slug, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        await EnsureTableAsync(cancellationToken);
        var state = await ReadModerationAsync(slug, cancellationToken);
        if (!state.IsHidden)
        {
            return false;
        }

        // Restore: drop the moderation row entirely, which resumes serving at the
        // slug AND resets the report count to zero (the unreported default), so the
        // same reports do not immediately re-hide it (AC-03).
        await TryDeleteModerationAsync(slug, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreFromTakedownAsync(string slug, bool confirmedByOperator, CancellationToken cancellationToken = default)
    {
        // AC-07: the confirmation marker is REQUIRED at the signature (a caller cannot
        // reach this path without supplying it). This defensive backstop refuses an
        // un-confirmed call rather than silently un-deleting reported content.
        if (!confirmedByOperator || string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        await EnsureTableAsync(cancellationToken);

        // Read the body row RAW (ReadTaleRawAsync sees a taken-down row that GetAsync
        // hides), so a takedown can actually be undone.
        var tale = await ReadTaleRawAsync(slug, cancellationToken);
        if (tale is null || !tale.IsTakenDown)
        {
            // Unknown slug, or a tale that was not taken down - nothing to un-delete.
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        // Genuinely gone (TTL-expired or past the restore window): reclaim it and
        // refuse - un-deleting a lapsed takedown is out of scope (AC-03).
        if (tale.IsExpired(now) || tale.IsRestoreWindowElapsed(now))
        {
            await TryDeleteAsync(slug, cancellationToken);
            return false;
        }

        // Clear the takedown marker -> the tale resumes serving at its slug EXACTLY as
        // before, byte-for-byte, with no re-vet and no content mutation (AC-05/AC-06).
        await _table.UpsertEntityAsync(
            ToEntity(tale with { DeletedUtc = null }),
            TableUpdateMode.Replace,
            cancellationToken);
        return true;
    }

    // Point-read one tale body row REGARDLESS of its takedown / expiry state (used by
    // ConfirmHiddenAsync / RestoreFromTakedownAsync, which must see a taken-down row
    // that GetAsync deliberately hides). Returns null when the row - or the table -
    // does not exist, or on a storage blip (logged).
    private async Task<PublishedTale?> ReadTaleRawAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(slug, slug, cancellationToken: cancellationToken);
            return response.HasValue && response.Value is not null ? FromEntity(response.Value) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Published-tale raw read failed for a slug (treated as absent).");
            return null;
        }
    }

    // Read the tiny moderation companion row for a slug, or the unreported default
    // (count 0, not hidden) when there is no row / on a storage blip.
    private async Task<TaleModerationState> ReadModerationAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(
                ModerationPartitionKey, slug, cancellationToken: cancellationToken);
            if (!response.HasValue || response.Value is null)
            {
                return TaleModerationState.None(slug);
            }

            var count = response.Value.GetInt32(ReportCountColumn) ?? 0;
            var hidden = response.Value.GetBoolean(HiddenColumn) ?? false;
            return new TaleModerationState(slug, count, hidden);
        }
        catch (Exception ex)
        {
            // A storage blip on the serve path degrades to "not reported" (the tale
            // keeps serving) rather than surfacing an error; log it server-side.
            _logger.LogWarning(ex, "Moderation-state read failed for a slug (treated as unreported).");
            return TaleModerationState.None(slug);
        }
    }

    // Delete a slug's moderation companion row, swallowing not-found (idempotent) and
    // logging any other failure - never throws to the caller.
    private async Task TryDeleteModerationAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            await _table.DeleteEntityAsync(ModerationPartitionKey, slug, ETag.All, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone - idempotent.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Moderation-row delete failed for a slug (swallowed).");
        }
    }

    // Serialize a tale to its stored entity. Anonymous, already-vetted fields only -
    // the tale shape has no PII (AC-03). DeletedUtc is the moderation-takedown
    // soft-delete marker (keepsake-vault/04): written nullable, and explicitly cleared
    // (set null) on restore so a stale marker never survives a Replace upsert.
    private static TableEntity ToEntity(PublishedTale tale) => new(tale.Slug, tale.Slug)
    {
        ["Title"] = tale.Title,
        ["PartsJson"] = JsonSerializer.Serialize(tale.Parts),
        ["BylineNames"] = tale.BylineNames,
        ["CreatedUtc"] = tale.CreatedUtc,
        ["ExpiresUtc"] = tale.ExpiresUtc,
        ["DeletedUtc"] = tale.DeletedUtc,
    };

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
            ExpiresUtc: entity.GetDateTimeOffset("ExpiresUtc") ?? DateTimeOffset.UtcNow,
            // Absent column (a tale published before story 04, or a serving tale) -> null.
            DeletedUtc: entity.GetDateTimeOffset("DeletedUtc"));
    }
}
