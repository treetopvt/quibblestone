// ----------------------------------------------------------------------------
//  IFamilyLinkCodeStore - the short-lived link-code ledger (accounts-identity/09,
//  issue #229). A link code is the human-enterable handoff the parent reads off the
//  Account page and types into the kid's device to redeem a long-lived device token.
//
//  A link code is DELIBERATELY EPHEMERAL (minutes, mirroring a magic link's lifetime):
//    - SINGLE-USE: once redeemed it is consumed and cannot mint a second device's
//      token, whether reused immediately or after expiry (AC-02).
//    - SHORT-TTL: an unredeemed code expires after its short validity window (AC-02).
//    - PER-CODE ATTEMPT BURN (ADR 0003 security posture): a code is invalidated after
//      a small number of redeem attempts against it, INDEPENDENT of the IP making them
//      - the "enumerable codes get a per-code attempt burn" layer the per-IP + global
//      limiters (Program.cs) cannot provide on their own.
//
//  Because a code lives only for minutes, the ledger is an IN-MEMORY singleton (like
//  the room registry and the AI quota) rather than durable storage - a process restart
//  simply invalidates any outstanding code, and the parent mints a fresh one. The code
//  value itself is minted by FamilyDeviceLinkService via a CSPRNG (AC-01); this ledger
//  only remembers "code -> AccountId, until, attempts-left".
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The outcome of a link-code redeem lookup: either the code resolved to a family
/// account (and was consumed, single-use), or it did not (unknown, expired, already
/// used, or burned). Carries the resolved <see cref="AccountId"/> only on success - a
/// failure carries none, so a caller can never learn anything about a code that did
/// not resolve.
/// </summary>
/// <param name="Success">True when the code resolved and was consumed this call.</param>
/// <param name="AccountId">The family account the code resolved to, on success only.</param>
public readonly record struct LinkCodeRedeemResult(bool Success, Guid AccountId)
{
    /// <summary>The shared "did not resolve" result - unknown / expired / used / burned.</summary>
    public static readonly LinkCodeRedeemResult Miss = new(false, Guid.Empty);
}

/// <summary>
/// Remembers freshly minted link codes until they are redeemed, expire, or burn
/// (accounts-identity/09). An in-memory singleton because codes live only minutes.
/// </summary>
public interface IFamilyLinkCodeStore
{
    /// <summary>
    /// Records a freshly minted code for <paramref name="accountId"/>, valid until
    /// <paramref name="expiresUtc"/> (AC-01/AC-02). The code string is server-minted via
    /// a CSPRNG by the caller - this ledger only stores the mapping + its short window.
    /// </summary>
    void Mint(string code, Guid accountId, DateTimeOffset expiresUtc);

    /// <summary>
    /// Attempts to redeem <paramref name="code"/> (AC-02): on a valid, unexpired,
    /// non-burned code it CONSUMES the code (single-use) and returns the resolved
    /// account; otherwise returns <see cref="LinkCodeRedeemResult.Miss"/>. Each attempt
    /// against a KNOWN code counts toward its per-code burn budget; once the budget is
    /// spent the code is invalidated regardless of the IP making the attempts (ADR 0003
    /// security posture). Comparison is case-insensitive (codes are shown/typed).
    /// </summary>
    LinkCodeRedeemResult TryRedeem(string code);
}
