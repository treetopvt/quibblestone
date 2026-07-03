// ----------------------------------------------------------------------------
//  InMemoryProcessedEventStore - the WORKING fallback idempotency ledger used when
//  no storage connection string is configured (billing-entitlements/03, local dev /
//  CI). Not a no-op: the webhook handler's idempotency (AC-05) is exercisable end to
//  end locally. A ConcurrentDictionary set keyed by event id; it just does not
//  survive a process restart (which is fine - Stripe would not redeliver across one).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// A thread-safe, in-memory <see cref="IProcessedEventStore"/> (billing-
/// entitlements/03), registered when no storage connection string is configured.
/// </summary>
public sealed class InMemoryProcessedEventStore : IProcessedEventStore
{
    // A set of processed event ids (the byte value is unused - ConcurrentDictionary
    // is the thread-safe set). TryAdd IS the "record if new" operation.
    private readonly ConcurrentDictionary<string, byte> _processed = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> HasProcessedAsync(string eventId, CancellationToken ct = default)
        => Task.FromResult(_processed.ContainsKey(eventId));

    /// <inheritdoc />
    public Task MarkProcessedAsync(string eventId, CancellationToken ct = default)
    {
        _processed.TryAdd(eventId, 0);
        return Task.CompletedTask;
    }
}
