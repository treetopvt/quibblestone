// ----------------------------------------------------------------------------
//  InMemoryAccountStore - the WORKING fallback account store used when NO storage
//  connection string is configured (accounts-identity/02, local dev / CI / a fresh
//  clone).
//
//  This is DELIBERATELY NOT a no-op (unlike DisabledPublishedTaleStore). The
//  published-tale feature can be switched fully off with no Azure setup because a
//  missing tale is a harmless 404; but accounts-identity/03's sign-in / restore
//  flow must be exercisable END TO END on a laptop with zero Azure, so this store
//  actually creates, remembers, and returns accounts - just in process memory
//  instead of Azure Table Storage. The moment Accounts:StorageConnectionString is
//  present (a deployed environment), Program.cs registers TableStorageAccountStore
//  instead and accounts persist across restarts; the semantics of BOTH stores are
//  identical, only the durability differs.
//
//  It persists ONLY the stable AccountId + email + created-at (AC-01) - no player /
//  nickname / room reference (AC-03). It mirrors the Table store's PRIMARY-plus-
//  INDEX shape (accounts-identity/05) with two dictionaries: the primary is keyed
//  by the stable AccountId, and a slim index maps the normalized-email SHA-256 hash
//  (see AccountIdentity) to that id - so "Sam@x.com" and "sam@x.com" resolve to one
//  account in either store, the durable key is the AccountId (not the mutable
//  email), and a by-id lookup is a direct point read (AC-04). ConcurrentDictionaries
//  make reads lock-free; a small create lock keeps the two-map create idempotent.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A thread-safe, in-memory <see cref="IAccountStore"/> (accounts-identity/02,
/// re-shaped by 05), registered when no storage connection string is configured.
/// Fully functional (create-or-get + by-email + by-id lookup) so story 03's sign-in
/// / restore is testable with zero Azure setup - it just does not survive a process
/// restart. Persists only the stable AccountId + email + created-at (AC-01); the
/// primary map is keyed by AccountId, a slim index maps the email hash to the id.
/// </summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    // PRIMARY: the AccountId -> Account map (mirrors the Table primary row keyed by
    // the stable GUID). This is the durable identity everything keys off; a by-id
    // lookup (AC-04) is a direct point read here.
    private readonly ConcurrentDictionary<Guid, Account> _byId = new();

    // INDEX: the normalized-email SHA-256 hash -> AccountId map (mirrors the slim
    // Table index row). The raw email is never a dictionary key; an email change
    // would only rewrite this index, never disturb the primary or anything keyed by
    // AccountId (AC-02). ConcurrentDictionary keeps reads lock-free.
    private readonly ConcurrentDictionary<string, Guid> _idByEmailHash =
        new(StringComparer.Ordinal);

    // The create path spans two maps (write primary, then index), so it cannot lean
    // on a single GetOrAdd for atomicity. A tiny lock keeps CreateOrGet idempotent:
    // two racing sign-ups for the same email observe the one account (one id, one
    // primary row), never two (AC-02). Reads never take this lock.
    private readonly object _createLock = new();

    /// <inheritdoc />
    public Task<Account> CreateOrGetAsync(string emailIdentity, CancellationToken ct = default)
    {
        var emailHash = AccountIdentity.KeyFor(emailIdentity);

        // Fast path: the email already indexes an existing account - return it
        // unchanged (its stable id and created-at do not move on a repeat, AC-02).
        if (TryResolveByEmailHash(emailHash, out var existing))
        {
            return Task.FromResult(existing);
        }

        lock (_createLock)
        {
            // Re-check under the lock so a racer that created it first wins (one row).
            if (TryResolveByEmailHash(emailHash, out var winner))
            {
                return Task.FromResult(winner);
            }

            // Mint the stable AccountId ONCE (AC-01) and store the NORMALIZED email so
            // both stores agree on the persisted form. Write the primary first, then
            // point the email index at it (the Table store's ordering, mirrored).
            var account = new Account(
                Id: Guid.NewGuid(),
                Email: AccountIdentity.Normalize(emailIdentity),
                CreatedUtc: DateTimeOffset.UtcNow);
            _byId[account.Id] = account;
            _idByEmailHash[emailHash] = account.Id;
            return Task.FromResult(account);
        }
    }

    /// <inheritdoc />
    public Task<Account?> GetByIdentityAsync(string emailIdentity, CancellationToken ct = default)
    {
        // READ ONLY BY EMAIL - resolve the email hash to an id via the index, then
        // read the primary. A miss returns null and NEVER creates a row (story 03's
        // "no create on miss" guarantee).
        var emailHash = AccountIdentity.KeyFor(emailIdentity);
        return Task.FromResult(TryResolveByEmailHash(emailHash, out var account) ? account : null);
    }

    /// <inheritdoc />
    public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        // READ ONLY BY AccountId (AC-04) - a direct primary point read, no email
        // round-trip. A miss returns null and NEVER creates a row.
        return Task.FromResult(_byId.TryGetValue(accountId, out var account) ? account : null);
    }

    // Resolve an email-hash index entry to its primary account. Two-step (index ->
    // primary) exactly like the Table store, so a dangling index (id with no primary,
    // never produced by the create path) reads as a clean miss rather than throwing.
    private bool TryResolveByEmailHash(string emailHash, out Account account)
    {
        if (_idByEmailHash.TryGetValue(emailHash, out var id) && _byId.TryGetValue(id, out var found))
        {
            account = found;
            return true;
        }
        account = null!;
        return false;
    }
}
