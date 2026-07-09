// ----------------------------------------------------------------------------
//  TableStorageAccountStore - the Azure Table Storage store for lightweight family
//  / purchaser accounts (accounts-identity/02, issue #68; re-shaped by
//  accounts-identity/05, issue #195). Mirrors the shape and posture of
//  TableStoragePublishedTaleStore (the reference pattern): Azure.Data.Tables
//  (already a project dependency - NO new NuGet), a CreateIfNotExists-once guard,
//  and the same config-presence split (the "absent" half is InMemoryAccountStore
//  rather than a no-op, because story 03 needs a working local store).
//
//  KEY / SCHEMA DESIGN (accounts-identity/05 - a PRIMARY row + a slim INDEX row):
//    - PRIMARY entity: PartitionKey = "account", RowKey = the stable AccountId (the
//      GUID as a string). Properties: Email (the normalized address, so sign-in can
//      read it back) + CreatedUtc. The AccountId is the durable key - grants and
//      gallery partition by it, and a change of email never disturbs this row's key
//      (AC-01/AC-02).
//    - INDEX entity: PartitionKey = "emailIndex", RowKey = a SHA-256 HEX hash of the
//      NORMALIZED email (AccountIdentity.KeyFor). It holds ONLY the AccountId. This
//      is how a magic-link sign-in FINDS the account by email: read the index to
//      resolve the id, then point-read the primary. The raw email is never a key,
//      and the hash is not a guessable sequential id, so an operator listing index
//      keys sees opaque hashes, not inboxes.
//    - There is NO password, NO name / birthdate / address / phone, and NO player /
//      nickname / room reference (AC-01, AC-03). The token-signing key (the one true
//      secret) lives ONLY in MagicLinkTokenService's config-supplied key and is
//      never persisted here.
//
//  CREATE vs READ posture:
//    - CreateOrGetAsync is the account-CREATE path and must be idempotent (AC-02):
//      it reads the email INDEX first and returns the existing account if present;
//      on a miss it mints a fresh AccountId, writes the PRIMARY row, then Adds the
//      INDEX row. A concurrent create that loses the INDEX Add race (409 Conflict)
//      re-reads the index and returns the winner, so two racing sign-ups resolve to
//      ONE account. The loser's just-written primary row is unreferenced (nothing
//      indexes it, so no read ever returns it); we best-effort DELETE it in the 409
//      path so a storm of racing creates cannot accumulate dead rows, and if that
//      cleanup fails the row is still harmless.
//    - GetByIdentityAsync is READ ONLY BY EMAIL: index -> primary; a missing email
//      is an ordinary null and NEVER creates a row (story 03's "no create on miss").
//    - GetByIdAsync is READ ONLY BY AccountId (AC-04): a single primary point read,
//      no index round-trip.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The Azure Table Storage family / purchaser-account store (accounts-identity/02,
/// re-shaped by 05). Stores a PRIMARY entity per account (PartitionKey = "account",
/// RowKey = the stable AccountId, holding Email + CreatedUtc) plus a slim INDEX
/// entity (PartitionKey = "emailIndex", RowKey = SHA-256 hash of the normalized
/// email, holding the AccountId) so an email resolves to the account without email
/// being the durable key (AC-01/AC-02/AC-04). No room / player reference (AC-03).
/// Used only when a storage connection string is configured (else
/// InMemoryAccountStore).
/// </summary>
public sealed class TableStorageAccountStore : IAccountStore
{
    /// <summary>The table name accounts land in (created on first write if absent).</summary>
    public const string TableName = "PurchaserAccounts";

    // The primary rows share ONE partition ("account"); the index rows share another
    // ("emailIndex"). Both live in the same table, kept apart by partition, so a
    // by-id read and an email->id read are each a single point read.
    private const string PrimaryPartition = "account";
    private const string IndexPartition = "emailIndex";
    private const string EmailColumn = "Email";
    private const string CreatedUtcColumn = "CreatedUtc";
    // The INDEX row's single property: the stable AccountId it maps the email to.
    private const string AccountIdColumn = "AccountId";

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
        var emailHash = AccountIdentity.KeyFor(emailIdentity);
        var normalizedEmail = AccountIdentity.Normalize(emailIdentity);

        // Idempotent (AC-02): if the email already indexes an account, return it
        // unchanged (its stable id + created-at do not move on a repeat).
        var existing = await ReadByEmailHashAsync(emailHash, ct);
        if (existing is not null)
        {
            return existing;
        }

        await EnsureTableAsync(ct);

        // Mint the durable AccountId ONCE (AC-01). Write the PRIMARY row first (keyed
        // by the id), then the INDEX row (email hash -> id) - so the primary always
        // exists before anything points at it.
        var accountId = Guid.NewGuid();
        var createdUtc = DateTimeOffset.UtcNow;

        var primary = new TableEntity(PrimaryPartition, accountId.ToString())
        {
            // Only the normalized email + created-at (AC-01). No PII beyond the one
            // email, no player / room reference (AC-03).
            [EmailColumn] = normalizedEmail,
            [CreatedUtcColumn] = createdUtc,
        };
        // The primary RowKey is a fresh GUID, so its Add never conflicts; a UNIQUE
        // insert (Add) rather than upsert keeps the "one primary per id" invariant.
        await _table.AddEntityAsync(primary, ct);

        var index = new TableEntity(IndexPartition, emailHash)
        {
            [AccountIdColumn] = accountId.ToString(),
        };

        try
        {
            // The INDEX row is the idempotency guard (one per email): Add, not upsert,
            // so a concurrent create for the SAME email is caught as a 409.
            await _table.AddEntityAsync(index, ct);
            return new Account(accountId, normalizedEmail, createdUtc);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // A concurrent sign-up for the SAME email won the INDEX Add race. Our own
            // just-written primary row (a different, losing id) is now unreferenced -
            // nothing indexes it, so no read ever returns it. Best-effort DELETE it so a
            // storm of racing creates cannot accumulate dead rows in PurchaserAccounts
            // (Copilot review); the delete is best-effort because an undeleted row is
            // still harmless. Then re-read the index and return the WINNER so
            // create-or-get stays idempotent (one account per email). Log at Debug (the
            // hashed key only - never the raw email or any secret): a benign, expected race.
            _logger.LogDebug(ex, "Concurrent account create lost the index Add race (409); cleaning up the orphaned primary row and re-reading the winner.");
            await TryDeletePrimaryAsync(accountId, ct);
            var winner = await ReadByEmailHashAsync(emailHash, ct);
            if (winner is not null)
            {
                return winner;
            }

            // The 409 proves the index row EXISTS, so a null re-read here is not a
            // normal miss - it is a transient read failure. Fabricating an Account
            // would claim a persistence this call did not make. Rethrow so the caller
            // sees a real error instead of a false success.
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Account?> GetByIdentityAsync(string emailIdentity, CancellationToken ct = default)
    {
        // READ ONLY BY EMAIL - resolve the email hash to an id via the index, then
        // read the primary. A miss returns null and NEVER creates a row (story 03's
        // "no create on miss" guarantee).
        var emailHash = AccountIdentity.KeyFor(emailIdentity);
        return await ReadByEmailHashAsync(emailHash, ct);
    }

    /// <inheritdoc />
    public async Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        // READ ONLY BY AccountId (AC-04) - a single primary point read, no index
        // round-trip. A miss returns null and NEVER creates a row.
        return await ReadPrimaryAsync(accountId, ct);
    }

    // Resolve an email hash to its account: read the INDEX row for the id, then the
    // PRIMARY row for the account. A missing index row - or a dangling index whose
    // primary is gone - reads as a clean null.
    private async Task<Account?> ReadByEmailHashAsync(string emailHash, CancellationToken ct)
    {
        var index = await PointReadAsync(IndexPartition, emailHash, ct);
        if (index is null)
        {
            return null;
        }
        var accountIdText = index.GetString(AccountIdColumn);
        if (!Guid.TryParse(accountIdText, out var accountId))
        {
            // A malformed / empty index value is schema drift, not a normal miss.
            // Warn (the hashed key only, never the raw email) so real drift is visible,
            // and treat it as a miss rather than throwing.
            _logger.LogWarning("Account email-index row {RowKey} has a missing/unparseable AccountId; treating as a miss.", emailHash);
            return null;
        }
        return await ReadPrimaryAsync(accountId, ct);
    }

    // Best-effort delete of a PRIMARY row we wrote but then lost the index Add race
    // for (see CreateOrGetAsync's 409 path). Swallows failures: an undeleted orphan is
    // harmless (unreferenced), so cleanup must never turn a benign race into a thrown
    // error. A 404 (already gone) is likewise a no-op. Uses ETag.All (unconditional).
    private async Task TryDeletePrimaryAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            await _table.DeleteEntityAsync(PrimaryPartition, accountId.ToString(), ETag.All, ct);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogDebug(ex, "Best-effort cleanup of an orphaned primary account row failed; leaving it (harmless, unreferenced).");
        }
    }

    // Point-read one PRIMARY account row by its stable id, or null if absent.
    private async Task<Account?> ReadPrimaryAsync(Guid accountId, CancellationToken ct)
    {
        var entity = await PointReadAsync(PrimaryPartition, accountId.ToString(), ct);
        return entity is null ? null : FromEntity(accountId, entity);
    }

    // A single point read that treats a table-not-yet-created 404 as an ordinary
    // miss (no account has ever been written). Shared by the index and primary reads.
    private async Task<TableEntity?> PointReadAsync(string partitionKey, string rowKey, CancellationToken ct)
    {
        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
            return response.HasValue ? response.Value : null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table itself does not exist yet (no account has ever been created)
            // - that is simply a miss, not an error. Trace it (no PII) so a real
            // storage misconfig is not fully invisible.
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

    // Rebuild the domain record from a stored PRIMARY entity + its id (the RowKey,
    // already parsed by the caller). Defensive on the stored fields so a partially-
    // written / legacy row degrades to sane values rather than throwing.
    private static Account FromEntity(Guid accountId, TableEntity entity) =>
        new(
            Id: accountId,
            Email: entity.GetString(EmailColumn) ?? string.Empty,
            CreatedUtc: entity.GetDateTimeOffset(CreatedUtcColumn) ?? DateTimeOffset.UtcNow);
}
