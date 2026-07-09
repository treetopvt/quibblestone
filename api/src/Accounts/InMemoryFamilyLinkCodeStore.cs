// ----------------------------------------------------------------------------
//  InMemoryFamilyLinkCodeStore - the in-memory link-code ledger (accounts-identity/09,
//  issue #229). See IFamilyLinkCodeStore for why this is in-memory (codes live only
//  minutes; a restart harmlessly invalidates any outstanding code).
//
//  Enforces the three ephemerality rules together under a small lock so a concurrent
//  redeem-storm can violate none:
//    - SINGLE-USE (AC-02): a successful redeem removes the entry, so a second redeem of
//      the same code - immediate or later - misses.
//    - SHORT-TTL (AC-02): an entry past its ExpiresUtc is removed and misses.
//    - PER-CODE ATTEMPT BURN (ADR 0003): each redeem attempt against a KNOWN code spends
//      one of its small attempt budget; once spent, the entry is removed and every later
//      attempt misses - independent of the IP making the attempts.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A thread-safe, in-memory <see cref="IFamilyLinkCodeStore"/> (accounts-identity/09).
/// Enforces single-use, short-TTL, and the per-code attempt burn. Codes are compared
/// case-insensitively (they are shown to a parent and typed on a kid's device).
/// </summary>
public sealed class InMemoryFamilyLinkCodeStore : IFamilyLinkCodeStore
{
    /// <summary>
    /// The per-code attempt budget (ADR 0003 security posture): a code tolerates only
    /// this many redeem attempts before it burns, independent of the IP. Small, because
    /// a legitimate redeem succeeds on the first correct entry; this bounds how long a
    /// partially-guessed code can be hammered before it self-invalidates.
    /// </summary>
    public const int MaxAttemptsPerCode = 5;

    private sealed class Entry
    {
        public required Guid AccountId { get; init; }
        public required DateTimeOffset ExpiresUtc { get; init; }
        public int AttemptsRemaining { get; set; }
    }

    // Keyed by the NORMALIZED (upper-invariant) code so a case-insensitive compare is a
    // direct point read. A ConcurrentDictionary for lock-free mint; TryRedeem takes a
    // short per-entry-consistent path via TryRemove + re-add so the attempt decrement
    // and the single-use consume never race.
    private readonly ConcurrentDictionary<string, Entry> _codes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Mint(string code, Guid accountId, DateTimeOffset expiresUtc)
    {
        _codes[Normalize(code)] = new Entry
        {
            AccountId = accountId,
            ExpiresUtc = expiresUtc,
            AttemptsRemaining = MaxAttemptsPerCode,
        };
    }

    /// <inheritdoc />
    public LinkCodeRedeemResult TryRedeem(string code)
    {
        var key = Normalize(code);
        if (string.IsNullOrEmpty(key))
        {
            // An empty submission never matches a minted code - nothing to burn.
            return LinkCodeRedeemResult.Miss;
        }

        // Atomically pull the entry out. Whatever the outcome (success, expiry, burn) the
        // entry is either consumed or re-inserted with a decremented budget under this
        // same removal, so two concurrent redeems of one code cannot both succeed and the
        // attempt budget cannot be double-spent.
        if (!_codes.TryRemove(key, out var entry))
        {
            // Unknown code (never minted, already consumed, expired-and-purged, or burned).
            // There is no per-code counter to touch - an attacker enumerating the space
            // hits this branch and is bounded by the per-IP + global limiters instead.
            return LinkCodeRedeemResult.Miss;
        }

        // Expired: it is now removed (single-use / TTL both satisfied by not re-inserting).
        if (entry.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            return LinkCodeRedeemResult.Miss;
        }

        // Spend one attempt. If the budget is now exhausted, do NOT re-insert - the code
        // is burned and every later attempt misses (ADR 0003 per-code burn). We still do
        // not resolve on the burning attempt itself: a code that has been hammered to its
        // limit is treated as compromised.
        entry.AttemptsRemaining -= 1;
        if (entry.AttemptsRemaining <= 0)
        {
            return LinkCodeRedeemResult.Miss;
        }

        // Valid, unexpired, budget remaining: this is the successful single-use redeem.
        // We do NOT re-insert the entry, so the code is consumed and cannot mint a second
        // device's token (AC-02).
        return new LinkCodeRedeemResult(true, entry.AccountId);
    }

    // Normalize to a case-insensitive key: trim + upper-invariant. The link-code alphabet
    // (FamilyDeviceLinkService) is upper-case already; this defends a lower-cased type-in.
    private static string Normalize(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();
}
