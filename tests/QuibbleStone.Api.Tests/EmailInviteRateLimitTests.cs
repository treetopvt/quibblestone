// ----------------------------------------------------------------------------
//  EmailInviteRateLimitTests - covers the per-IP partitioning of the email-invite
//  endpoint's rate limit (session-engine/12, issue #180). Mirrors
//  PublishTalesRateLimitTests / SignInRateLimit's coverage.
//
//  The framework's fixed-window enforcement is trusted; what is worth locking in is
//  the SECURITY-relevant custom bit - that the limit partitions on the CALLER'S IP (so
//  one abuser cannot exhaust the allowance for everyone) and fails CLOSED to a shared
//  bucket (bounded, not unlimited) when no IP is available.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using Microsoft.AspNetCore.Http;
using QuibbleStone.Api.Invite;

namespace QuibbleStone.Api.Tests;

public sealed class EmailInviteRateLimitTests
{
    [Fact]
    public void PartitionKey_is_the_caller_ip_so_the_limit_is_per_client()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal("203.0.113.7", EmailInviteRateLimit.PartitionKey(context));
    }

    [Fact]
    public void PartitionKey_gives_two_different_ips_two_different_buckets()
    {
        var a = new DefaultHttpContext();
        a.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var b = new DefaultHttpContext();
        b.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");

        Assert.NotEqual(EmailInviteRateLimit.PartitionKey(a), EmailInviteRateLimit.PartitionKey(b));
    }

    [Fact]
    public void PartitionKey_fails_closed_to_a_shared_bucket_when_no_ip_is_available()
    {
        // No RemoteIpAddress set - an IP-less caller must still land in a bounded
        // (shared "unknown") bucket, never an unlimited one.
        var context = new DefaultHttpContext();

        Assert.Equal("unknown", EmailInviteRateLimit.PartitionKey(context));
    }

    [Fact]
    public void Limit_tunables_are_sane()
    {
        Assert.True(EmailInviteRateLimit.PermitLimit > 0);
        Assert.True(EmailInviteRateLimit.Window > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(EmailInviteRateLimit.PolicyName));
    }
}
