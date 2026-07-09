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

    // ---- keepsake-vault/04: the pure soft-delete / restore-window helpers ------

    [Fact]
    public void A_live_tale_is_not_deleted_and_never_window_elapsed()
    {
        var tale = Tale(VaultA, "t1", DateTimeOffset.UtcNow);
        Assert.False(tale.IsDeleted);
        Assert.Null(tale.DeletedUtc);
        // A live tale has no restore window to elapse (nothing to purge).
        Assert.False(tale.IsRestoreWindowElapsed(DateTimeOffset.UtcNow.AddYears(1)));
    }

    [Fact]
    public void A_soft_deleted_tale_is_recoverable_until_the_window_elapses()
    {
        // AC-02/AC-03: the restore window is DeletedUtc + RestoreWindowDays, inclusive.
        var deleted = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tale = Tale(VaultA, "t1", deleted.AddDays(-1)) with { DeletedUtc = deleted };

        Assert.True(tale.IsDeleted);
        // One day short of the window: still recoverable.
        Assert.False(tale.IsRestoreWindowElapsed(deleted.AddDays(VaultTale.RestoreWindowDays - 1)));
        // Exactly at the window: elapsed (<=), eligible for hard removal.
        Assert.True(tale.IsRestoreWindowElapsed(deleted.AddDays(VaultTale.RestoreWindowDays)));
        // Well past: elapsed.
        Assert.True(tale.IsRestoreWindowElapsed(deleted.AddDays(VaultTale.RestoreWindowDays + 5)));
    }

    // ---- AC-01: soft-delete removes a tale from the listing (row retained) -----

    [Fact]
    public async Task SoftDeleteAsync_drops_the_tale_from_the_listing_but_it_stays_restorable()
    {
        var store = new InMemoryVaultStore();
        await store.SaveAsync(Tale(VaultA, "keep", DateTimeOffset.UtcNow), CancellationToken.None);
        await store.SaveAsync(Tale(VaultA, "gone", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(await store.SoftDeleteAsync(VaultA, "gone", CancellationToken.None));

        // AC-01: the soft-deleted tale is gone from the listing immediately.
        var live = await store.ListAsync(VaultA, CancellationToken.None);
        var only = Assert.Single(live);
        Assert.Equal("keep", only.TaleId);

        // But the underlying row still exists - a restore brings it right back (AC-02).
        var restored = await store.RestoreAsync(VaultA, "gone", CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(2, (await store.ListAsync(VaultA, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task SoftDeleteAsync_is_idempotent_and_false_for_an_unknown_tale()
    {
        var store = new InMemoryVaultStore();
        await store.SaveAsync(Tale(VaultA, "t1", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(await store.SoftDeleteAsync(VaultA, "t1", CancellationToken.None));
        // A second soft-delete of the same tale is an idempotent no-op success.
        Assert.True(await store.SoftDeleteAsync(VaultA, "t1", CancellationToken.None));
        // An unknown tale id (or vault) has nothing to delete.
        Assert.False(await store.SoftDeleteAsync(VaultA, "never", CancellationToken.None));
        Assert.False(await store.SoftDeleteAsync("no-such-vault-000000000000000000000", "t1", CancellationToken.None));
    }

    // ---- AC-02 + AC-06: restore returns the original content, unchanged --------

    [Fact]
    public async Task RestoreAsync_returns_the_original_content_byte_for_byte()
    {
        var store = new InMemoryVaultStore();
        var original = new VaultTale(
            VaultA, "t1", "The saga",
            [new VaultTalePart(false, "Once a "), new VaultTalePart(true, "banana"), new VaultTalePart(false, " danced")],
            "Sam & Mia", DateTimeOffset.UtcNow);
        await store.SaveAsync(original, CancellationToken.None);
        await store.SoftDeleteAsync(VaultA, "t1", CancellationToken.None);

        var restored = await store.RestoreAsync(VaultA, "t1", CancellationToken.None);

        Assert.NotNull(restored);
        // AC-06: title / parts / byline are identical to the pre-deletion content.
        Assert.Equal(original.Title, restored!.Title);
        Assert.Equal(original.BylineNames, restored.BylineNames);
        Assert.Equal(original.CreatedUtc, restored.CreatedUtc);
        Assert.Equal(original.Parts, restored.Parts);
        // The marker is cleared so it is live again.
        Assert.False(restored.IsDeleted);
    }

    [Fact]
    public async Task RestoreAsync_of_a_live_tale_is_a_harmless_no_op()
    {
        var store = new InMemoryVaultStore();
        await store.SaveAsync(Tale(VaultA, "t1", DateTimeOffset.UtcNow), CancellationToken.None);

        var restored = await store.RestoreAsync(VaultA, "t1", CancellationToken.None);
        Assert.NotNull(restored);
        Assert.False(restored!.IsDeleted);
        Assert.Single(await store.ListAsync(VaultA, CancellationToken.None));
    }

    // ---- AC-03: past the restore window, a soft-deleted tale is gone -----------

    [Fact]
    public async Task A_soft_deleted_tale_past_its_window_reads_as_gone_and_is_not_restorable()
    {
        // Save a tale, then plant it in a past-window soft-deleted state directly (the
        // in-memory store re-saves the same key, overwriting). DeletedUtc is well past
        // the restore window, but CreatedUtc is recent so TTL is NOT the cause.
        var store = new InMemoryVaultStore();
        var created = DateTimeOffset.UtcNow;
        await store.SaveAsync(
            Tale(VaultA, "t1", created) with { DeletedUtc = DateTimeOffset.UtcNow.AddDays(-(VaultTale.RestoreWindowDays + 1)) },
            CancellationToken.None);

        // AC-03: the next list reads it as gone (lazy purge-on-read).
        Assert.Empty(await store.ListAsync(VaultA, CancellationToken.None));
        // And a restore refuses - a lapsed soft-delete is genuinely gone (out of scope).
        Assert.Null(await store.RestoreAsync(VaultA, "t1", CancellationToken.None));
        Assert.False(await store.SoftDeleteAsync(VaultA, "t1", CancellationToken.None));
    }
}
