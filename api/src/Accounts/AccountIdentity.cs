// ----------------------------------------------------------------------------
//  AccountIdentity - the ONE place the email identity is normalized and hashed
//  (accounts-identity/02, issue #68).
//
//  WHY THIS EXISTS: both IAccountStore implementations (Table Storage + in-memory)
//  MUST treat the email identity identically, or "Sam@x.com" and "sam@x.com" would
//  resolve to different accounts in one store and the same account in the other.
//  Rather than duplicate the trim + lowercase-invariant rule and the SHA-256 key
//  derivation in each store (a DRY smell that would drift), both call these helpers
//  - so the normalization + key scheme is defined exactly once.
//
//  KEY SCHEME (AC-06 spirit): the storage key is a SHA-256 HEX hash of the
//  NORMALIZED email, NOT the raw email and NOT a guessable sequential id. That
//  keeps the raw address out of the partition / row key (an operator listing keys
//  sees hashes, not inboxes) and makes a point read by identity the whole access
//  pattern (no scans). Note this hashing is a keying convenience, NOT a security
//  secret - the email is still stored as a normal entity property so story 03 can
//  read it back; the AC-06 "never log / never store a secret in plaintext"
//  invariant is about the TOKEN-SIGNING KEY (see MagicLinkTokenService), which is
//  never persisted here at all.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Shared email-identity normalization and key derivation for the account stores
/// (accounts-identity/02). Both <see cref="IAccountStore"/> implementations use
/// these helpers so an identity resolves to the SAME account regardless of the
/// store, and so the storage key is a non-guessable hash of the email rather than
/// the raw address (AC-06 spirit).
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
    /// Derives the stable storage key for an identity: the lowercase SHA-256 HEX
    /// digest of its normalized form (see <see cref="Normalize"/>). Deterministic
    /// (the same identity always yields the same key, so a point read finds the
    /// row) and non-guessable / non-sequential (AC-06 spirit) - the raw email is
    /// never the key.
    /// </summary>
    /// <param name="emailIdentity">The raw or normalized email; normalized here first.</param>
    /// <returns>The 64-char lowercase hex SHA-256 of the normalized identity.</returns>
    public static string KeyFor(string? emailIdentity)
    {
        var normalized = Normalize(emailIdentity);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(bytes);
    }
}
