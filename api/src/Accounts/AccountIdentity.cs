// ----------------------------------------------------------------------------
//  AccountIdentity - the ONE place the email login attribute is normalized and
//  hashed (accounts-identity/02, issue #68).
//
//  WHAT IT KEYS, POST accounts-identity/05 (issue #195, ADR 0003 Layer 0): this is
//  now the key of the EMAIL INDEX ROW ONLY - the slim "email hash -> AccountId"
//  lookup the account store keeps so a magic-link sign-in can FIND the account by
//  email. It is NO LONGER the primary key of anything durable: the stable AccountId
//  (a GUID, see Account.Id) is what entitlement grants and cloud-gallery tales
//  partition by now. Email is a mutable login attribute (Account, AC-02), so keying
//  grants / gallery off a hash of it - as the pre-ADR-0003 code did - is exactly the
//  bug story 05 fixes (an email change would orphan them). KeyFor / Normalize stay
//  precisely the right tool for the INDEX row, and nothing else.
//
//  WHY THIS EXISTS: both IAccountStore implementations (Table Storage + in-memory)
//  MUST treat the email identically, or "Sam@x.com" and "sam@x.com" would resolve
//  to different accounts in one store and the same account in the other. Rather
//  than duplicate the trim + lowercase-invariant rule and the SHA-256 key
//  derivation in each store (a DRY smell that would drift), both call these helpers
//  - so the normalization + index-key scheme is defined exactly once.
//
//  KEY SCHEME: the index key is a SHA-256 HEX hash of the NORMALIZED email, NOT the
//  raw email and NOT a guessable sequential id. That keeps the raw address out of
//  the partition / row key (an operator listing keys sees hashes, not inboxes) and
//  makes a point read by email the whole access pattern (no scans). Note this
//  hashing is a keying convenience, NOT a security secret - the email is still
//  stored as a normal entity property so sign-in can read it back; the "never log /
//  never store a secret in plaintext" invariant is about the TOKEN-SIGNING KEY (see
//  MagicLinkTokenService), which is never persisted here at all.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Shared email normalization and index-key derivation for the account stores
/// (accounts-identity/02). Both <see cref="IAccountStore"/> implementations use
/// these helpers so an email resolves to the SAME account regardless of the store,
/// and so the EMAIL INDEX key is a non-guessable hash of the email rather than the
/// raw address. Post accounts-identity/05 this keys ONLY the email-to-AccountId
/// index row - the durable partition key for grants / gallery is now the stable
/// <see cref="Account.Id"/> GUID, not a hash of the (mutable) email.
/// </summary>
internal static class AccountIdentity
{
    /// <summary>
    /// Normalizes an email identity to its canonical form: trimmed of surrounding
    /// whitespace and lowercased with the INVARIANT culture (so the same address
    /// is one account regardless of the caller's culture or the letter case typed).
    /// A null input normalizes to an empty string rather than throwing.
    /// </summary>
    /// <param name="emailIdentity">The raw email as supplied by the caller.</param>
    /// <returns>The canonical (trimmed, invariant-lowercased) identity.</returns>
    public static string Normalize(string? emailIdentity) =>
        (emailIdentity ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Derives the email INDEX key: the lowercase SHA-256 HEX digest of the email's
    /// normalized form (see <see cref="Normalize"/>). Deterministic (the same email
    /// always yields the same key, so a point read finds the index row) and
    /// non-guessable / non-sequential - the raw email is never the key. Post
    /// accounts-identity/05 this keys the email-to-AccountId index row only, not the
    /// primary account row (that is keyed by the stable <see cref="Account.Id"/>).
    /// </summary>
    /// <param name="emailIdentity">The raw or normalized email; normalized here first.</param>
    /// <returns>The 64-char lowercase hex SHA-256 of the normalized email.</returns>
    public static string KeyFor(string? emailIdentity)
    {
        var normalized = Normalize(emailIdentity);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes);
    }
}
