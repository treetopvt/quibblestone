// ----------------------------------------------------------------------------
//  AiQuotaTests - the REAL per-anonymous-session AI quota (ai-cost-gate/03, #122).
//
//  Pins the story-03 acceptance criteria on the metering stage:
//    - AC-01: a session exceeding N is refused (Allowed = false) - the deny the gate
//      turns into a deterministic fallback BEFORE the transport is called.
//    - AC-02: the remaining count is server-authoritative - it decrements per consume
//      and pins at zero (the "N Fresh Runes left" meter).
//    - AC-04: distinct anonymous sessions (distinct InstanceIds) have INDEPENDENT
//      quotas - one session's spend never draws down another's.
//    - AC-05/AC-07: a bad/empty key returns a clean deny (never throws), so the caller
//      degrades to the fallback rather than failing open to unlimited.
//    - Thread-safety: concurrent consumes for one session never over-spend the cap.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Tests.Ai;

public class AiQuotaTests
{
    private static AiQuota BuildQuota(int perSession) =>
        new(new AiOptions { QuotaPerSession = perSession }, NullLogger<AiQuota>.Instance);

    [Fact]
    public void Consume_decrements_remaining_and_is_server_authoritative()
    {
        // AC-02: N = 3 -> the remaining count walks 2, 1, 0 as units are consumed.
        var quota = BuildQuota(perSession: 3);

        var first = quota.TryConsume("instance-A");
        var second = quota.TryConsume("instance-A");
        var third = quota.TryConsume("instance-A");

        Assert.True(first.Allowed);
        Assert.Equal(2, first.Remaining);
        Assert.True(second.Allowed);
        Assert.Equal(1, second.Remaining);
        Assert.True(third.Allowed);
        Assert.Equal(0, third.Remaining);
    }

    [Fact]
    public void Session_exceeding_N_is_denied_with_zero_remaining()
    {
        // AC-01: once the allowance is spent, the very next consume is refused (the
        // deny the gate degrades to the deterministic fallback), and it STAYS denied
        // no matter how many times the client replays the request.
        var quota = BuildQuota(perSession: 2);

        Assert.True(quota.TryConsume("instance-A").Allowed);
        Assert.True(quota.TryConsume("instance-A").Allowed);

        var overLimit = quota.TryConsume("instance-A");
        var replay = quota.TryConsume("instance-A");

        Assert.False(overLimit.Allowed);
        Assert.Equal(0, overLimit.Remaining);
        Assert.False(replay.Allowed);
        Assert.Equal(0, replay.Remaining);
    }

    [Fact]
    public void Distinct_sessions_have_independent_quotas()
    {
        // AC-04: session B's allowance is untouched by session A exhausting its own.
        var quota = BuildQuota(perSession: 1);

        var aFirst = quota.TryConsume("instance-A");
        var aSecond = quota.TryConsume("instance-A"); // A is now out
        var bFirst = quota.TryConsume("instance-B");  // B still has its full allowance

        Assert.True(aFirst.Allowed);
        Assert.False(aSecond.Allowed);
        Assert.True(bFirst.Allowed);
        Assert.Equal(0, bFirst.Remaining);
    }

    [Fact]
    public void Empty_key_fails_safe_to_deny_never_throws()
    {
        // AC-07: no usable session key -> cannot confirm quota -> DENY (never unlimited,
        // never an exception). The gate turns this into the deterministic fallback.
        var quota = BuildQuota(perSession: 5);

        var nullKey = quota.TryConsume(null!);
        var emptyKey = quota.TryConsume(string.Empty);

        Assert.False(nullKey.Allowed);
        Assert.Equal(0, nullKey.Remaining);
        Assert.False(emptyKey.Allowed);
        Assert.Equal(0, emptyKey.Remaining);
    }

    [Fact]
    public void Zero_or_negative_allowance_denies_from_the_start_never_unlimited()
    {
        // Fail-safe posture (AC-07): a misconfigured non-positive N means "no
        // allowance" (every call falls back), NEVER "unlimited".
        var zero = BuildQuota(perSession: 0);
        var negative = BuildQuota(perSession: -5);

        Assert.False(zero.TryConsume("instance-A").Allowed);
        Assert.False(negative.TryConsume("instance-A").Allowed);
    }

    [Fact]
    public void Concurrent_consumes_never_exceed_the_cap()
    {
        // Thread-safety: 200 threads race to consume one session's quota of N = 50.
        // EXACTLY 50 must be allowed - no over-spend past the cap under contention.
        const int limit = 50;
        const int attempts = 200;
        var quota = BuildQuota(perSession: limit);
        var allowed = 0;

        Parallel.For(0, attempts, _ =>
        {
            if (quota.TryConsume("instance-hot").Allowed)
            {
                Interlocked.Increment(ref allowed);
            }
        });

        Assert.Equal(limit, allowed);

        // And the session is now fully drained: the next consume is denied at zero.
        var afterDrain = quota.TryConsume("instance-hot");
        Assert.False(afterDrain.Allowed);
        Assert.Equal(0, afterDrain.Remaining);
    }
}
