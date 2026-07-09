// ----------------------------------------------------------------------------
//  StripeResyncRateLimitTests - the partitioning of the operator Stripe-resync
//  endpoint's rate limit (billing-entitlements/08, #215, AC-06d). The framework's
//  fixed-window enforcement is trusted (same posture as PublishTalesRateLimitTests);
//  what is worth locking in is the SECURITY-relevant custom bit - that this limiter
//  partitions on a CONSTANT GLOBAL key (one shared budget for the whole endpoint),
//  NOT per-IP: the abuse scenario is repeated invocation against Stripe's API, which
//  the operator-only auth already scopes to one trusted actor, so distinct IPs must
//  NOT each get their own budget to multiply the Stripe traffic.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using Microsoft.AspNetCore.Http;
using QuibbleStone.Api.Billing;

namespace QuibbleStone.Api.Tests.Billing;

public sealed class StripeResyncRateLimitTests
{
    // The whole endpoint shares ONE bucket: two different callers (different IPs) map to
    // the SAME partition key, unlike the per-IP limiters.
    [Fact]
    public void PartitionKey_is_a_constant_global_bucket_not_per_ip()
    {
        var a = new DefaultHttpContext();
        a.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var b = new DefaultHttpContext();
        b.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");

        Assert.Equal(StripeResyncRateLimit.PartitionKey(a), StripeResyncRateLimit.PartitionKey(b));
    }

    // Even an IP-less caller lands in the same shared bucket (still bounded).
    [Fact]
    public void PartitionKey_is_stable_even_without_an_ip()
    {
        var context = new DefaultHttpContext();

        Assert.Equal(StripeResyncRateLimit.PartitionKey(new DefaultHttpContext()), StripeResyncRateLimit.PartitionKey(context));
        Assert.False(string.IsNullOrWhiteSpace(StripeResyncRateLimit.PartitionKey(context)));
    }

    [Fact]
    public void Limit_tunables_are_sane()
    {
        Assert.True(StripeResyncRateLimit.PermitLimit > 0);
        Assert.True(StripeResyncRateLimit.Window > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(StripeResyncRateLimit.PolicyName));
    }
}
