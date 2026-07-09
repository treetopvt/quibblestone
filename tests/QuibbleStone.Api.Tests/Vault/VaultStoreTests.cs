// ----------------------------------------------------------------------------
//  VaultStoreTests - store + TTL tests for keepsake-vault/01 (issue #196).
//
//  Covers the parts of the vault store that carry the story's non-obvious
//  guarantees, using the working InMemoryVaultStore (no Azure) and the pure
//  VaultTale.IsExpired helper:
//    - AC-03 TTL (pure): VaultTale.IsExpired computes CreatedUtc + TtlDays and
//      returns expired only at/past the cutoff - given a CreatedUtc and a now.
//    - AC-03 LIST: ListAsync omits tales past their computed expiry (and reclaims
//      them) while keeping live ones.
//    - AC-05 ROUND-TRIP: a save -> list round-trips with zero config.
//    - AC-07 CAP: a save past MaxTalesPerVault for one vault is rejected while a
//      different vault (under the cap) still saves.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Vault;

public class VaultStoreTests
{
    private const string VaultA = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string VaultB = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

    private static VaultTale Tale(string vaultId, string taleId, DateTimeOffset createdUtc) =>
        new(vaultId, taleId, "A tale", [new VaultTalePart(false, "hello")], "Sam", createdUtc);

    // ---- AC-03: the pure TTL-expiry helper ------------------------------------

    [Fact]
    public void IsExpired_is_false_before_the_ttl_and_true_at_or_past_it()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tale = Tale(VaultA, "t1", created);

        // One day short of the TTL: still live.
        Assert.False(tale.IsExpired(created.AddDays(VaultTale.TtlDays - 1)));
        // Exactly at the TTL: expired (<=).
        Assert.True(tale.IsExpired(created.AddDays(VaultTale.TtlDays)));
        // Well past: expired.
        Assert.True(tale.IsExpired(created.AddDays(VaultTale.TtlDays + 30)));
    }

    // ---- AC-03: ListAsync omits expired rows ----------------------------------

    [Fact]
    public async Task ListAsync_omits_expired_tales_and_keeps_live_ones()
    {
        var store = new InMemoryVaultStore();
        // A fresh tale (now) and an expired one (created well past the TTL ago).
        await store.SaveAsync(Tale(VaultA, "fresh", DateTimeOffset.UtcNow), CancellationToken.None);
        await store.SaveAsync(Tale(VaultA, "stale", DateTimeOffset.UtcNow.AddDays(-(VaultTale.TtlDays + 1))), CancellationToken.None);

        var live = await store.ListAsync(VaultA, CancellationToken.None);

        var only = Assert.Single(live);
        Assert.Equal("fresh", only.TaleId);
    }

    // ---- AC-05: save -> list round-trip with zero config ----------------------

    [Fact]
    public async Task Save_then_list_round_trips_in_memory()
    {
        var store = new InMemoryVaultStore();
        var outcome = await store.SaveAsync(Tale(VaultA, "t1", DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.Equal(VaultSaveOutcome.Saved, outcome);

        var listed = Assert.Single(await store.ListAsync(VaultA, CancellationToken.None));
        Assert.Equal("t1", listed.TaleId);
        Assert.Equal("A tale", listed.Title);
    }

    [Fact]
    public async Task ListAsync_for_an_unknown_vault_is_empty_not_an_error()
    {
        var store = new InMemoryVaultStore();
        Assert.Empty(await store.ListAsync("never-saved-vault-id-000000000000000", CancellationToken.None));
    }

    // ---- AC-07: the per-vault cap ---------------------------------------------

    [Fact]
    public async Task SaveAsync_rejects_past_the_cap_for_one_vault_but_not_another()
    {
        var store = new InMemoryVaultStore();
        for (var i = 0; i < IVaultStore.MaxTalesPerVault; i++)
        {
            Assert.Equal(VaultSaveOutcome.Saved,
                await store.SaveAsync(Tale(VaultA, $"t{i}", DateTimeOffset.UtcNow), CancellationToken.None));
        }

        // One more for the SAME vault is rejected.
        Assert.Equal(VaultSaveOutcome.RejectedCapExceeded,
            await store.SaveAsync(Tale(VaultA, "over", DateTimeOffset.UtcNow), CancellationToken.None));

        // A DIFFERENT vault (its own partition) is unaffected.
        Assert.Equal(VaultSaveOutcome.Saved,
            await store.SaveAsync(Tale(VaultB, "b1", DateTimeOffset.UtcNow), CancellationToken.None));
    }
}
