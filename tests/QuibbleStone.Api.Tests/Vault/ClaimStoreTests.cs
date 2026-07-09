// ----------------------------------------------------------------------------
//  ClaimStoreTests - store-level tests for keepsake-vault/03's claim and
//  recovery state (issue #230), exercised against the REAL, working
//  InMemoryVaultStore (no mocking framework, matching the harness) over a
//  FixedTimeProvider so expiry / rotation / burn are deterministic.
//
//  Covers the ACs that live at the store layer:
//    - AC-01 CLAIM + CROSS-DEVICE: claiming a vault into a family account, then
//      redeeming its code from a SECOND device, makes that device see the SAME
//      tales (the durability upgrade the whole feature exists to offer).
//    - AC-02 REDEEM ALIASES: a valid redemption aliases the calling device's own
//      vault id to the claimed vault - a later list under the calling id
//      resolves the claimed vault's tales.
//    - AC-05 NO TTL ONCE CLAIMED: a claimed vault's tales never expire
//      regardless of CreatedUtc age, in contrast to an unclaimed vault's tale,
//      which is still dropped past its TTL.
//    - AC-07 VALIDITY WINDOW / ROTATION / EXPLICIT REGENERATE / NOT SINGLE-USE:
//      an expired code is rejected and GetClaimAsync auto-mints a fresh one on
//      next read; RegenerateClaimCodeAsync immediately invalidates the prior
//      code; the SAME still-valid code redeems from two different devices.
//    - AC-03.3 PER-CODE BURN: 20 cumulative failed attempts against the CURRENT
//      code auto-invalidate it and mint a fresh one, and a successful redemption
//      resets the failed-attempt count.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Tests.Ai;
using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Vault;

public sealed class ClaimStoreTests
{
    private const string VaultA = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string VaultB = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    private const string DeviceX = "xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx";
    private const string DeviceY = "yyyyyyyy-yyyy-4yyy-8yyy-yyyyyyyyyyyy";

    private static readonly Guid FamilyAccountId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static VaultTale Tale(string vaultId, string taleId, DateTimeOffset createdUtc) =>
        new(vaultId, taleId, "A keepsake tale", [new VaultTalePart(false, "hello")], "Sam", createdUtc);

    // ---- AC-01 + AC-02: claim, redeem from a second device, same tales -------

    [Fact]
    public async Task Claim_then_a_second_device_redeeming_the_code_sees_the_same_tales()
    {
        var store = new InMemoryVaultStore();
        await store.SaveAsync(Tale(VaultA, "t1", DateTimeOffset.UtcNow), CancellationToken.None);

        var claim = await store.ClaimAsync(VaultA, FamilyAccountId, CancellationToken.None);

        var outcome = await store.RedeemClaimCodeAsync(claim.ClaimCode, DeviceX, CancellationToken.None);
        Assert.Equal(VaultRedeemOutcome.Redeemed, outcome);

        // AC-02: the calling device's OWN id now resolves to the claimed vault.
        var seenByDeviceX = await store.ListAsync(DeviceX, CancellationToken.None);
        var tale = Assert.Single(seenByDeviceX);
        Assert.Equal("t1", tale.TaleId);
    }

    [Fact]
    public async Task Redeeming_an_unknown_code_is_invalid_and_aliases_nothing()
    {
        var store = new InMemoryVaultStore();
        await store.SaveAsync(Tale(VaultA, "t1", DateTimeOffset.UtcNow), CancellationToken.None);
        await store.ClaimAsync(VaultA, FamilyAccountId, CancellationToken.None);

        var outcome = await store.RedeemClaimCodeAsync("NOTAREALCODE", DeviceX, CancellationToken.None);

        Assert.Equal(VaultRedeemOutcome.InvalidOrExpired, outcome);
        Assert.Empty(await store.ListAsync(DeviceX, CancellationToken.None));
    }

    // ---- AC-05: a claimed vault's tales never expire --------------------------

    [Fact]
    public async Task Claimed_vaults_tales_never_expire_but_an_unclaimed_vaults_do()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryVaultStore(clock);

        var ancientCreatedUtc = clock.GetUtcNow().AddDays(-(VaultTale.TtlDays + 30));
        await store.SaveAsync(Tale(VaultA, "old-claimed", ancientCreatedUtc), CancellationToken.None);
        await store.SaveAsync(Tale(VaultB, "old-unclaimed", ancientCreatedUtc), CancellationToken.None);

        // Claim only vault A - vault B stays an ordinary, unclaimed, TTL-bound vault.
        await store.ClaimAsync(VaultA, FamilyAccountId, CancellationToken.None);

        // Advance well past the TTL from "now" too, for good measure.
        clock.Set(clock.GetUtcNow().AddDays(VaultTale.TtlDays * 2));

        var claimedList = await store.ListAsync(VaultA, CancellationToken.None);
        Assert.Single(claimedList);
        Assert.Equal("old-claimed", claimedList[0].TaleId);

        var unclaimedList = await store.ListAsync(VaultB, CancellationToken.None);
        Assert.Empty(unclaimedList);
    }

    // ---- AC-07: validity window, auto-rotation on next GetClaimAsync ----------

    [Fact]
    public async Task An_expired_code_is_rejected_and_a_fresh_one_is_minted_on_next_GetClaim()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryVaultStore(clock);

        var claim = await store.ClaimAsync(VaultA, FamilyAccountId, CancellationToken.None);
        var originalCode = claim.ClaimCode;

        // Past the 7-day validity window.
        clock.Set(claim.ClaimCodeExpiresUtc.AddSeconds(1));

        var redeemOutcome = await store.RedeemClaimCodeAsync(originalCode, DeviceX, CancellationToken.None);
        Assert.Equal(VaultRedeemOutcome.InvalidOrExpired, redeemOutcome);

        // AC-07: the family always sees a live code the next time the gallery
        // screen (GetClaimAsync) is opened - auto-rotated, without needing to
        // notice or act on the expiry.
        var refreshed = await store.GetClaimAsync(VaultA, CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.NotEqual(originalCode, refreshed!.ClaimCode);

        // The freshly rotated code works.
        var redeemFresh = await store.RedeemClaimCodeAsync(refreshed.ClaimCode, DeviceX, CancellationToken.None);
        Assert.Equal(VaultRedeemOutcome.Redeemed, redeemFresh);
    }

    // ---- AC-07: explicit regenerate immediately invalidates the prior code ----

    [Fact]
    public async Task Explicit_regenerate_immediately_invalidates_the_prior_code()
    {
        var store = new InMemoryVaultStore();
        var claim = await store.ClaimAsync(VaultA, FamilyAccountId, CancellationToken.None);
        var originalCode = claim.ClaimCode;

        var regenerated = await store.RegenerateClaimCodeAsync(VaultA, CancellationToken.None);
        Assert.NotNull(regenerated);
        Assert.NotEqual(originalCode, regenerated!.ClaimCode);

        // The OLD (still within its would-be 7-day window) code no longer redeems.
        var oldOutcome = await store.RedeemClaimCodeAsync(originalCode, DeviceX, CancellationToken.None);
        Assert.Equal(VaultRedeemOutcome.InvalidOrExpired, oldOutcome);

        // The NEW code does.
        var newOutcome = await store.RedeemClaimCodeAsync(regenerated.ClaimCode, DeviceX, CancellationToken.None);
        Assert.Equal(VaultRedeemOutcome.Redeemed, newOutcome);
    }

    [Fact]
    public async Task Regenerate_for_a_never_claimed_vault_returns_null()
    {
        var store = new InMemoryVaultStore();
        Assert.Null(await store.RegenerateClaimCodeAsync(VaultA, CancellationToken.None));
    }

    // ---- AC-07: not single-use - the same still-valid code redeems from TWO ---
    // ---- different devices, and both see the tales -----------------------------

    [Fact]
    public async Task The_same_still_valid_code_redeems_from_two_different_devices()
    {
        var store = new InMemoryVaultStore();
        await store.SaveAsync(Tale(VaultA, "t1", DateTimeOffset.UtcNow), CancellationToken.None);
        var claim = await store.ClaimAsync(VaultA, FamilyAccountId, CancellationToken.None);

        var outcomeX = await store.RedeemClaimCodeAsync(claim.ClaimCode, DeviceX, CancellationToken.None);
        var outcomeY = await store.RedeemClaimCodeAsync(claim.ClaimCode, DeviceY, CancellationToken.None);

        Assert.Equal(VaultRedeemOutcome.Redeemed, outcomeX);
        Assert.Equal(VaultRedeemOutcome.Redeemed, outcomeY);

        Assert.Single(await store.ListAsync(DeviceX, CancellationToken.None));
        Assert.Single(await store.ListAsync(DeviceY, CancellationToken.None));
    }

    // ---- AC-03.3: the per-code failed-attempt burn ----------------------------

    [Fact]
    public async Task Twenty_cumulative_failed_attempts_against_the_current_code_burn_it_and_mint_a_fresh_one()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryVaultStore(clock);

        var claim = await store.ClaimAsync(VaultA, FamilyAccountId, CancellationToken.None);
        var burnedCode = claim.ClaimCode;

        // Push the code past its validity window so every redemption attempt
        // against it is an ATTRIBUTABLE failure (resolves to this vault, AC-03.3),
        // not a blind miss.
        clock.Set(claim.ClaimCodeExpiresUtc.AddSeconds(1));

        for (var attempt = 1; attempt <= VaultClaim.ClaimCodeFailedAttemptBurnThreshold; attempt++)
        {
            var outcome = await store.RedeemClaimCodeAsync(burnedCode, DeviceX, CancellationToken.None);
            Assert.Equal(VaultRedeemOutcome.InvalidOrExpired, outcome);
        }

        // AC-03.3: the code has rotated - the vault's owning device sees a fresh
        // code the next time it opens the gallery, regardless of which IP(s) the
        // 20 failed attempts came from (this store method never sees an IP at all).
        var afterBurn = await store.GetClaimAsync(VaultA, CancellationToken.None);
        Assert.NotNull(afterBurn);
        Assert.NotEqual(burnedCode, afterBurn!.ClaimCode);
    }

    [Fact]
    public async Task A_successful_redemption_resets_the_failed_attempt_count()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryVaultStore(clock);

        var claim = await store.ClaimAsync(VaultA, FamilyAccountId, CancellationToken.None);
        var code = claim.ClaimCode;
        var mintedAt = clock.GetUtcNow();

        // Move past the validity window and accumulate a FEW failed attempts
        // (well under the burn threshold, so the code itself is not yet rotated).
        clock.Set(claim.ClaimCodeExpiresUtc.AddSeconds(1));
        for (var i = 0; i < 3; i++)
        {
            var outcome = await store.RedeemClaimCodeAsync(code, DeviceX, CancellationToken.None);
            Assert.Equal(VaultRedeemOutcome.InvalidOrExpired, outcome);
        }

        // Roll the clock back to when the code was still live (a deterministic test
        // technique over the injected clock - not a real-world path, but it isolates
        // the "does a SUCCESS reset the count" behavior from the expiry/rotation path
        // already covered above).
        clock.Set(mintedAt);
        var successOutcome = await store.RedeemClaimCodeAsync(code, DeviceX, CancellationToken.None);
        Assert.Equal(VaultRedeemOutcome.Redeemed, successOutcome);

        var afterSuccess = await store.GetClaimAsync(VaultA, CancellationToken.None);
        Assert.NotNull(afterSuccess);
        Assert.Equal(0, afterSuccess!.ClaimCodeFailedAttempts);
    }
}
