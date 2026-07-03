// ----------------------------------------------------------------------------
//  TableStorageAccountStore - the Azure Table Storage store for lightweight
//  purchaser accounts (accounts-identity/02, issue #68). Mirrors the shape and
//  posture of TableStoragePublishedTaleStore (the reference pattern):
//  Azure.Data.Tables (already a project dependency - NO new NuGet), a
//  CreateIfNotExists-once guard, and the same config-presence split (the "absent"
//  half is InMemoryAccountStore rather than a no-op, because story 03 needs a
//  working local store).
//
//  KEY / SCHEMA DESIGN (AC-06 spirit):
//    - PartitionKey = RowKey = a SHA-256 HEX hash of the NORMALIZED email (see
//      AccountIdentity.KeyFor). The raw email is NEVER the key, and the key is not
//      a guessable sequential id - so a point read by identity is the whole access
//      pattern (GetEntity by partition + row), never a scan or a cross-partition
//      query, and an operator listing keys sees opaque hashes, not inboxes.
//    - The stored PROPERTIES are just Email (the normalized address, so story 03
//      can read it back) and CreatedUtc. There is NO password, NO name / birthdate
//      / address / phone, and NO player / nickname / room reference (AC-01, AC-03).
//      The token-signing key (the one true secret, AC-06) lives ONLY in
//      MagicLinkTokenService's config-supplied key and is never persisted here.
//
//  CREATE vs READ posture:
//    - CreateOrGetAsync is the PURCHASE path and must be idempotent (AC-02): it
//      point-reads first and returns the existing account if present; otherwise it
//      Adds. A concurrent create that loses the Add race (409 Conflict) re-reads
//      and returns the winner, so two racing purchases still leave ONE row.
//    - GetByIdentityAsync is READ ONLY: a missing identity is an ordinary null and
//      NEVER creates a row (story 03's "no create on miss").
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The Azure Table Storage purchaser-account store (accounts-identity/02). Stores
/// one entity per account, keyed PartitionKey = RowKey = SHA-256 hash of the
/// normalized email for a single-lookup point read (AC-06 spirit), persisting only
/// Email + CreatedUtc (AC-01) with no room / player reference (AC-03). Used only
/// when a storage connection string is configured (else InMemoryAccountStore).
/// </summary>
public sealed class TableStorageAccountStore : IAccountStore
{
    /// <summary>The table name accounts land in (created on first write if absent).</summary>
    public const string TableName = "PurchaserAccounts";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageAccountStore> _logger;

    // Ensure-once guard (same rationale as TableStoragePublishedTaleStore):
    // CreateIfNotExists is a round-trip we only need on the FIRST write. A benign
    // race is harmless (idempotent); a failed create leaves the flag false so the
    // next create retries.
    private volatile bool _tableEnsured;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration,
    /// NEVER a committed literal - see Program.cs). The connection is resolved once
    /// at startup; the table is created lazily on the first account create.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (supplied per-environment).</param>
    /// <param name="logger">Logs storage failures server-side (never the token-signing key, AC-06).</param>
    public TableStorageAccountStore(string connectionString, ILogger<TableStorageAccountStore> logger)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Account> CreateOrGetAsync(string emailIdentity, CancellationToken ct = default)
    {
        var key = AccountIdentity.KeyFor(emailIdentity);
        var normalizedEmail = AccountIdentity.Normalize(emailIdentity);

        // Idempotent (AC-02): if the account already exists, return it unchanged
        // rather than overwriting its created-at.
        var existing = await ReadAsync(key, ct);
        if (existing is not null)
        {
            return existing;
        }

        await EnsureTableAsync(ct);

        var createdUtc = DateTimeOffset.UtcNow;
        var entity = new TableEntity(key, key)
        {
            // Only the normalized email + created-at (AC-01). No PII beyond the one
            // identity, no player / room reference (AC-03).
            ["Email"] = normalizedEmail,
            ["CreatedUtc"] = createdUtc,
        };

        try
        {
            await _table.AddEntityAsync(entity, ct);
            return new Account(normalizedEmail, createdUtc);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // A concurrent purchase for the SAME identity won the Add race. Re-read
            // and return the winner so the create-or-get stays idempotent (one row).
            // Log at Debug (the hashed key only - never the raw email or any secret,
            // AC-06): a benign, expected race, not an error.
            _logger.LogDebug(ex, "Concurrent account create lost the Add race (409); re-reading the existing account.");
            var winner = await ReadAsync(key, ct);
            return winner ?? new Account(normalizedEmail, createdUtc);
        }
    }

    /// <inheritdoc />
    public async Task<Account?> GetByIdentityAsync(string emailIdentity, CancellationToken ct = default)
    {
        // READ ONLY - a miss returns null and NEVER creates a row (story 03's "no
        // create on miss" guarantee).
        var key = AccountIdentity.KeyFor(emailIdentity);
        return await ReadAsync(key, ct);
    }

    // Point-read one account by its (hashed) key, or null if absent. Shared by both
    // the create path (idempotency check + Add-race recovery) and the read path.
    private async Task<Account?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(key, key, cancellationToken: ct);
            if (!response.HasValue || response.Value is null)
            {
                return null;
            }
            return FromEntity(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table itself does not exist yet (no account has ever been created)
            // - that is simply a miss, not an error. Trace it (no PII, AC-06) so a
            // real storage misconfig is not fully invisible.
            _logger.LogDebug(ex, "Account table read returned 404 (table not yet created); treating as a miss.");
            return null;
        }
    }

    // Create the table ONCE (lazy); after the first success the guard skips the
    // extra round-trip on every subsequent create.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(ct);
            _tableEnsured = true;
        }
    }

    // Rebuild the domain record from a stored entity. Defensive on the stored
    // fields so a partially-written / legacy row degrades to sane values rather
    // than throwing.
    private static Account FromEntity(TableEntity entity) =>
        new(
            Email: entity.GetString("Email") ?? string.Empty,
            CreatedUtc: entity.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow);
}
