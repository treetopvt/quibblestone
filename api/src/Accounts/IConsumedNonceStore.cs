// ----------------------------------------------------------------------------
//  IConsumedNonceStore - the single-use bookkeeping seam for magic-link tokens
//  (platform-devops/08 AC-07). It records "this nonce has been used" and answers
//  "was it already used?" in ONE atomic step (TryConsumeAsync), so the second
//  verify of a single-use magic-link token fails.
//
//  WHY IT EXISTS (the scale-out replay gap this closes): before this seam,
//  MagicLinkTokenService kept the consumed-nonce set in a per-process
//  ConcurrentDictionary. That is safe on ONE instance, but the moment
//  platform-devops/08 makes the SIGNING KEY durable and shared across instances,
//  a token that verifies on instance A - whose nonce-consumption lives only in A's
//  memory - can be replayed once per OTHER instance behind the load balancer. A
//  single-use token would no longer be single-use under scale-out, for BOTH
//  purchaser sign-in and operator login (both ride MagicLinkTokenService). Moving
//  the consumed-nonce set to the SAME durable, SHARED store as the key ring closes
//  that gap: a token consumed on one instance is recognized as consumed by every
//  other instance.
//
//  CONFIG-PRESENCE SPLIT (mirrors every other durable-vs-local store here): a
//  durable Azure Table Storage implementation (TableStorageConsumedNonceStore) when
//  a storage connection string is configured (a deployed environment), and a
//  working in-memory implementation (InMemoryConsumedNonceStore) when it is not
//  (local dev / CI) - so single-use is still exercisable end to end with ZERO Azure
//  setup, exactly as it was before this story (AC-02's Development posture).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Records and checks consumed magic-link token nonces (jti) for single-use
/// enforcement (platform-devops/08 AC-07). One implementation writes to Azure Table
/// Storage (a deployed environment, shared across instances); the other is a
/// working in-memory set (local dev / CI). A nonce carries its token's expiry so a
/// consumed entry that can never be replayed again (it is past expiry) can be
/// pruned.
/// </summary>
public interface IConsumedNonceStore
{
    /// <summary>
    /// Atomically records <paramref name="nonce"/> as consumed and reports whether
    /// THIS call is the one that consumed it. Returns true when the nonce was not
    /// previously present (the token's FIRST use - allow it); returns false when the
    /// nonce was already consumed (a replay - reject it). This "record-if-new" IS the
    /// single-use check, so callers must treat a false as "already used".
    /// </summary>
    /// <param name="nonce">The token's unique nonce (jti).</param>
    /// <param name="expiry">The token's expiry, stored so a past-expiry entry can be pruned.</param>
    /// <param name="ct">Cancellation for the (storage-bound) operation.</param>
    /// <returns>True if the nonce was newly consumed (first use); false if it was already consumed (replay).</returns>
    Task<bool> TryConsumeAsync(string nonce, DateTimeOffset expiry, CancellationToken ct = default);
}
