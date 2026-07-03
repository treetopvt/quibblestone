// ----------------------------------------------------------------------------
//  JumbleWordGeneratorTests - the AI word-bank jumble backend
//  (ai-on-demand-generation/05, issue #126; moderation policy /02, #127).
//
//  These wire the generator to a REAL GatedAiCompletionClient (real quota, real
//  AiOutputModerator + ContentSafetyFilter, no-op spend guard) behind a stubbed
//  transport, so they exercise the SHIPPED gate path end to end, not a stub of
//  it. They pin the ACs:
//    - AC-01: a category request yields a parsed word set from the proxy reply.
//    - AC-02: on-theme single words, deduped vs the avoid-list; numbering /
//             multi-word / malformed lines are shaped away or dropped; too few
//             usable words -> fall back.
//    - AC-03: an unsafe word is dropped by the gate's moderation; a family-safe
//             session drops non-family-safe words a non-family-safe one keeps.
//    - AC-04/06: quota exhausted or AI unavailable -> fall back WITHOUT an AI
//             call; the remaining quota is carried for the meter.
//    - AC-07: the generator consumes the gate (no parallel generator / filter).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;
using QuibbleStone.Api.Ai.Jumble;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests.Ai;

public class JumbleWordGeneratorTests
{
    private static JumbleWordGenerator BuildGenerator(
        IAiCompletionClient transport,
        IAiQuota? quota = null)
    {
        var gate = new GatedAiCompletionClient(
            quota ?? new UnlimitedAiQuota(),
            new NoOpAiSpendGuard(),
            transport,
            new AiOutputModerator(
                new ContentSafetyFilter(),
                new NoOpAiContentSafetyScreen(),
                NullLogger<AiOutputModerator>.Instance),
            NullLogger<GatedAiCompletionClient>.Instance);

        return new JumbleWordGenerator(gate, NullLogger<JumbleWordGenerator>.Instance);
    }

    private static AiCompletionResult Reply(string text) =>
        new(text, InputTokens: 400, OutputTokens: 30, ModelId: "gpt-5-mini", IsAvailable: true);

    // --- AC-01/02: parse the proxy reply into a clean word set --------------------

    [Fact]
    public async Task Generates_a_parsed_word_set_from_the_proxy_reply()
    {
        var gen = BuildGenerator(new StubTransport(Reply("moss\nember\nglint\nfrost\nquartz")));

        var result = await gen.GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        Assert.False(result.FellBack);
        Assert.Equal(new[] { "moss", "ember", "glint", "frost", "quartz" }, result.Words);
    }

    [Fact]
    public async Task Shapes_away_numbering_and_drops_multiword_and_dupes()
    {
        // Numbered lines -> the word; a multi-word line -> dropped; a case dupe -> once.
        var gen = BuildGenerator(new StubTransport(Reply("1. moss\n2. ember\nfrost quartz\nMOSS\nglint\ncinder")));

        var result = await gen.GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        Assert.False(result.FellBack);
        Assert.Equal(new[] { "moss", "ember", "glint", "cinder" }, result.Words);
    }

    [Fact]
    public async Task Dedupes_against_the_avoid_list()
    {
        var gen = BuildGenerator(new StubTransport(Reply("moss\nember\nglint\nfrost\nquartz\ncinder")));

        var result = await gen.GenerateAsync(
            "noun",
            new[] { "moss", "Ember" },
            familySafe: false,
            instanceId: "room-1");

        Assert.False(result.FellBack);
        Assert.Equal(new[] { "glint", "frost", "quartz", "cinder" }, result.Words); // only the fresh words survive
        Assert.DoesNotContain("moss", result.Words);
        Assert.DoesNotContain("ember", result.Words);
    }

    [Fact]
    public async Task Too_few_usable_words_falls_back()
    {
        // Only two parse cleanly (a multiword + a numbered-only line yield nothing) -
        // below MinUsableWords, so the caller degrades to the free reshuffle.
        var gen = BuildGenerator(new StubTransport(Reply("moss\nember\nfrost quartz")));

        var result = await gen.GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        Assert.True(result.FellBack);
        Assert.Empty(result.Words);
    }

    [Fact]
    public async Task Empty_or_whitespace_reply_falls_back()
    {
        var gen = BuildGenerator(new StubTransport(Reply("   \n  \n")));

        var result = await gen.GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        Assert.True(result.FellBack);
        Assert.Empty(result.Words);
    }

    // --- AC-03: the gate's moderation drops unsafe / non-family-safe words ---------

    [Fact]
    public async Task Unsafe_word_is_dropped_by_the_gate_moderation()
    {
        // "shit" is on the bundled blocklist; it never reaches the returned set.
        var gen = BuildGenerator(new StubTransport(Reply("moss\nshit\nember\nglint\nfrost")));

        var result = await gen.GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        Assert.False(result.FellBack);
        Assert.DoesNotContain("shit", result.Words);
        Assert.Equal(new[] { "moss", "ember", "glint", "frost" }, result.Words);
    }

    [Fact]
    public async Task Family_safe_session_drops_words_a_non_family_safe_session_keeps()
    {
        const string reply = "moss\ngun\nember\nglint\nfrost";

        var safe = await BuildGenerator(new StubTransport(Reply(reply)))
            .GenerateAsync("noun", Array.Empty<string>(), familySafe: true, instanceId: "room-1");
        var open = await BuildGenerator(new StubTransport(Reply(reply)))
            .GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        // "gun" is in the moderator's family-safe-sensitive seed set.
        Assert.DoesNotContain("gun", safe.Words);
        Assert.Contains("gun", open.Words);
    }

    // --- AC-04/06: quota + availability degrade WITHOUT (or after) an AI call ------

    [Fact]
    public async Task Unavailable_transport_falls_back()
    {
        var gen = BuildGenerator(new StubTransport(AiCompletionResult.Unavailable));

        var result = await gen.GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        Assert.True(result.FellBack);
        Assert.Empty(result.Words);
    }

    [Fact]
    public async Task Quota_exhausted_falls_back_without_calling_the_transport()
    {
        var counting = new CountingTransport(Reply("moss\nember\nglint\nfrost"));
        // A zero allowance means every consume denies before the transport.
        var quota = new AiQuota(new AiOptions { QuotaPerSession = 0 }, NullLogger<AiQuota>.Instance);
        var gen = BuildGenerator(counting, quota);

        var result = await gen.GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        Assert.True(result.FellBack);
        Assert.Equal(0, counting.Calls);
    }

    [Fact]
    public async Task Remaining_quota_is_carried_for_the_meter()
    {
        var quota = new AiQuota(new AiOptions { QuotaPerSession = 5 }, NullLogger<AiQuota>.Instance);
        var gen = BuildGenerator(new StubTransport(Reply("moss\nember\nglint\nfrost")), quota);

        var result = await gen.GenerateAsync("noun", Array.Empty<string>(), familySafe: false, instanceId: "room-1");

        Assert.False(result.FellBack);
        Assert.Equal(4, result.RemainingQuota); // 5 - 1 consumed
    }

    /// <summary>A transport that returns a fixed, pre-baked result.</summary>
    private sealed class StubTransport : IAiCompletionClient
    {
        private readonly AiCompletionResult _result;
        public StubTransport(AiCompletionResult result) => _result = result;
        public Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    /// <summary>A transport that counts how many times it was called (to prove quota short-circuits before it).</summary>
    private sealed class CountingTransport : IAiCompletionClient
    {
        private readonly AiCompletionResult _result;
        public int Calls { get; private set; }
        public CountingTransport(AiCompletionResult result) => _result = result;
        public Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}
