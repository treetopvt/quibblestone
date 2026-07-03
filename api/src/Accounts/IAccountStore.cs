// ----------------------------------------------------------------------------
//  IAccountStore - the storage contract for lightweight purchaser accounts
//  (accounts-identity/02, issue #68).
//
//  There are exactly TWO implementations, chosen once at startup by whether a
//  storage connection string is configured (see Program.cs), MIRRORING the
//  published-tale store's config-presence split - but with one deliberate
//  difference in the "absent" half:
//
//    - TableStorageAccountStore : the real Azure Table Storage impl, used when
//      Accounts:StorageConnectionString is present (a deployed environment,
//      AC-06). Persists one entity per account, keyed by a SHA-256 hash of the
//      normalized email so the raw address is never the key and the key is never
//      guessable.
//    - InMemoryAccountStore : the fallback used with NO connection string (local
//      dev, CI, a fresh clone). UNLIKE the published-tale "disabled no-op", this
//      is a genuinely WORKING thread-safe store, because accounts-identity/03's
//      sign-in / restore flow must be exercisable end-to-end locally with ZERO
//      Azure setup - a no-op account store would make that untestable.
//
//  BOTH implementations normalize the email identity IDENTICALLY (trim +
//  ToLowerInvariant), so "Sam@x.com" and "sam@x.com" resolve to the SAME account.
//
//  TWO METHODS, TWO DISTINCT SEMANTICS - read carefully, story 03 depends on it:
//    - CreateOrGetAsync is the PURCHASE path (accounts-identity/02, AC-02). It is
//      idempotent: the same identity twice returns the same account and leaves
//      exactly ONE row. This is the ONLY path that may create an account.
//    - GetByIdentityAsync is a READ-ONLY lookup (accounts-identity/03's sign-in /
//      restore). It returns null on a miss and NEVER creates a row - a sign-in
//      attempt for an email that never bought must NOT silently mint an account
//      ("no create on miss"). Keeping create and read as separate methods is what
//      makes that guarantee structural, not a matter of remembering a flag.
//
//  CONSUMABLE WITHOUT ROOMS (AC-04): this contract references only Account (email
//  + created-at). It imports NOTHING from api/src/Rooms, so billing-entitlements/
//  01's session-creation gate can ask "is there an entitled purchaser behind this
//  session?" purely against this store, never touching player / room data.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Creates and retrieves lightweight purchaser accounts (accounts-identity/02),
/// keyed by the normalized email identity. One implementation writes to Azure
/// Table Storage (deployed); the other is a working in-memory store used when no
/// storage connection string is configured (local dev / CI). Holds no PII beyond
/// the one email and no room / player reference (AC-03), so it is consumable by
/// the entitlement gate without importing anything from api/src/Rooms (AC-04).
/// </summary>
public interface IAccountStore
{
    /// <summary>
    /// The PURCHASE path (accounts-identity/02, AC-02): return the account for
    /// <paramref name="emailIdentity"/>, creating it if it does not yet exist.
    /// IDEMPOTENT - calling it twice for the same identity (compared after the
    /// shared trim + lowercase-invariant normalization) returns the SAME account
    /// and leaves exactly ONE stored row, so a retried / double-delivered purchase
    /// never mints a duplicate. This is the ONLY method that may create an account;
    /// creation is purchase-triggered only (never a side effect of playing).
    /// </summary>
    /// <param name="emailIdentity">The purchaser's email (normalized internally); the magic-link identity.</param>
    /// <param name="ct">Cancellation for the (storage-bound) create-or-get.</param>
    /// <returns>The existing or newly created account for this identity.</returns>
    Task<Account> CreateOrGetAsync(string emailIdentity, CancellationToken ct = default);

    /// <summary>
    /// A READ-ONLY lookup (accounts-identity/03's sign-in / restore): return the
    /// account for <paramref name="emailIdentity"/>, or null if none exists. It
    /// NEVER creates a row - a sign-in for an email that never purchased must miss
    /// (return null), not silently create an account ("no create on miss"). Story
    /// 03 relies on this method precisely for that guarantee.
    /// </summary>
    /// <param name="emailIdentity">The email to look up (normalized internally).</param>
    /// <param name="ct">Cancellation for the (storage-bound) lookup.</param>
    /// <returns>The account if one exists for this identity, else null (never created here).</returns>
    Task<Account?> GetByIdentityAsync(string emailIdentity, CancellationToken ct = default);
}
