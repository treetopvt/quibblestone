// ----------------------------------------------------------------------------
//  TableStorageOperatorActionLog - the DURABLE operator action-log store that lights up
//  the IOperatorActionLog seam (sysadmin-console/06, issue #233). Mirrors the shape and
//  posture of TableStorageActiveStripeModeStore / TableStorageEntitlementGrantStore (the
//  reference pattern): Azure.Data.Tables (already a project dependency - NO new NuGet), a
//  CreateIfNotExists-once guard, and the SAME config-presence split (the "absent" half is
//  InMemoryOperatorActionLog, a working store - not a no-op).
//
//  NEWEST-FIRST WITHOUT A CLIENT SORT (AC-03): every row lives in ONE fixed partition, and
//  its RowKey is the INVERTED tick count (DateTimeOffset.MaxValue.Ticks - timestamp.Ticks),
//  zero-padded to 19 digits, plus a short random disambiguator so two appends in the same
//  tick cannot collide. Azure Table Storage returns rows sorted by (PartitionKey, RowKey)
//  ASCENDING, so a plain bounded top-N query already yields newest-first - no full-partition
//  scan-and-sort in the controller.
//
//  TRUSTWORTHY DISPUTE INSURANCE (ADR 0003 "Security posture"):
//    - LOG-BEFORE-ACT (AC-01a): the money / moderation call sites AppendAsync BEFORE their
//      effect. AppendAsync validates the target FIRST (AC-07) - an invalid target throws
//      before any write, aborting the action rather than persisting a bad row.
//    - RETENTION FLOOR (AC-04): PruneAsync deletes only rows OLDER than
//      OperatorActionLogPolicy.ClampRetentionDays(...) - a value clamped UP to the hard floor,
//      so no runtime setting can evict a row still within the floor (config- or volume-evict).
//
//  ANONYMITY (AC-06): a row stores ONLY operator email + action + target + note + timestamp.
//  There is no column for, and no code path that writes, a player / room / session reference.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Globalization;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The Azure Table Storage operator action-log store (sysadmin-console/06). Appends one row per
/// effectful operator action into a single fixed partition keyed by an inverted-ticks RowKey, so a
/// bounded ascending read is already newest-first (AC-03). Validates the target on write (AC-07) and
/// prunes only rows past the clamped retention floor (AC-04). Used only when a storage connection
/// string is configured (else InMemoryOperatorActionLog stands in).
/// </summary>
public sealed class TableStorageOperatorActionLog : IOperatorActionLog
{
    /// <summary>The table name the rows land in (created on first write if absent).</summary>
    public const string TableName = "OperatorActionLog";

    // Every row shares ONE partition - the operator log is low-volume, and a single partition
    // is what makes an ascending top-N read return the global newest-first order (AC-03).
    private const string FixedPartitionKey = "op";
    private const string OperatorEmailColumn = "OperatorEmail";
    private const string ActionColumn = "Action";
    private const string TargetColumn = "Target";
    private const string NoteColumn = "Note";
    private const string TimestampColumn = "TimestampUtc";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageOperatorActionLog> _logger;

    // Ensure-once guard (same rationale as the sibling Table stores): CreateIfNotExists is a
    // round-trip only needed on the FIRST write. A benign race is harmless.
    private volatile bool _tableEnsured;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration, NEVER a committed
    /// literal - see Program.cs). The table is created lazily on first write.
    /// </summary>
    public TableStorageOperatorActionLog(string connectionString, ILogger<TableStorageOperatorActionLog> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AppendAsync(string operatorEmail, string action, string target, string note, CancellationToken ct = default)
    {
        // Validate BEFORE any write. Because the money / moderation call sites append BEFORE their
        // effect (log-before-act, AC-01a), a throw here aborts the action - nothing is persisted and
        // the effect never commits. (1) The ACTOR must be present: a row that does not identify who
        // acted is a useless trail (the dispute-insurance contract).
        if (!OperatorActionLogPolicy.IsValidOperatorIdentity(operatorEmail))
        {
            throw new ArgumentException(
                "An operator identity is required to append an action-log row.", nameof(operatorEmail));
        }
        // (2) The TARGET must be valid (AC-07): a malformed / markup-bearing email target is refused.
        if (!OperatorActionLogPolicy.IsValidTarget(target))
        {
            throw new InvalidOperatorActionTargetException($"'{target}' is not a valid action-log target.");
        }

        await EnsureTableAsync(ct);

        var timestamp = DateTimeOffset.UtcNow;
        var entity = new TableEntity(FixedPartitionKey, BuildRowKey(timestamp))
        {
            [OperatorEmailColumn] = operatorEmail,
            [ActionColumn] = action,
            [TargetColumn] = target,
            [NoteColumn] = note,
            [TimestampColumn] = timestamp,
        };

        // Append-only: a fresh, unique RowKey each time, so Add (never Upsert) - a collision would
        // be a genuine error worth surfacing, not a silent overwrite of an existing row.
        await _table.AddEntityAsync(entity, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperatorActionLogEntry>> ListRecentAsync(int maxItems, CancellationToken ct = default)
    {
        var cap = Math.Max(0, maxItems);
        var rows = new List<OperatorActionLogEntry>(cap);
        if (cap == 0)
        {
            return rows;
        }

        try
        {
            // Ascending (PartitionKey, RowKey) is the Table Storage default, and the inverted-ticks
            // RowKey makes that newest-first (AC-03). MaxPerPage caps the transfer; we stop at `cap`.
            // A string filter (CreateQueryFilter formats / escapes the value) matches the codebase's
            // other Table stores rather than relying on expression-to-OData translation.
            var query = _table.QueryAsync<TableEntity>(
                filter: TableClient.CreateQueryFilter($"PartitionKey eq {FixedPartitionKey}"),
                maxPerPage: cap,
                cancellationToken: ct);

            await foreach (var entity in query)
            {
                rows.Add(ToEntry(entity));
                if (rows.Count >= cap)
                {
                    break;
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No table yet (nothing ever appended) - an empty log, not an error.
            _logger.LogDebug(ex, "Action-log list found no table yet; returning an empty page.");
        }

        return rows;
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(int? configuredRetentionDays, CancellationToken ct = default)
    {
        // The horizon is clamped UP to the hard floor (AC-04): a null / below-floor / hostile value
        // can never shorten retention, so a row still within the floor is never eligible for prune.
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(OperatorActionLogPolicy.ClampRetentionDays(configuredRetentionDays));
        var removed = 0;

        try
        {
            // A string filter over the partition + the stored timestamp column (CreateQueryFilter
            // formats the DateTimeOffset as the OData datetime literal) - the codebase's Table-store
            // idiom, not an expression the SDK would have to translate for a dynamic column.
            // NOTE: the column name (TimestampUtc) and operators are LITERAL text; only the partition
            // value and the cutoff are interpolated, so CreateQueryFilter escapes / datetime-formats
            // exactly those two (interpolating the column name would wrongly quote it as a value).
            var filter = TableClient.CreateQueryFilter(
                $"PartitionKey eq {FixedPartitionKey} and TimestampUtc lt {cutoff}");
            var stale = _table.QueryAsync<TableEntity>(filter, cancellationToken: ct);

            await foreach (var entity in stale)
            {
                await _table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, ETag.All, ct);
                removed++;
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No table / row to prune - nothing to do.
            _logger.LogDebug(ex, "Action-log prune found no table yet; nothing to remove.");
        }

        return removed;
    }

    /// <summary>
    /// Builds the inverted-ticks RowKey for <paramref name="timestamp"/>: the newest row sorts
    /// FIRST under an ascending query (AC-03). A short random suffix disambiguates two appends that
    /// land in the same tick, so neither is lost to a RowKey collision.
    /// </summary>
    private static string BuildRowKey(DateTimeOffset timestamp)
    {
        var inverted = (DateTimeOffset.MaxValue.Ticks - timestamp.Ticks).ToString("d19", CultureInfo.InvariantCulture);
        var disambiguator = Guid.NewGuid().ToString("N").Substring(0, 8);
        return $"{inverted}-{disambiguator}";
    }

    /// <summary>Projects a stored row to the seam's row record. A missing timestamp degrades to now (never throws).</summary>
    private static OperatorActionLogEntry ToEntry(TableEntity entity) => new(
        entity.GetString(OperatorEmailColumn) ?? string.Empty,
        entity.GetString(ActionColumn) ?? string.Empty,
        entity.GetString(TargetColumn) ?? string.Empty,
        entity.GetString(NoteColumn) ?? string.Empty,
        entity.GetDateTimeOffset(TimestampColumn) ?? DateTimeOffset.UtcNow);

    // Create the table ONCE (lazy); after the first success the guard skips the extra round-trip.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }
}
