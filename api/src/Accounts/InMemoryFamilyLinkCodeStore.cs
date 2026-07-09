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

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A thread-safe, in-memory <see cref="IFamilyLinkCodeStore"/> (accounts-identity/09).
/// Enforces single-use, short-TTL, and the per-code attempt burn together under one lock.
/// Codes are compared case-insensitively (they are shown to a parent and typed on a kid's
/// device).
/// </summary>
public sealed class InMemoryFamilyLinkCodeStore : IFamilyLinkCodeStore
{
    /// <summary>
    /// The per-code attempt budget (ADR 0003 security posture): a specific code tolerates
    /// only this many redeem PRESENTATIONS before it burns (is purged), independent of the
    /// IP making them. Small, because a legitimate redeem succeeds on the first correct
    /// entry; this bounds how long a KNOWN code can be hammered / probed (a race, a replay
    /// after consumption, or a shoulder-surfed code being retried) before it self-destructs.
    /// </summary>
    public const int MaxAttemptsPerCode = 5;

    private sealed class Entry
    {
        public required Guid AccountId { get; init; }
        public required DateTimeOffset ExpiresUtc { get; init; }
        // Remaining PRESENTATIONS before the code burns. Every TryRedeem that matches this
        // code (success OR a post-consume replay) spends one; at zero the entry is purged.
        public int AttemptsRemaining { get; set; }
        // Single-use: flipped true by the FIRST successful redeem so every later
        // presentation of the SAME code misses (it cannot mint a second device's token).
        public bool Consumed { get; set; }
    }

    // Keyed by the NORMALIZED (upper-invariant) code. A single lock guards the whole
    // read-decrement-decide-write sequence so the attempt spend, the single-use consume,
    // and the burn purge never race across concurrent redeems of the same code.
    private readonly Dictionary<string, Entry> _codes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <inheritdoc />
    public void Mint(string code, Guid accountId, DateTimeOffset expiresUtc)
    {
        var key = Normalize(code);
        lock (_gate)
        {
            _codes[key] = new Entry
            {
                AccountId = accountId,
                ExpiresUtc = expiresUtc,
                AttemptsRemaining = MaxAttemptsPerCode,
                Consumed = false,
            };
        }
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

        lock (_gate)
        {
            if (!_codes.TryGetValue(key, out var entry))
            {
                // Unknown code (never minted, already burned, or expired-and-purged). An
                // attacker enumerating the code SPACE only ever hits this branch (each guess
                // is a different string with no entry) and is bounded by the per-IP + global
                // limiters, not this per-code counter.
                return LinkCodeRedeemResult.Miss;
            }

            // Expired: purge and miss (TTL, AC-02). Do this BEFORE spending an attempt so an
            // expired code reads the same whether or not it still had budget.
            if (entry.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                _codes.Remove(key);
                return LinkCodeRedeemResult.Miss;
            }

            // Spend one presentation against THIS code (ADR 0003 per-code burn). When the
            // budget is exhausted, PURGE the entry - the code is burned and every later
            // presentation falls to the unknown-code miss above. This is what makes a KNOWN
            // code un-hammerable: even a valid-but-already-consumed code is gone after a few
            // retries, so it cannot be probed or raced indefinitely.
            entry.AttemptsRemaining -= 1;
            if (entry.AttemptsRemaining <= 0)
            {
                _codes.Remove(key);
                return LinkCodeRedeemResult.Miss;
            }

            // Single-use (AC-02): a code that has already minted a device token misses on
            // every later presentation - but the entry is RETAINED (its budget still ticks
            // down above) so replays are counted toward the burn rather than being free.
            if (entry.Consumed)
            {
                return LinkCodeRedeemResult.Miss;
            }

            // Valid, unexpired, budget remaining, not yet consumed: the successful single-use
            // redeem. Mark it consumed (so replays miss) and keep the entry so those replays
            // are burn-counted. It cannot mint a second device's token (AC-02).
            entry.Consumed = true;
            return new LinkCodeRedeemResult(true, entry.AccountId);
        }
    }

    // Normalize to a case-insensitive key: trim + upper-invariant. The link-code alphabet
    // (FamilyDeviceLinkService) is upper-case already; this defends a lower-cased type-in.
    private static string Normalize(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();
}
