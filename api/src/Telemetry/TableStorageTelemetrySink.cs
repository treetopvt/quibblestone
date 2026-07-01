// ----------------------------------------------------------------------------
//  TableStorageTelemetrySink - the Azure Table Storage serve-log sink
//  (story-selection/04, AC-01/AC-06).
//
//  Writes ONE tiny, PII-free entity per serve event into the provisioned Storage
//  account's Table service (Storage is the in-charter sink, README section 9,
//  CLAUDE.md section 10). Program.cs registers THIS sink only when a storage
//  connection string is configured; otherwise the NoOp sink is used and this
//  class is never constructed (AC-05).
//
//  KEY / SCHEMA DESIGN (AC-06 - "answer frequency questions cheaply"):
//    - PartitionKey = the TEMPLATE ID. So every serve of one template lands in the
//      same partition, and "how often was space-llama served?" is a single
//      partition scan, never a cross-partition query.
//    - RowKey = INVERTED-TICKS + a short GUID. Inverted ticks (max ticks minus the
//      event's ticks) sort MOST-RECENT-FIRST within a partition, so a per-template
//      recent-window read is a cheap prefix range; the short GUID suffix keeps two
//      events in the same tick from colliding on the key.
//    - The remaining anonymous fields (mode, length class, player count,
//      family-safe, the opaque instance id, the timestamp) ride as plain
//      properties. There is NO PII property - the ServeEvent shape itself has none
//      (AC-04).
//
//  FIRE-AND-FORGET / NEVER GATES GAMEPLAY (AC-03): callers do not await this on a
//  round-start path, AND this method NEVER throws for a sink failure. A down /
//  slow / misconfigured Table (bad connection string, network blip, throttling)
//  is caught, logged server-side, and swallowed - so a fire-and-forget task can
//  never become an unobserved exception and gameplay is completely unaffected.
//  CreateTableIfNotExists on first write is fine (a toy footprint, no migration).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Telemetry;

/// <summary>
/// The Azure Table Storage serve-log sink (story-selection/04). Writes one
/// anonymous entity per <see cref="ServeEvent"/> into the "StoryServes" table,
/// keyed for cheap per-template time-range reads (AC-06). Catches and SWALLOWS
/// every write failure (logs server-side) so a fire-and-forget caller is never
/// faulted and gameplay is never gated (AC-03). Carries NO PII (AC-04).
/// </summary>
public sealed class TableStorageTelemetrySink : ITelemetrySink
{
    /// <summary>The table name the serve log lands in (created on first write if absent).</summary>
    public const string TableName = "StoryServes";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageTelemetrySink> _logger;

    /// <summary>
    /// Constructs the sink over a storage connection string (from configuration /
    /// Key Vault, NEVER a committed literal - see Program.cs). The connection is
    /// resolved once at startup; the actual table is created lazily on the first
    /// successful write.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (supplied per-environment).</param>
    /// <param name="logger">Logs swallowed write failures server-side (AC-03).</param>
    public TableStorageTelemetrySink(string connectionString, ILogger<TableStorageTelemetrySink> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordServeAsync(ServeEvent serveEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            // Create-if-not-exists is fine for a toy footprint (no migration story).
            await _table.CreateIfNotExistsAsync(cancellationToken);

            var entity = new TableEntity(serveEvent.TemplateId, BuildRowKey(serveEvent.TimestampUtc))
            {
                // Anonymous properties only - the ServeEvent shape has no PII (AC-04).
                ["TemplateId"] = serveEvent.TemplateId,
                ["TimestampUtc"] = serveEvent.TimestampUtc,
                ["Mode"] = serveEvent.Mode,
                ["LengthClass"] = serveEvent.LengthClass,
                ["PlayerCount"] = serveEvent.PlayerCount,
                ["FamilySafe"] = serveEvent.FamilySafe,
                ["InstanceId"] = serveEvent.InstanceId,
            };

            await _table.AddEntityAsync(entity, cancellationToken);
        }
        catch (Exception ex)
        {
            // AC-03: a sink failure must NEVER throw to the caller (the write is
            // fire-and-forget and must never gate gameplay). Log it server-side so
            // an engineer can notice a broken sink, then swallow it. We log only the
            // anonymous template id - never anything about a player.
            _logger.LogWarning(
                ex,
                "Serve-log write failed for template {TemplateId} (swallowed - telemetry never gates gameplay).",
                serveEvent.TemplateId);
        }
    }

    /// <summary>
    /// Builds a RowKey that sorts MOST-RECENT-FIRST within a template's partition
    /// (AC-06): inverted ticks (max minus the event's UTC ticks, zero-padded to a
    /// fixed width so lexical order matches numeric order) plus a short GUID suffix
    /// so two serves in the same tick cannot collide.
    /// </summary>
    private static string BuildRowKey(DateTimeOffset timestampUtc)
    {
        var invertedTicks = DateTimeOffset.MaxValue.UtcTicks - timestampUtc.UtcTicks;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        return $"{invertedTicks:D19}-{shortGuid}";
    }
}
