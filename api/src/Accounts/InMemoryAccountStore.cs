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
//  It persists ONLY email + created-at (AC-01) - no player / nickname / room
//  reference (AC-03). It is keyed by the SAME normalized-email SHA-256 hash the
//  Table store uses (see AccountIdentity), so "Sam@x.com" and "sam@x.com" resolve
//  to one account in either store. A ConcurrentDictionary makes concurrent
//  purchases / sign-ins safe without an explicit lock.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A thread-safe, in-memory <see cref="IAccountStore"/> (accounts-identity/02),
/// registered when no storage connection string is configured. Fully functional
/// (create-or-get + read-only lookup) so story 03's sign-in / restore is testable
/// with zero Azure setup - it just does not survive a process restart. Persists
/// only email + created-at (AC-01), keyed by the shared normalized-email hash.
/// </summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    // Keyed by the normalized-email SHA-256 hash (the SAME key scheme as the Table
    // store), so lookups are case / whitespace insensitive and the raw email is
    // never the dictionary key. The value is the immutable Account (email +
    // created-at only). ConcurrentDictionary handles concurrent access; GetOrAdd
    // gives us the idempotent "create exactly once" behaviour for free.
    private readonly ConcurrentDictionary<string, Account> _accounts =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<Account> CreateOrGetAsync(string emailIdentity, CancellationToken ct = default)
    {
        var key = AccountIdentity.KeyFor(emailIdentity);
        // Store the NORMALIZED email on the account so both stores agree on the
        // persisted form. GetOrAdd is atomic: two racing purchases for the same
        // identity both observe the one account, and only one row ever exists (AC-02).
        var normalizedEmail = AccountIdentity.Normalize(emailIdentity);
        var account = _accounts.GetOrAdd(
            key,
            _ => new Account(normalizedEmail, DateTimeOffset.UtcNow));
        return Task.FromResult(account);
    }

    /// <inheritdoc />
    public Task<Account?> GetByIdentityAsync(string emailIdentity, CancellationToken ct = default)
    {
        var key = AccountIdentity.KeyFor(emailIdentity);
        // READ ONLY - a miss returns null and NEVER creates a row (story 03's "no
        // create on miss" guarantee). TryGetValue never inserts.
        return Task.FromResult(_accounts.TryGetValue(key, out var account) ? account : null);
    }
}
