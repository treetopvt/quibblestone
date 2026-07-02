// ----------------------------------------------------------------------------
//  TableStorageTelemetrySink - the Azure Table Storage serve-log +
//  feedback-vote sink (story-selection/04 AC-01/AC-06, story-selection/05
//  AC-02/AC-06).
//
//  Writes ONE tiny, PII-free entity per event into the provisioned Storage
//  account's Table service (Storage is the in-charter sink, README section 9,
//  CLAUDE.md section 10). Program.cs registers THIS sink only when a storage
//  connection string is configured; otherwise the NoOp sink is used and this
//  class is never constructed (AC-05).
//
//  TWO TABLES, ONE SINK:
//    - "StoryServes"   (story-selection/04): one row per "template served".
//    - "StoryFeedback" (story-selection/05): one row per thumbs up/down vote.
//  Both PartitionKey on TEMPLATE ID, so a like-rate-per-serve report is two cheap
//  per-template partition scans joined by id (AC-06) - never a cross-partition
//  query on either table.
//
//  KEY / SCHEMA DESIGN:
//    - StoryServes RowKey = INVERTED-TICKS + a short GUID. Inverted ticks (max
//      ticks minus the event's ticks) sort MOST-RECENT-FIRST within a partition,
//      so a per-template recent-window read is a cheap prefix range; the short
//      GUID suffix keeps two events in the same tick from colliding on the key.
//    - StoryFeedback RowKey = the VOTE ID ITSELF (an opaque GUID minted
//      client-side once per round's viewing of the reveal/recap screen). Writing
//      via UpsertEntity(..., TableUpdateMode.Replace) means a CHANGED vote (the
//      same VoteId, a different thumb) overwrites the SAME row rather than
//      appending a second one - last write wins, never double-counted (AC-02).
//    - The remaining anonymous fields (mode, length class, player count,
//      family-safe / vote, the opaque instance/session id, the timestamp) ride
//      as plain properties. There is NO PII property on either shape (AC-04).
//
//  FIRE-AND-FORGET / NEVER GATES GAMEPLAY (AC-03/AC-05): callers do not await
//  this on a round-start or vote-tap path, AND these methods NEVER throw for a
//  sink failure. A down / slow / misconfigured Table (bad connection string,
//  network blip, throttling) is caught, logged server-side, and swallowed - so a
//  fire-and-forget task can never become an unobserved exception and gameplay is
//  completely unaffected. CreateIfNotExists on first write is fine (a toy
//  footprint, no migration).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Telemetry;

/// <summary>
/// The Azure Table Storage serve-log + feedback-vote sink (story-selection/04,
/// story-selection/05). Writes one anonymous entity per <see cref="ServeEvent"/>
/// into the "StoryServes" table and one per <see cref="FeedbackEvent"/> into the
/// "StoryFeedback" table, both keyed for cheap per-template reads (AC-06).
/// Catches and SWALLOWS every write failure (logs server-side) so a
/// fire-and-forget caller is never faulted and gameplay is never gated
/// (AC-03/AC-05). Carries NO PII (AC-04).
/// </summary>
public sealed class TableStorageTelemetrySink : ITelemetrySink
{
    /// <summary>The table name the serve log lands in (created on first write if absent).</summary>
    public const string TableName = "StoryServes";

    /// <summary>The table name the feedback votes land in (created on first write if absent).</summary>
    public const string FeedbackTableName = "StoryFeedback";

    private readonly TableClient _table;
    private readonly TableClient _feedbackTable;
    private readonly ILogger<TableStorageTelemetrySink> _logger;

    // Ensure-once guards (story-selection review hardening): CreateIfNotExists is
    // a network round-trip (and a billable transaction) we only need on the FIRST
    // write to each table, not on every serve/feedback event. Once a create has
    // succeeded the flag short-circuits it, so the hot path is just the Add/Upsert.
    // A benign race (two concurrent first-writes both call CreateIfNotExists) is
    // harmless - the call is idempotent - and a failed create leaves the flag
    // false so the next write retries rather than being wedged forever.
    private volatile bool _tableEnsured;
    private volatile bool _feedbackTableEnsured;

    /// <summary>
    /// Constructs the sink over a storage connection string (from configuration /
    /// Key Vault, NEVER a committed literal - see Program.cs). The connection is
    /// resolved once at startup; each table is created lazily on its first write
    /// and then ensured only once (see the ensure-once guards below).
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (supplied per-environment).</param>
    /// <param name="logger">Logs swallowed write failures server-side (AC-03/AC-05).</param>
    public TableStorageTelemetrySink(string connectionString, ILogger<TableStorageTelemetrySink> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _feedbackTable = new TableClient(connectionString, FeedbackTableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordServeAsync(ServeEvent serveEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure the table exists ONCE (lazy); after the first success the
            // guard skips the extra round-trip on every subsequent write.
            if (!_tableEnsured)
            {
                await _table.CreateIfNotExistsAsync(cancellationToken);
                _tableEnsured = true;
            }

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

    /// <inheritdoc />
    public async Task RecordFeedbackAsync(FeedbackEvent feedbackEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure the feedback table exists ONCE (lazy); after the first success
            // the guard skips the extra round-trip on every subsequent vote write.
            if (!_feedbackTableEnsured)
            {
                await _feedbackTable.CreateIfNotExistsAsync(cancellationToken);
                _feedbackTableEnsured = true;
            }

            var entity = new TableEntity(feedbackEvent.TemplateId, BuildFeedbackRowKey(feedbackEvent.VoteId))
            {
                // Anonymous properties only - the FeedbackEvent shape has no PII (AC-04).
                ["TemplateId"] = feedbackEvent.TemplateId,
                ["Vote"] = feedbackEvent.Vote,
                ["TimestampUtc"] = feedbackEvent.TimestampUtc,
                ["Mode"] = feedbackEvent.Mode,
                ["SessionId"] = feedbackEvent.SessionId,
                ["VoteId"] = feedbackEvent.VoteId,
            };

            // Replace-mode UPSERT (AC-02): the RowKey is the VoteId, so a changed
            // vote (same player, same round, a different thumb) overwrites the SAME
            // row - last write wins, never a second, double-counted row.
            await _feedbackTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        }
        catch (Exception ex)
        {
            // AC-05: a sink failure must NEVER throw to the caller (the write is
            // fire-and-forget and must never gate a vote tap). Log it server-side so
            // an engineer can notice a broken sink, then swallow it. We log only the
            // anonymous template id - never anything about a player.
            _logger.LogWarning(
                ex,
                "Feedback-log write failed for template {TemplateId} (swallowed - telemetry never gates gameplay).",
                feedbackEvent.TemplateId);
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

    /// <summary>
    /// The feedback table's RowKey (AC-02, AC-06): the VoteId itself, so a re-vote
    /// (same VoteId, a different thumb) UPSERTS the same row rather than appending
    /// a new one. Falls back to a fresh GUID only for the defensive case of a
    /// missing/blank VoteId (the controller should never let that through, but a
    /// sink write must never throw on an unexpected empty key).
    /// </summary>
    private static string BuildFeedbackRowKey(string voteId) =>
        string.IsNullOrWhiteSpace(voteId) ? Guid.NewGuid().ToString("N") : voteId;
}
