// ----------------------------------------------------------------------------
//  IMagicLinkTokenService - a REUSABLE, one-time, signed-token issuer / verifier
//  (accounts-identity/02, issue #68).
//
//  WHAT IT PROVES (and, load-bearing, what it does NOT):
//  This service mints and validates opaque single-use tokens that prove ONE thing:
//  "the holder controls subject X". The subject is an OPAQUE string - it is an
//  email for a purchaser magic link (accounts-identity/03), and it will be an
//  operator id for the admin console's operator login (sysadmin-console/01), which
//  reuses this EXACT service against a SEPARATE allowlist. Because the subject is
//  opaque, this service bakes in NO purchaser and NO admin semantics: it never
//  says "X is a purchaser" or "X is an admin", only "the holder controls X". That
//  keeps `purchaser == admin` STRUCTURALLY impossible - the two callers layer
//  their own authorization (which allowlist / store the subject belongs to) on top
//  of the SAME proof-of-control primitive, and neither can be mistaken for the
//  other here.
//
//  This is why it lives as a standalone contract, not a method on IAccountStore:
//  the account store is purchaser-only, but the token service is identity-neutral
//  plumbing shared across features.
//
//  SECURITY POSTURE (AC-06):
//    - Tokens are signed with HMAC-SHA256 under a key from configuration
//      (Accounts:TokenSigningKey, Key Vault-backed in a deployed environment,
//      NEVER a committed literal and NEVER a VITE_* var). Absent (local dev / CI),
//      a random ephemeral key is generated per process - tokens work within the
//      process lifetime, which is all a toy needs (README section 4). A deployed
//      environment gets a DURABLE, CSPRNG-generated key auto-provisioned into Key
//      Vault (platform-devops/08 AC-04), so a delivered link survives a recycle /
//      scale-out.
//    - Verification is CONSTANT TIME, checks expiry, and enforces SINGLE USE (the
//      token's nonce is consumed on first successful verify; a replay fails). The
//      consumed-nonce set lives in an IConsumedNonceStore - a durable, SHARED store
//      in a deployed environment (platform-devops/08 AC-07) so single use holds
//      across every instance, and an in-memory set locally. Verification is async
//      because consuming the nonce is a (possibly storage-bound) call.
//    - The token and the signing key are NEVER logged or persisted in plaintext.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Issues and verifies opaque, single-use, HMAC-signed tokens that prove the
/// holder controls an opaque <c>subject</c> (accounts-identity/02). Deliberately
/// identity-neutral: the subject is an email for purchaser magic links and an
/// operator id for the admin console (sysadmin-console/01 reuses this same
/// service), so NO purchaser / admin meaning is baked in and `purchaser == admin`
/// stays structurally impossible - each caller authorizes the subject against its
/// own allowlist. Never logs or stores the token or the signing key (AC-06).
/// </summary>
public interface IMagicLinkTokenService
{
    /// <summary>
    /// Mints an opaque token binding <paramref name="subject"/> to an expiry and a
    /// unique single-use nonce, signed with HMAC-SHA256. The subject is OPAQUE (an
    /// email, an operator id - this service never interprets it). Safe to place in
    /// a magic-link URL: it carries no secret (the signing key stays server-side)
    /// and cannot be tampered with undetectably.
    /// </summary>
    /// <param name="subject">The opaque subject the holder is proving control of (e.g. an email).</param>
    /// <param name="lifetime">How long the token stays valid; a sensible default is used when null.</param>
    /// <returns>The signed, single-use token string.</returns>
    string Issue(string subject, TimeSpan? lifetime = null);

    /// <summary>
    /// Validates a token in CONSTANT TIME, checks its expiry, and enforces SINGLE
    /// USE (consumes the nonce in the shared store - a second verify of the same
    /// token returns a failed result). Never throws on ANY tampering, garbage,
    /// expiry, or replay - it returns <see cref="TokenVerification.Failure"/> in
    /// those cases and <see cref="TokenVerification"/> carrying the bound subject on
    /// success. Async because consuming the nonce may be a storage-bound call.
    /// </summary>
    /// <param name="token">The token to verify (as received from a magic link).</param>
    /// <param name="ct">Cancellation for the (possibly storage-bound) nonce consume.</param>
    /// <returns>A success result carrying the opaque subject, or a failure result.</returns>
    Task<TokenVerification> TryVerifyAsync(string token, CancellationToken ct = default);
}

/// <summary>
/// The outcome of <see cref="IMagicLinkTokenService.TryVerifyAsync"/>: whether the
/// token was authentic, unexpired, and being used for the first time, and (on
/// success) the opaque subject it was issued for. On failure <see cref="Subject"/>
/// is the empty string, never null.
/// </summary>
/// <param name="Succeeded">True if the token verified and was consumed for the first time.</param>
/// <param name="Subject">The opaque subject on success; the empty string on failure.</param>
public readonly record struct TokenVerification(bool Succeeded, string Subject)
{
    /// <summary>A failed verification (bad signature, garbage, expired, or replayed).</summary>
    public static TokenVerification Failure { get; } = new(false, string.Empty);

    /// <summary>A successful verification carrying the recovered opaque subject.</summary>
    public static TokenVerification Success(string subject) => new(true, subject);
}
