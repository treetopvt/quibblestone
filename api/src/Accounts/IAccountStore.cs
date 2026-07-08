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
//  THREE METHODS, THREE DISTINCT SEMANTICS - read carefully, story 03 depends on it:
//    - CreateOrGetAsync is the account-CREATE path (accounts-identity/02's purchase,
//      07's free family sign-up, AC-02). It is idempotent: the same email twice
//      returns the same account (same stable AccountId) and leaves exactly ONE
//      account. This is the ONLY path that may create an account.
//    - GetByIdentityAsync is a READ-ONLY lookup BY EMAIL (accounts-identity/03's
//      sign-in / restore, billing's purchaser resolution). It returns null on a
//      miss and NEVER creates a row - a sign-in attempt for an email that never
//      signed up must NOT silently mint an account ("no create on miss"). Keeping
//      create and read as separate methods makes that guarantee structural.
//    - GetByIdAsync is a READ-ONLY lookup BY AccountId (accounts-identity/05, AC-04):
//      a caller that already holds a stable AccountId (a resolved family-device
//      token, story 09; a future support / vault-claim lookup) resolves the account
//      directly, never round-tripping through an email. Also null on a miss, never
//      creates.
//
//  WHY EMAIL IS NOT THE PRIMARY KEY (accounts-identity/05, AC-01/AC-02): the stable
//  AccountId (Account.Id, a GUID minted once at creation) is the durable identity;
//  the email is a MUTABLE login attribute. GetByIdentityAsync resolves an email to
//  the account via the store's slim email-hash INDEX (AccountIdentity.KeyFor), then
//  reads the account by its AccountId - so an email change never orphans grants or
//  gallery (which key off AccountId, not the email).
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
    /// A READ-ONLY lookup BY EMAIL (accounts-identity/03's sign-in / restore): return
    /// the account for <paramref name="emailIdentity"/>, or null if none exists. It
    /// NEVER creates a row - a sign-in for an email that never signed up must miss
    /// (return null), not silently create an account ("no create on miss"). Story 03
    /// relies on this method precisely for that guarantee. Internally resolves the
    /// email to an AccountId via the email-hash index, then reads the account.
    /// </summary>
    /// <param name="emailIdentity">The email to look up (normalized internally).</param>
    /// <param name="ct">Cancellation for the (storage-bound) lookup.</param>
    /// <returns>The account if one exists for this email, else null (never created here).</returns>
    Task<Account?> GetByIdentityAsync(string emailIdentity, CancellationToken ct = default);

    /// <summary>
    /// A READ-ONLY lookup BY AccountId (accounts-identity/05, AC-04): return the
    /// account whose stable <see cref="Account.Id"/> is <paramref name="accountId"/>,
    /// or null if none exists. For a caller that already holds an AccountId (a
    /// resolved family-device token - story 09; a future support / vault-claim
    /// lookup) so it never needs to round-trip through an email to find the account.
    /// NEVER creates a row. It resolves the SAME account
    /// <see cref="GetByIdentityAsync"/> would for that account's email (AC-06).
    /// </summary>
    /// <param name="accountId">The stable account id to look up.</param>
    /// <param name="ct">Cancellation for the (storage-bound) lookup.</param>
    /// <returns>The account if one exists with this id, else null (never created here).</returns>
    Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default);
}
