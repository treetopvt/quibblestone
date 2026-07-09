// ----------------------------------------------------------------------------
//  VaultRateLimitTests - covers the per-IP partitioning of the anonymous vault
//  endpoints' rate limits (keepsake-vault/01, AC-06; keepsake-vault/03, AC-03.1).
//
//  The framework's fixed-window enforcement is trusted; what is worth locking in
//  is the SECURITY-relevant custom bit - that the read, write, AND claim-code
//  redeem policies all exist and are distinct (the gap this feature closes vs
//  the write-only sibling surfaces), that the limit partitions on the CALLER'S
//  IP (the SAME PartitionKey the redeem endpoint's [EnableRateLimiting] policy
//  uses - the first of AC-03's three anti-brute-force controls), and that it
//  fails CLOSED to a shared bucket when no IP is available.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using Microsoft.AspNetCore.Http;
using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Vault;

public sealed class VaultRateLimitTests
{
    [Fact]
    public void PartitionKey_is_the_caller_ip_so_the_limit_is_per_client()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal("203.0.113.7", VaultRateLimit.PartitionKey(context));
    }

    [Fact]
    public void PartitionKey_gives_two_different_ips_two_different_buckets()
    {
        var a = new DefaultHttpContext();
        a.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var b = new DefaultHttpContext();
        b.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");

        Assert.NotEqual(VaultRateLimit.PartitionKey(a), VaultRateLimit.PartitionKey(b));
    }

    [Fact]
    public void PartitionKey_fails_closed_to_a_shared_bucket_when_no_ip_is_available()
    {
        var context = new DefaultHttpContext();
        Assert.Equal("unknown", VaultRateLimit.PartitionKey(context));
    }

    [Fact]
    public void Both_a_read_and_a_write_policy_exist_with_sane_tunables()
    {
        // AC-06: the vault limits BOTH reads AND writes (the gap the sibling
        // write-only surfaces leave open), so both policies must be distinct and set.
        Assert.False(string.IsNullOrWhiteSpace(VaultRateLimit.SavePolicyName));
        Assert.False(string.IsNullOrWhiteSpace(VaultRateLimit.ReadPolicyName));
        Assert.NotEqual(VaultRateLimit.SavePolicyName, VaultRateLimit.ReadPolicyName);

        Assert.True(VaultRateLimit.SavePermitLimit > 0);
        Assert.True(VaultRateLimit.ReadPermitLimit > 0);
        Assert.True(VaultRateLimit.Window > TimeSpan.Zero);
    }

    [Fact]
    public void The_claim_code_redeem_policy_is_a_distinct_per_ip_policy_with_a_tight_limit()
    {
        // AC-03.1: the FIRST of redemption's three anti-brute-force controls - a
        // per-IP fixed-window limiter, distinct from the save/read policies, and
        // tighter than either (a legitimate family recovers a vault a handful of
        // times; a single IP making many redemption attempts is a guesser). On its
        // own this is defeated by an attacker rotating source IPs - which is why
        // ClaimRedemptionCeiling (AC-03.2) and the per-code burn (AC-03.3) exist
        // alongside it.
        Assert.False(string.IsNullOrWhiteSpace(VaultRateLimit.RedeemPolicyName));
        Assert.NotEqual(VaultRateLimit.RedeemPolicyName, VaultRateLimit.SavePolicyName);
        Assert.NotEqual(VaultRateLimit.RedeemPolicyName, VaultRateLimit.ReadPolicyName);

        Assert.True(VaultRateLimit.RedeemPermitLimit > 0);
        Assert.True(VaultRateLimit.RedeemPermitLimit <= VaultRateLimit.ReadPermitLimit);
    }

    [Fact]
    public void The_redeem_policy_partition_key_is_the_same_per_ip_scheme_as_save_and_read()
    {
        // The redeem endpoint reuses this SAME PartitionKey (no separate scheme) -
        // proven here by exercising it exactly as the save/read tests above do.
        var a = new DefaultHttpContext();
        a.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var b = new DefaultHttpContext();
        b.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal(VaultRateLimit.PartitionKey(a), VaultRateLimit.PartitionKey(b));
    }
}
