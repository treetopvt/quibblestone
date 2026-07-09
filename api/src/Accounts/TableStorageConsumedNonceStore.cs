// ----------------------------------------------------------------------------
//  TableStorageConsumedNonceStore - the DURABLE, SHARED single-use nonce set backing
//  MagicLinkTokenService in a deployed environment (platform-devops/08 AC-07).
//  Mirrors the posture of the other TableStorage*Store classes here: Azure.Data.Tables
//  (no new NuGet), a CreateIfNotExists-once guard, reusing the ALREADY-provisioned
//  Storage Account (a new table, not a new resource type - see infra/main.bicep's
//  ConsumedMagicLinkNonces table).
//
//  WHY DURABLE + SHARED (the gap it closes): once the signing key is durable and
//  shared across instances, a per-process consumed-nonce set lets a single-use token
//  be replayed once per OTHER instance behind the load balancer. Persisting the set
//  to Table Storage - which every instance reads and writes - makes "consumed on
//  instance A" visible to instance B, so a magic link is single-use FLEET-wide for
//  both purchaser sign-in and operator login.
//
//  ATOMICITY: TryConsumeAsync is a single AddEntity - Table Storage rejects a second
//  insert of the same (PartitionKey, RowKey) with 409 Conflict. That server-side
//  conflict IS the atomic "record-if-new" check across all instances (no read-then-
//  write race), which is exactly the single-use guarantee this store must give.
//
//  SCHEMA: one entity per consumed nonce, PartitionKey = a constant bucket, RowKey =
//  the nonce (a random hex jti, globally unique - so a point insert by nonce is the
//  whole access pattern). The token's expiry is stored as ExpiresAt so a periodic
//  sweep can delete rows that can never be replayed again (they are past expiry),
//  the same pruning intent the in-memory set has. NO subject, NO email, NO room /
//  player / session field is ever written here (AC-05) - only the opaque nonce and
//  its expiry.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The Azure Table Storage <see cref="IConsumedNonceStore"/> (platform-devops/08).
/// One entity per consumed magic-link nonce (PartitionKey = fixed bucket, RowKey =
/// nonce, plus an ExpiresAt property) so single-use is enforced across every
/// instance behind the load balancer. Used only when a storage connection string is
/// configured (else InMemoryConsumedNonceStore).
/// </summary>
public sealed class TableStorageConsumedNonceStore : IConsumedNonceStore
{
    /// <summary>The table name consumed-nonce markers land in.</summary>
    public const string TableName = "ConsumedMagicLinkNonces";

    // All markers share one partition - the set is small (bounded by the short token
    // lifetime) and every access is a point insert by the globally-unique nonce.
    private const string Partition = "nonce";

    // The stored property carrying the token's expiry, used by the opportunistic
    // prune to drop rows that can never be replayed again.
    private const string ExpiresAtColumn = "ExpiresAt";

    // Run an opportunistic expired-row sweep at most this often, so pruning does not
    // add a query to every single consume (it just needs to keep the table from
    // growing unbounded over a long-lived deployment).
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(30);

    private readonly TableClient _table;
    private readonly ILogger<TableStorageConsumedNonceStore> _logger;
    private volatile bool _tableEnsured;
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;
    private int _pruneRunning;

    /// <summary>Constructs the store over a storage connection string (from configuration, never a committed literal).</summary>
    public TableStorageConsumedNonceStore(string connectionString, ILogger<TableStorageConsumedNonceStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(string nonce, DateTimeOffset expiry, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        var entity = new TableEntity(Partition, nonce)
        {
            [ExpiresAtColumn] = expiry,
        };

        try
        {
            // AddEntity (NOT Upsert): a second insert of the same nonce fails with 409
            // Conflict server-side. That conflict is the atomic, cross-instance replay
            // check - if it succeeds this is the token's first use.
            await _table.AddEntityAsync(entity, ct);
            // Prune against the REAL clock (UtcNow), NEVER the token's future expiry: the
            // sweep deletes rows whose ExpiresAt is in the past, so a future cutoff would
            // delete markers for tokens that have NOT yet expired - reopening the very
            // single-use replay AC-07 closes. UtcNow deletes only truly-expired rows.
            PruneExpiredIfDue(DateTimeOffset.UtcNow);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // The nonce is already present - this token has been consumed before
            // (a replay). Reject it. The exception carries only the opaque nonce as a
            // row key, never any subject / PII.
            return false;
        }
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }

    // Opportunistic, non-blocking prune of rows whose expiry has passed: the token
    // itself is expiry-checked BEFORE its nonce is consumed, so an expired row can
    // never gate a live token again - it is pure housekeeping to keep the table
    // small. Runs at most once per PruneInterval, on a background task so it never
    // slows a sign-in, and guards against overlapping runs with a simple flag.
    private void PruneExpiredIfDue(DateTimeOffset now)
    {
        if (now - _lastPrune < PruneInterval)
        {
            return;
        }
        if (Interlocked.Exchange(ref _pruneRunning, 1) == 1)
        {
            return;
        }
        _lastPrune = now;
        _ = Task.Run(() => PruneExpiredAsync(now));
    }

    private async Task PruneExpiredAsync(DateTimeOffset olderThan)
    {
        try
        {
            // Filter server-side with an OData STRING, not a LINQ predicate: the
            // expression translator cannot render the client-side TableEntity helper
            // (GetDateTimeOffset) into OData, so a predicate would throw at runtime and
            // the table would grow unbounded. CreateQueryFilter escapes each
            // interpolation hole as a typed VALUE (Partition -> a quoted string,
            // olderThan -> an OData datetime literal), so the column NAME "ExpiresAt"
            // must stay LITERAL text in the format string - it mirrors ExpiresAtColumn.
            var filter = TableClient.CreateQueryFilter(
                $"PartitionKey eq {Partition} and ExpiresAt lt {olderThan}");
            var expired = _table.QueryAsync<TableEntity>(filter);
            await foreach (var row in expired)
            {
                await _table.DeleteEntityAsync(row.PartitionKey, row.RowKey, ETag.All);
            }
        }
        catch (RequestFailedException ex)
        {
            // Pruning is best-effort housekeeping - a transient storage error here is
            // harmless (expired rows are never replayable) and must not surface. Log
            // at debug with no nonce / PII in the message.
            _logger.LogDebug(ex, "Opportunistic consumed-nonce prune failed; will retry on the next interval.");
        }
        finally
        {
            Interlocked.Exchange(ref _pruneRunning, 0);
        }
    }
}
