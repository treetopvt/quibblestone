// ----------------------------------------------------------------------------
//  InMemoryConsumedNonceStore - the WORKING fallback single-use nonce set used when
//  no storage connection string is configured (platform-devops/08 AC-07, local dev /
//  CI). Not a no-op: MagicLinkTokenService's single-use guarantee (a replay fails)
//  is exercisable end to end locally with ZERO Azure setup. This is the EXACT
//  ConcurrentDictionary + opportunistic-prune bookkeeping that lived inside
//  MagicLinkTokenService before the seam was extracted - moved here unchanged so
//  local behavior is byte-for-byte what it was (AC-02).
//
//  It just does not survive a process restart and is not shared across instances -
//  which is fine locally (a single laptop process) and is precisely WHY a deployed
//  environment uses the durable, shared TableStorageConsumedNonceStore instead.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A thread-safe, in-memory <see cref="IConsumedNonceStore"/> (platform-devops/08),
/// registered when no storage connection string is configured (local dev / CI). A
/// ConcurrentDictionary gives a thread-safe TryAdd that IS the single-use check;
/// entries past their expiry are pruned opportunistically so the set cannot grow
/// without bound over a long-lived process.
/// </summary>
public sealed class InMemoryConsumedNonceStore : IConsumedNonceStore
{
    // Above this many consumed nonces, opportunistically prune expired entries so
    // the single-use set cannot grow without bound over a long-lived process.
    private const int PruneThreshold = 1024;

    // Consumed nonce (jti) -> the token's expiry, so a pruned sweep can drop entries
    // that can never be replayed anyway (they are already expired). A
    // ConcurrentDictionary gives a thread-safe TryAdd that IS the single-use check.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumedNonces =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> TryConsumeAsync(string nonce, DateTimeOffset expiry, CancellationToken ct = default)
    {
        // TryAdd fails if this nonce was already consumed by an earlier successful
        // verify - that failure IS the replay check.
        var consumed = _consumedNonces.TryAdd(nonce, expiry);
        if (consumed)
        {
            PruneExpiredNoncesIfLarge();
        }
        return Task.FromResult(consumed);
    }

    private void PruneExpiredNoncesIfLarge()
    {
        if (_consumedNonces.Count < PruneThreshold)
        {
            return;
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var (nonce, expiry) in _consumedNonces)
        {
            if (now >= expiry)
            {
                _consumedNonces.TryRemove(nonce, out _);
            }
        }
    }
}
