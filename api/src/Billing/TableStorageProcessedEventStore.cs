// ----------------------------------------------------------------------------
//  TableStorageProcessedEventStore - the Azure Table Storage idempotency ledger for
//  Stripe webhook events (billing-entitlements/03, issue #72, AC-05). Mirrors the
//  posture of the grant / account stores: Azure.Data.Tables (no new NuGet), a
//  CreateIfNotExists-once guard, reusing the SAME storage account as the grant store.
//
//  SCHEMA: one entity per processed event, PartitionKey = a constant bucket, RowKey =
//  the Stripe event id (globally unique, so a point read by id is the whole access
//  pattern). No payload is stored - the id's PRESENCE is the fact. No PII.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The Azure Table Storage <see cref="IProcessedEventStore"/> (billing-entitlements/03).
/// One entity per processed Stripe event id (PartitionKey = fixed bucket, RowKey =
/// event id) for a single-point idempotency check. Used only when a storage
/// connection string is configured (else InMemoryProcessedEventStore).
/// </summary>
public sealed class TableStorageProcessedEventStore : IProcessedEventStore
{
    /// <summary>The table name processed-event markers land in.</summary>
    public const string TableName = "StripeProcessedEvents";

    // All markers share one partition - the set is small (one row per lifetime event)
    // and every access is a point read/write by the globally-unique event id.
    private const string Partition = "stripe";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageProcessedEventStore> _logger;
    private volatile bool _tableEnsured;

    /// <summary>Constructs the store over a storage connection string (from configuration, never a committed literal).</summary>
    public TableStorageProcessedEventStore(string connectionString, ILogger<TableStorageProcessedEventStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HasProcessedAsync(string eventId, CancellationToken ct = default)
    {
        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(Partition, eventId, cancellationToken: ct);
            return response.HasValue;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table does not exist yet (no event ever processed) - simply "not
            // processed", not an error.
            _logger.LogDebug(ex, "Processed-event table read returned 404 (table not yet created); treating as not processed.");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(string eventId, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);
        // Upsert so recording the same id twice is harmless (idempotent).
        await _table.UpsertEntityAsync(new TableEntity(Partition, eventId), TableUpdateMode.Replace, ct);
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }
}
