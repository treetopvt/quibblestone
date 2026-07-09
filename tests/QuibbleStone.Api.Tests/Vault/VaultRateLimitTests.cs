// ----------------------------------------------------------------------------
//  VaultRateLimitTests - covers the per-IP partitioning of the anonymous vault
//  endpoints' rate limits (keepsake-vault/01, AC-06).
//
//  The framework's fixed-window enforcement is trusted; what is worth locking in
//  is the SECURITY-relevant custom bit - that BOTH the read and the write policies
//  exist (the gap this feature closes vs the write-only sibling surfaces), that the
//  limit partitions on the CALLER'S IP, and that it fails CLOSED to a shared bucket
//  when no IP is available.
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
}
