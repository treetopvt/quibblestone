// ----------------------------------------------------------------------------
//  ReportRateLimitTests - covers the per-IP partitioning of the public "report
//  this tale" endpoint's rate limit (sysadmin-console/03, issue #137, AC-05).
//  Mirrors PublishTalesRateLimitTests: the framework's fixed-window enforcement is
//  trusted; what is worth locking in is the SECURITY-relevant custom bit - that the
//  limit partitions on the CALLER'S IP (so one actor cannot flood reports to
//  force-hide a tale for everyone) and fails CLOSED to a shared bucket (bounded, not
//  unlimited) when no IP is available.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using Microsoft.AspNetCore.Http;
using QuibbleStone.Api.PublishedTales;

namespace QuibbleStone.Api.Tests;

public sealed class ReportRateLimitTests
{
    [Fact]
    public void PartitionKey_is_the_caller_ip_so_the_limit_is_per_client()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal("203.0.113.7", ReportTalesRateLimit.PartitionKey(context));
    }

    [Fact]
    public void PartitionKey_gives_two_different_ips_two_different_buckets()
    {
        var a = new DefaultHttpContext();
        a.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var b = new DefaultHttpContext();
        b.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");

        Assert.NotEqual(ReportTalesRateLimit.PartitionKey(a), ReportTalesRateLimit.PartitionKey(b));
    }

    [Fact]
    public void PartitionKey_fails_closed_to_a_shared_bucket_when_no_ip_is_available()
    {
        // No RemoteIpAddress set - an IP-less caller must still land in a bounded
        // (shared "unknown") bucket, never an unlimited one (AC-05, fail-closed).
        var context = new DefaultHttpContext();

        Assert.Equal("unknown", ReportTalesRateLimit.PartitionKey(context));
    }

    [Fact]
    public void Limit_tunables_are_sane()
    {
        Assert.True(ReportTalesRateLimit.PermitLimit > 0);
        Assert.True(ReportTalesRateLimit.Window > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(ReportTalesRateLimit.PolicyName));
    }

    [Fact]
    public void Report_policy_is_a_distinct_sibling_of_the_publish_policy()
    {
        // A SIBLING with its own tunables (the task allows a separate policy), so a
        // report flood and a publish flood are bounded independently.
        Assert.NotEqual(PublishTalesRateLimit.PolicyName, ReportTalesRateLimit.PolicyName);
    }
}
