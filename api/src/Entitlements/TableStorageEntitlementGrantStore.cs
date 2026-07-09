// ----------------------------------------------------------------------------
//  TableStorageEntitlementGrantStore - the Azure Table Storage store for purchaser
//  entitlement grants (billing-entitlements/01, issue #70). Mirrors the shape and
//  posture of TableStorageAccountStore (the reference pattern): Azure.Data.Tables
//  (already a project dependency - NO new NuGet), a CreateIfNotExists-once guard,
//  and the same config-presence split (the "absent" half is
//  InMemoryEntitlementGrantStore rather than a no-op, because the gate + stories
//  03-05 need a working local store).
//
//  KEY / SCHEMA DESIGN (AC-05; re-keyed by accounts-identity/05):
//    - PartitionKey = the stable AccountId (Account.Id) as a string - DIRECTLY, no
//      hashing. A GUID is already random and unguessable (unlike a raw email), and
//      it is durable (an email change does not move it), so ALL of an account's
//      grants share ONE partition and the session-creation read is a single
//      partition query - never a scan, never cross-partition. The pre-ADR-0003 key
//      (a SHA-256 of the email) is gone: it orphaned grants on an email change.
//    - RowKey = the capability key (e.g. "library.full", "pack.spooky"). One row
//      per (account, capability), so a subscription renewal UPSERTS - extends the
//      lease in place - rather than piling up rows.
//    - Stored PROPERTIES: ValidThrough (nullable - null = a permanent one-time
//      pack), Source (the GrantSource name), and (billing-entitlements/08) the
//      recovery / support metadata columns GrantId, PlanId, StripeSubscriptionId,
//      and Mode. No PII, no player / room reference.
//
//  ADDITIVE, BACK-COMPAT COLUMNS (billing-entitlements/08 AC-03): the four metadata
//  columns are new. A row written by the already-shipped code has none of them, so
//  FromEntity DEGRADES a missing column rather than throwing - mirroring the existing
//  defensive Source handling: a missing GrantId mints a fresh Guid, a missing PlanId /
//  StripeSubscriptionId reads as null, and a missing Mode reads as Test (the FACTUAL
//  default - no grant in this store predates Stripe Live ever going active, ADR 0003
//  Decision 4). An operator comp (Mode intentionally null) is persisted with an
//  explicit sentinel so it round-trips back to null rather than colliding with the
//  legacy "missing column" default. IsActiveAt is byte-for-byte unchanged.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using QuibbleStone.Api.Billing;

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// The Azure Table Storage account-grant store (billing-entitlements/01, re-keyed by
/// accounts-identity/05). Stores one entity per (account, capability), PartitionKey =
/// the stable AccountId + RowKey = capability key, so an account's whole grant set is
/// one partition query (AC-05), persisting only ValidThrough + Source. Used only when
/// a storage connection string is configured (else InMemoryEntitlementGrantStore).
/// </summary>
public sealed class TableStorageEntitlementGrantStore : IEntitlementGrantStore
{
    /// <summary>The table name grants land in (created on first write if absent).</summary>
    public const string TableName = "EntitlementGrants";

    private const string ValidThroughColumn = "ValidThrough";
    private const string SourceColumn = "Source";

    // billing-entitlements/08 recovery / support metadata columns (all additive).
    private const string GrantIdColumn = "GrantId";
    private const string PlanIdColumn = "PlanId";
    private const string StripeSubscriptionIdColumn = "StripeSubscriptionId";
    private const string ModeColumn = "Mode";

    // The Mode column value for a grant whose Mode is intentionally null (a
    // GrantSource.Operator comp - AC-01). A NON-null sentinel so it is distinguishable
    // from a legacy row that has NO Mode column at all (which defaults to Test - AC-03).
    private const string ModeNoneSentinel = "none";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageEntitlementGrantStore> _logger;

    // Ensure-once guard (same rationale as TableStorageAccountStore): CreateIfNotExists
    // is a round-trip we only need on the FIRST write. A benign race is harmless.
    private volatile bool _tableEnsured;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration,
    /// NEVER a committed literal - see Program.cs). The table is created lazily on
    /// the first grant write.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (supplied per-environment).</param>
    /// <param name="logger">Logs storage failures server-side (never a purchaser secret).</param>
    public TableStorageEntitlementGrantStore(string connectionString, ILogger<TableStorageEntitlementGrantStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntitlementGrant>> GetGrantsAsync(Guid accountId, CancellationToken ct = default)
    {
        var partition = accountId.ToString();
        var grants = new List<EntitlementGrant>();
        try
        {
            // Single-partition query (AC-05): all of this purchaser's grants, no scan.
            var query = _table.QueryAsync<TableEntity>(e => e.PartitionKey == partition, cancellationToken: ct);
            await foreach (var entity in query)
            {
                grants.Add(FromEntity(entity, _logger));
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table does not exist yet (no grant has ever been written) - that is
            // simply an empty result, not an error. Trace it (no PII) so a real
            // storage misconfig is not fully invisible.
            _logger.LogDebug(ex, "Grant table read returned 404 (table not yet created); treating as no grants.");
        }
        return grants;
    }

    /// <inheritdoc />
    public async Task PutGrantAsync(Guid accountId, EntitlementGrant grant, CancellationToken ct = default)
    {
        var partition = accountId.ToString();
        await EnsureTableAsync(ct);

        // UPSERT (Replace): one row per capability, so a renewal extends the lease in
        // place rather than adding a duplicate (story 03's invoice.paid path).
        await _table.UpsertEntityAsync(ToEntity(partition, grant), TableUpdateMode.Replace, ct);
    }

    /// <summary>
    /// Projects a grant to its stored <see cref="TableEntity"/> (the schema, one place):
    /// the lease + source (AC-05) plus billing-entitlements/08's recovery / support
    /// metadata columns. No PII, no player / room reference. Public + static so the schema
    /// round-trip is unit-testable without an Azure connection (AC-01), mirroring
    /// StripeCheckoutService.BuildSessionOptions.
    /// </summary>
    public static TableEntity ToEntity(string partitionKey, EntitlementGrant grant) =>
        new(partitionKey, grant.CapabilityKey)
        {
            [ValidThroughColumn] = grant.ValidThrough,
            [SourceColumn] = grant.Source.ToString(),
            [GrantIdColumn] = grant.GrantId.ToString(),
            [PlanIdColumn] = grant.PlanId,
            [StripeSubscriptionIdColumn] = grant.StripeSubscriptionId,
            // A real mode is stored as its wire value; an intentionally-null mode (an
            // operator comp) as the sentinel, so it round-trips to null rather than to
            // the legacy "missing column -> Test" default on read (AC-01 / AC-03).
            [ModeColumn] = grant.Mode?.ToWire() ?? ModeNoneSentinel,
        };

    // Create the table ONCE (lazy); after the first success the guard skips the
    // extra round-trip on every subsequent write.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }

    /// <summary>
    /// Rebuilds the domain record from a stored entity (billing-entitlements/08 AC-03).
    /// Public + static so a legacy / partially-written row's degradation is unit-testable
    /// without Azure. Defensive on every stored field so a pre-story row degrades to sane
    /// values rather than throwing: a missing/unparseable Source falls back to OneTime (the
    /// most conservative - permanent-shaped - source; the lease still governs activeness), a
    /// missing GrantId mints a fresh id, a missing PlanId / StripeSubscriptionId reads null,
    /// and a missing Mode reads Test (the FACTUAL default - no grant here predates Stripe
    /// Live going active). IsActiveAt is byte-for-byte unchanged from a full row.
    /// </summary>
    public static EntitlementGrant FromEntity(TableEntity entity, ILogger logger)
    {
        GrantSource source;
        if (Enum.TryParse(entity.GetString(SourceColumn), ignoreCase: true, out source))
        {
            // parsed cleanly
        }
        else
        {
            // A missing / unparseable source is a schema-drift or legacy-row signal.
            // Degrade to OneTime (the lease still governs activeness, so this can never
            // wrongly UNLOCK anything) but warn so real drift is visible rather than
            // silently mislabeling the source story 05's restore view would display.
            source = GrantSource.OneTime;
            logger.LogWarning(
                "Entitlement grant row {RowKey} has a missing/unparseable Source; defaulting to OneTime for display.",
                entity.RowKey);
        }
        return new EntitlementGrant(
            CapabilityKey: entity.RowKey,
            ValidThrough: entity.GetDateTimeOffset(ValidThroughColumn),
            Source: source,
            PlanId: entity.GetString(PlanIdColumn),
            StripeSubscriptionId: entity.GetString(StripeSubscriptionIdColumn),
            Mode: ModeFromEntity(entity, logger))
        {
            // A legacy row (no GrantId column) mints a fresh id rather than throwing
            // (AC-03); a stored id is preserved so the write's identity survives a read.
            GrantId = Guid.TryParse(entity.GetString(GrantIdColumn), out var grantId) ? grantId : Guid.NewGuid(),
        };
    }

    // Rebuild the grant's Mode defensively (AC-03): a MISSING column is a pre-story /
    // legacy row -> Test (the FACTUAL default: no grant here predates Stripe Live going
    // active); the explicit sentinel -> null (an operator comp, AC-01); a wire value ->
    // that mode; anything else is schema drift -> Test with a warning (never throws).
    private static StripeMode? ModeFromEntity(TableEntity entity, ILogger logger)
    {
        var raw = entity.GetString(ModeColumn);
        if (raw is null)
        {
            return StripeMode.Test; // legacy row - no Mode column (AC-03)
        }
        if (string.Equals(raw, ModeNoneSentinel, StringComparison.OrdinalIgnoreCase))
        {
            return null; // an operator comp: Mode intentionally absent (AC-01)
        }
        var parsed = StripeModeText.TryParse(raw);
        if (parsed is null)
        {
            logger.LogWarning(
                "Entitlement grant row {RowKey} has an unparseable Mode; defaulting to Test.",
                entity.RowKey);
            return StripeMode.Test;
        }
        return parsed;
    }
}
