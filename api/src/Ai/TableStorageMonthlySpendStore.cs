// ----------------------------------------------------------------------------
//  IMonthlySpendStore + TableStorageMonthlySpendStore - the PERSISTED running
//  monthly AI-spend total behind the spend circuit-breaker (ai-cost-gate story 04,
//  issue #123, AC-02 / AC-09).
//
//  WHY PERSISTED (and not in-memory like the story-03 quota): the spend total is
//  the AUTHORITATIVE fast-path spend figure that enforces the $20 ceiling, so it
//  MUST survive a process recycle / redeploy - a runaway cannot be forgiven just
//  because the App Service restarted (AC-02). Azure billing data lags hours, so
//  this app-estimated total is the real-time enforcer; the Azure budget alert
//  (story 06) is the slower authoritative backstop the two reconcile against.
//
//  WHERE IT LIVES (NO new resource): the SAME Azure Storage account + connection
//  string the serve-log / telemetry sink already uses
//  (Telemetry:StorageConnectionString), via the SAME Azure.Data.Tables dependency
//  (README section 9: Storage is the in-charter sink). One tiny row:
//    PartitionKey = "spend", RowKey = "YYYY-MM" (the UTC month).
//  Keying the row by UTC month means a new month's total starts at zero NATURALLY
//  (a fresh RowKey), so the breaker "resets and AI resumes" at the month boundary
//  with no sweep job (AC-03).
//
//  CONCURRENCY (AC-02, "must NOT systematically undercount past the ceiling"):
//  increments are an optimistic-concurrency read-modify-write. A create races on
//  409 (another writer created the row first) and an update races on 412 (the ETag
//  moved); either way we re-read and retry so no concurrent increment is silently
//  lost. Exactness to the penny is not required, but two simultaneous calls must
//  not both write "old + mine" and drop one - the ETag guard prevents that.
//
//  FAIL POSTURE (AC-09, two DIFFERENT directions):
//    - READ (TryReadMonthTotalUsdAsync) fails to the SAFE side: a genuinely
//      UNREADABLE total returns null so the breaker treats spend as AT-ceiling and
//      degrades to the fallback rather than calling AI blind. A simply-absent row
//      (nothing spent yet this month) is NOT unreadable - it returns 0.
//    - WRITE (AddAsync) is best-effort: it never blocks gameplay. Transient store
//      failures propagate to the breaker's RecordAsync, which swallows + logs them
//      (metering never gates gameplay); sustained write contention is logged and
//      the single increment dropped rather than throwing.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The persisted running monthly AI-spend total behind the spend breaker (story 04,
/// AC-02). Abstracted behind an interface so <see cref="AiSpendBreaker"/> is unit-
/// testable with an in-memory fake and the real Table Storage I/O stays isolated
/// here. Read fails SAFE (null =&gt; unreadable =&gt; breaker treats as at-ceiling);
/// write is best-effort and never gates gameplay (AC-09).
/// </summary>
public interface IMonthlySpendStore
{
    /// <summary>
    /// Reads the running USD total for the given UTC month (e.g. "2026-07"). Returns
    /// 0 when no row exists yet (nothing spent this month), or <c>null</c> when the
    /// total genuinely cannot be read (store down / misconfigured) so the breaker
    /// fails to the SAFE side and degrades rather than calling AI blind (AC-09).
    /// </summary>
    /// <param name="monthKey">The UTC month row key, "YYYY-MM".</param>
    /// <param name="cancellationToken">Cancellation for the read.</param>
    Task<decimal?> TryReadMonthTotalUsdAsync(string monthKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="amountUsd"/> to the running total for the given UTC month
    /// (creating the row if absent) with an ETag-guarded read-modify-write so
    /// concurrent increments are not lost (AC-02). Best-effort: it may throw a
    /// transient store failure for the caller to swallow, but it never blocks
    /// gameplay.
    /// </summary>
    /// <param name="monthKey">The UTC month row key, "YYYY-MM".</param>
    /// <param name="amountUsd">The estimated cost to add (ignored when &lt;= 0).</param>
    /// <param name="cancellationToken">Cancellation for the write.</param>
    Task AddAsync(string monthKey, decimal amountUsd, CancellationToken cancellationToken = default);
}

/// <summary>
/// Azure Table Storage implementation of <see cref="IMonthlySpendStore"/> (story 04,
/// AC-02). Persists ONE row per UTC month (PartitionKey "spend", RowKey "YYYY-MM")
/// in the already-provisioned Storage account, incremented with an optimistic-
/// concurrency retry so concurrent calls do not lose increments. Registered in
/// Program.cs only when Telemetry:StorageConnectionString is present (mirrors the
/// ITelemetrySink config-presence idiom); with no connection string the app uses
/// the in-memory guard instead and this class is never constructed.
/// </summary>
public sealed class TableStorageMonthlySpendStore : IMonthlySpendStore
{
    /// <summary>The table the monthly total lives in (created on first write if absent).</summary>
    public const string TableName = "AiSpend";

    /// <summary>The single partition every monthly-total row shares.</summary>
    public const string SpendPartitionKey = "spend";

    // The entity column holding the running total. Table Storage has no decimal
    // column type, so the total round-trips through a double here; the estimator's
    // arithmetic (AiCostEstimator) stays in decimal, and a running total against a
    // $20 ceiling is well within double's exact-integer-cents range in practice.
    private const string TotalColumn = "TotalUsd";

    // Bounded optimistic-concurrency retries. Under normal single-instance load an
    // increment succeeds first try; a burst of concurrent calls may lose a couple of
    // ETag races before winning. If contention is so sustained that even this many
    // attempts all lose, we log and drop the ONE increment rather than spin or throw
    // (AC-09: metering never gates gameplay). This slightly UNDER-counts under
    // pathological contention, which is the safe direction only for the player - so
    // it is paired with the fail-safe READ that stops AI if the total is unreadable.
    private const int MaxIncrementAttempts = 8;

    private readonly TableClient _table;
    private readonly ILogger<TableStorageMonthlySpendStore> _logger;

    // Ensure-once guard (mirrors TableStorageTelemetrySink): CreateIfNotExists is a
    // network round-trip we only need on the FIRST access, not every call. A benign
    // race (two concurrent first-accesses both create) is idempotent and harmless; a
    // failed create leaves the flag false so the next access retries.
    private volatile bool _tableEnsured;

    /// <summary>
    /// Constructs the store over the storage connection string (from configuration /
    /// Key Vault, NEVER a committed literal - see Program.cs). The table is created
    /// lazily on first access and then ensured only once.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (shared with the telemetry sink).</param>
    /// <param name="logger">Logs read/write failures server-side (AC-09).</param>
    public TableStorageMonthlySpendStore(string connectionString, ILogger<TableStorageMonthlySpendStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<decimal?> TryReadMonthTotalUsdAsync(string monthKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

            var response = await _table
                .GetEntityIfExistsAsync<TableEntity>(SpendPartitionKey, monthKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            // Absent row = nothing spent yet this month. That is a KNOWN-zero, NOT an
            // unreadable total, so the breaker stays closed and AI proceeds (AC-09).
            if (!response.HasValue || response.Value is null)
            {
                return 0m;
            }

            return ReadTotal(response.Value);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation (a dropped client / shed round) - propagate.
            throw;
        }
        catch (Exception ex)
        {
            // Genuinely UNREADABLE (store down / misconfigured / throttled): return
            // null so the breaker fails to the SAFE side and degrades to the fallback
            // rather than calling AI blind (AC-09). We log only the anonymous month
            // key - never anything about a player.
            _logger.LogWarning(
                ex,
                "AI monthly spend total unreadable for {MonthKey} (breaker will treat as at-ceiling).",
                monthKey);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(string monthKey, decimal amountUsd, CancellationToken cancellationToken = default)
    {
        // A zero/negative estimate never moves the total (and a create with 0 is
        // pointless) - nothing to record.
        if (amountUsd <= 0m)
        {
            return;
        }

        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; attempt < MaxIncrementAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await _table
                .GetEntityIfExistsAsync<TableEntity>(SpendPartitionKey, monthKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            try
            {
                if (!existing.HasValue || existing.Value is null)
                {
                    // No row yet this month: create it at this increment. AddEntity
                    // fails 409 if a concurrent writer created it first - caught below
                    // and retried into the update path so neither increment is lost.
                    var created = new TableEntity(SpendPartitionKey, monthKey)
                    {
                        [TotalColumn] = (double)amountUsd,
                    };
                    await _table.AddEntityAsync(created, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // Read-modify-write guarded by the entity's ETag: UpdateEntity fails
                // 412 if another writer changed the row since our read - caught below
                // and retried so the concurrent increment is not clobbered (AC-02).
                var entity = existing.Value;
                var updatedTotal = ReadTotal(entity) + amountUsd;
                entity[TotalColumn] = (double)updatedTotal;
                await _table
                    .UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
            {
                // Lost the optimistic-concurrency race (create collided / ETag moved).
                // Re-read and retry so no increment is dropped - do NOT rethrow.
                continue;
            }
        }

        // Exhausted the retry budget under sustained contention. Log and drop this ONE
        // increment rather than throw or spin (AC-09: metering never gates gameplay).
        _logger.LogWarning(
            "AI monthly spend increment for {MonthKey} lost {Attempts} optimistic-concurrency races; dropping this increment.",
            monthKey,
            MaxIncrementAttempts);
    }

    /// <summary>Reads the stored double total back into decimal (defaulting to 0 for a missing column).</summary>
    private static decimal ReadTotal(TableEntity entity)
    {
        var value = entity.GetDouble(TotalColumn) ?? 0d;
        return (decimal)value;
    }

    /// <summary>Ensures the table exists, once (see the ensure-once guard rationale in the header).</summary>
    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableEnsured)
        {
            return;
        }

        await _table.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _tableEnsured = true;
    }
}
