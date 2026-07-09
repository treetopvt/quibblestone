// ----------------------------------------------------------------------------
//  ClaimRedemptionCeilingTests - unit tests for the GLOBAL, IP-agnostic
//  redemption ceiling on the vault claim-code redeem endpoint (keepsake-vault/03,
//  AC-03.2, issue #230).
//
//  This is the second of the three anti-brute-force controls AC-03 requires: a
//  single shared budget across ALL callers, so it bounds a distributed,
//  IP-rotating brute force the per-IP limiter (VaultRateLimit.RedeemPolicyName,
//  covered in VaultRateLimitTests) cannot. Exercised directly (no middleware, no
//  HTTP) against a FixedTimeProvider so the fixed-window rollover is
//  deterministic:
//    - admits up to GlobalRedemptionCeiling attempts, then rejects within the
//      SAME window regardless of who is calling;
//    - admits again once the window rolls over;
//    - is IP-agnostic BY CONSTRUCTION - TryAcquire() takes no caller identity at
//      all, so there is nothing for a rotated IP to reset.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Tests.Ai;
using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Vault;

public sealed class ClaimRedemptionCeilingTests
{
    [Fact]
    public void TryAcquire_admits_up_to_the_ceiling_then_rejects_within_the_same_window()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var ceiling = new ClaimRedemptionCeiling(clock);

        for (var i = 0; i < ClaimRedemptionCeiling.GlobalRedemptionCeiling; i++)
        {
            Assert.True(ceiling.TryAcquire(), $"attempt {i} should still be within budget");
        }

        // The ceiling+1'th attempt in the SAME window is rejected - regardless of
        // which (if any) caller identity it came from, since none is consulted.
        Assert.False(ceiling.TryAcquire());
        Assert.False(ceiling.TryAcquire());
    }

    [Fact]
    public void TryAcquire_admits_again_once_the_fixed_window_rolls_over()
    {
        var start = DateTimeOffset.UtcNow;
        var clock = new FixedTimeProvider(start);
        var ceiling = new ClaimRedemptionCeiling(clock);

        for (var i = 0; i < ClaimRedemptionCeiling.GlobalRedemptionCeiling; i++)
        {
            Assert.True(ceiling.TryAcquire());
        }
        Assert.False(ceiling.TryAcquire());

        // Advance the clock past the window: a fresh window resets the shared count.
        clock.Set(start + ClaimRedemptionCeiling.Window + TimeSpan.FromSeconds(1));
        Assert.True(ceiling.TryAcquire());
    }

    [Fact]
    public void TryAcquire_is_ip_agnostic_by_construction_it_takes_no_caller_identity()
    {
        // AC-03.2: the global ceiling reads NOTHING about the caller (not an IP, an
        // account, a vault id, or the code) - proven structurally, not just by
        // behavior: the method literally has no parameter a caller identity could
        // flow through.
        var method = typeof(ClaimRedemptionCeiling).GetMethod(nameof(ClaimRedemptionCeiling.TryAcquire));
        Assert.NotNull(method);
        Assert.Empty(method!.GetParameters());
    }

    [Fact]
    public void The_ceiling_and_window_constants_are_sane()
    {
        Assert.True(ClaimRedemptionCeiling.GlobalRedemptionCeiling > 0);
        Assert.True(ClaimRedemptionCeiling.Window > TimeSpan.Zero);
    }
}
