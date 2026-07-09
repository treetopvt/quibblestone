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
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Ai;

public class AiQuotaTests
{
    // control-plane/03 (#232): the allowance N now comes from the `ai.quota.perSession`
    // settings key read live, not a captured AiOptions field - so a test pins N by seeding
    // that key's override (code default is 20). TryConsume is async (it reads settings).
    private static AiQuota BuildQuota(int perSession) =>
        new(TestRuntimeSettings.WithInt(SettingsCatalog.AiQuotaPerSession, perSession),
            NullLogger<AiQuota>.Instance);

    [Fact]
    public async Task Consume_decrements_remaining_and_is_server_authoritative()
    {
        // AC-02: N = 3 -> the remaining count walks 2, 1, 0 as units are consumed.
        var quota = BuildQuota(perSession: 3);

        var first = await quota.TryConsumeAsync("instance-A");
        var second = await quota.TryConsumeAsync("instance-A");
        var third = await quota.TryConsumeAsync("instance-A");

        Assert.True(first.Allowed);
        Assert.Equal(2, first.Remaining);
        Assert.True(second.Allowed);
        Assert.Equal(1, second.Remaining);
        Assert.True(third.Allowed);
        Assert.Equal(0, third.Remaining);
    }

    [Fact]
    public async Task Session_exceeding_N_is_denied_with_zero_remaining()
    {
        // AC-01: once the allowance is spent, the very next consume is refused (the
        // deny the gate degrades to the deterministic fallback), and it STAYS denied
        // no matter how many times the client replays the request.
        var quota = BuildQuota(perSession: 2);

        Assert.True((await quota.TryConsumeAsync("instance-A")).Allowed);
        Assert.True((await quota.TryConsumeAsync("instance-A")).Allowed);

        var overLimit = await quota.TryConsumeAsync("instance-A");
        var replay = await quota.TryConsumeAsync("instance-A");

        Assert.False(overLimit.Allowed);
        Assert.Equal(0, overLimit.Remaining);
        Assert.False(replay.Allowed);
        Assert.Equal(0, replay.Remaining);
    }

    [Fact]
    public async Task Distinct_sessions_have_independent_quotas()
    {
        // AC-04: session B's allowance is untouched by session A exhausting its own.
        var quota = BuildQuota(perSession: 1);

        var aFirst = await quota.TryConsumeAsync("instance-A");
        var aSecond = await quota.TryConsumeAsync("instance-A"); // A is now out
        var bFirst = await quota.TryConsumeAsync("instance-B");  // B still has its full allowance

        Assert.True(aFirst.Allowed);
        Assert.False(aSecond.Allowed);
        Assert.True(bFirst.Allowed);
        Assert.Equal(0, bFirst.Remaining);
    }

    [Fact]
    public async Task Empty_key_fails_safe_to_deny_never_throws()
    {
        // AC-07: no usable session key -> cannot confirm quota -> DENY (never unlimited,
        // never an exception). The gate turns this into the deterministic fallback.
        var quota = BuildQuota(perSession: 5);

        var nullKey = await quota.TryConsumeAsync(null!);
        var emptyKey = await quota.TryConsumeAsync(string.Empty);

        Assert.False(nullKey.Allowed);
        Assert.Equal(0, nullKey.Remaining);
        Assert.False(emptyKey.Allowed);
        Assert.Equal(0, emptyKey.Remaining);
    }

    [Fact]
    public async Task Zero_or_negative_allowance_denies_from_the_start_never_unlimited()
    {
        // Fail-safe posture (AC-07): a non-positive N means "no allowance" (every call
        // falls back), NEVER "unlimited". A negative override drifts past the catalog
        // bound but the read-site Math.Max(0, ...) still pins it to the safe side.
        var zero = BuildQuota(perSession: 0);
        var negative = BuildQuota(perSession: -5);

        Assert.False((await zero.TryConsumeAsync("instance-A")).Allowed);
        Assert.False((await negative.TryConsumeAsync("instance-A")).Allowed);
    }

    [Fact]
    public async Task An_overridden_quota_governs_a_new_session()
    {
        // control-plane/03 (#232) AC-05: the settings key (not a captured AiOptions
        // field) governs a NEW session's allowance. An override of 2 (below the code
        // default of 20) denies the 3rd consume for a fresh instance.
        var quota = BuildQuota(perSession: 2);

        Assert.True((await quota.TryConsumeAsync("instance-override")).Allowed);
        Assert.True((await quota.TryConsumeAsync("instance-override")).Allowed);

        var third = await quota.TryConsumeAsync("instance-override");

        Assert.False(third.Allowed);
        Assert.Equal(0, third.Remaining);
    }

    [Fact]
    public async Task A_session_already_counting_keeps_its_established_allowance()
    {
        // AC-05: the override only seeds a NEW session's allowance - a session that has
        // already started counting under a value is unaffected by anything after the
        // fact. (There is no live mid-session mutation seam in this codebase - the
        // settings service is read once, at the moment TryConsumeAsync establishes a
        // brand-new session's counter - so this just pins that a session keeps counting
        // consistently against the allowance it was created with.)
        var quota = BuildQuota(perSession: 5);

        var first = await quota.TryConsumeAsync("instance-established");
        Assert.Equal(4, first.Remaining);

        // Further consumes on the SAME session keep walking down from the SAME
        // established allowance (5), not any other value.
        for (var i = 0; i < 3; i++)
        {
            await quota.TryConsumeAsync("instance-established");
        }
        var fifth = await quota.TryConsumeAsync("instance-established");

        Assert.True(fifth.Allowed);
        Assert.Equal(0, fifth.Remaining);
    }

    [Fact]
    public async Task Concurrent_consumes_never_exceed_the_cap()
    {
        // Thread-safety: 200 tasks race to consume one session's quota of N = 50.
        // EXACTLY 50 must be allowed - no over-spend past the cap under contention.
        const int limit = 50;
        const int attempts = 200;
        var quota = BuildQuota(perSession: limit);
        var allowed = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, attempts),
            async (_, ct) =>
            {
                if ((await quota.TryConsumeAsync("instance-hot", ct)).Allowed)
                {
                    Interlocked.Increment(ref allowed);
                }
            });

        Assert.Equal(limit, allowed);

        // And the session is now fully drained: the next consume is denied at zero.
        var afterDrain = await quota.TryConsumeAsync("instance-hot");
        Assert.False(afterDrain.Allowed);
        Assert.Equal(0, afterDrain.Remaining);
    }
}
