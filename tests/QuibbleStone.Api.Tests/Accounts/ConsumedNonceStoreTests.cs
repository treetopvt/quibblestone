// ----------------------------------------------------------------------------
//  ConsumedNonceStoreTests - unit tests for InMemoryConsumedNonceStore, the WORKING
//  local-dev / CI fallback of the single-use magic-link nonce seam
//  (platform-devops/08 AC-07).
//
//  These pin the store's actual contract (IConsumedNonceStore.TryConsumeAsync):
//  "record-if-new IS the single-use check" - first use returns true, a replay of
//  the SAME nonce returns false, and two DISTINCT nonces are tracked independently.
//
//  The token-level expiry check (is this token past its expiry?) lives in
//  MagicLinkTokenService, not in the store - the store only ever records "this
//  nonce has been used", regardless of the expiry it was handed (the expiry is
//  carried purely for the store's own opportunistic-prune housekeeping). So a
//  consume call with an already-past expiry still succeeds on first use here,
//  which is the store's real contract, not a bug.
//
//  TableStorageConsumedNonceStore (the durable, deployed-environment
//  implementation) is deliberately NOT covered here - it talks to Azure Table
//  Storage and there is no emulator in CI. It is validated manually / at deploy,
//  per the story's Tests table (platform-devops/08).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Tests.Accounts;

public class ConsumedNonceStoreTests
{
    [Fact]
    public async Task TryConsumeAsync_FirstUseOfANonce_ReturnsTrue()
    {
        var store = new InMemoryConsumedNonceStore();

        var consumed = await store.TryConsumeAsync("nonce-1", DateTimeOffset.UtcNow.AddMinutes(15));

        Assert.True(consumed);
    }

    [Fact]
    public async Task TryConsumeAsync_SecondCallWithSameNonce_ReturnsFalse_Replay()
    {
        // AC-07's single-use guarantee: the second consume of the SAME nonce must be
        // rejected - this IS the replay defence for a magic-link token.
        var store = new InMemoryConsumedNonceStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(15);

        var first = await store.TryConsumeAsync("nonce-1", expiry);
        var replay = await store.TryConsumeAsync("nonce-1", expiry);

        Assert.True(first);
        Assert.False(replay);
    }

    [Fact]
    public async Task TryConsumeAsync_TwoDistinctNonces_BothConsumeTrue_Independently()
    {
        var store = new InMemoryConsumedNonceStore();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(15);

        var a = await store.TryConsumeAsync("nonce-a", expiry);
        var b = await store.TryConsumeAsync("nonce-b", expiry);

        Assert.True(a);
        Assert.True(b);

        // Each remains individually single-use - consuming one does not affect the
        // other's first-use eligibility, and each still rejects its own replay.
        Assert.False(await store.TryConsumeAsync("nonce-a", expiry));
        Assert.False(await store.TryConsumeAsync("nonce-b", expiry));
    }

    [Fact]
    public async Task TryConsumeAsync_AlreadyPastExpiry_StillConsumesOnFirstUse()
    {
        // The store's contract is single-use bookkeeping only; whether a TOKEN is
        // still within its validity window is MagicLinkTokenService's job (checked
        // before the nonce is ever consumed). A past-expiry value handed to the store
        // is just data for the opportunistic prune, so first use still succeeds here.
        var store = new InMemoryConsumedNonceStore();
        var pastExpiry = DateTimeOffset.UtcNow.AddMinutes(-5);

        var consumed = await store.TryConsumeAsync("nonce-expired", pastExpiry);
        var replay = await store.TryConsumeAsync("nonce-expired", pastExpiry);

        Assert.True(consumed);
        Assert.False(replay);
    }
}
