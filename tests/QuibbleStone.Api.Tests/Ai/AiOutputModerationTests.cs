// ----------------------------------------------------------------------------
//  AiOutputModerationTests - unit tests for the real AiOutputModerator, the story
//  05 moderate-before-display seam (ai-cost-gate, issue #124).
//
//  These exercise the REAL composition: the actual ContentSafetyFilter (loading the
//  bundled blocklist), the actual FamilySafeContentSelector, and the default NoOp
//  Content Safety screen - so the tests cover the shipped hard gate, not a stub.
//  They pin the ACs:
//    - AC-01: an item that fails the filter is DROPPED and never returned.
//    - AC-02: a family-safe session drops non-family-safe output while a non-family-
//             safe session keeps more.
//    - AC-04: a partially-unsafe batch keeps survivors; too-few-left flips
//             Sufficient=false; an empty/all-dropped batch is never Sufficient=true.
//    - AC-05: with Content Safety config absent (the NoOp screen), behavior equals
//             the hard filter alone; a configured screen can drop an item.
//    - AC-06: a rejection leaves no content/PII - the result exposes only survivors
//             + the flag (there is no per-item reason to leak).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests.Ai;

public class AiOutputModerationTests
{
    private static AiOutputModerator BuildModerator(
        IAiContentSafetyScreen? contentSafety = null,
        int minimumSafeItems = AiOutputModerator.DefaultMinimumSafeItems) =>
        new(
            new ContentSafetyFilter(),
            contentSafety ?? new NoOpAiContentSafetyScreen(),
            NullLogger<AiOutputModerator>.Instance,
            minimumSafeItems);

    // --- AC-01: the hard filter drops unsafe AI items -----------------------------

    [Fact]
    public async Task Item_failing_the_filter_is_dropped_and_never_returned()
    {
        var moderator = BuildModerator();

        // "shit" is on the bundled blocklist; the clean words survive.
        var result = await moderator.ModerateAsync(
            new[] { "moss", "shit", "ember", "glint" },
            familySafe: false);

        Assert.DoesNotContain("shit", result.Safe);
        Assert.Equal(new[] { "moss", "ember", "glint" }, result.Safe);
        Assert.True(result.Sufficient);
    }

    [Fact]
    public async Task Obfuscated_profanity_is_dropped_too()
    {
        var moderator = BuildModerator();

        // Leet + separator obfuscation is caught by the same filter (child-safety/01).
        var result = await moderator.ModerateAsync(
            new[] { "sparkle", "sh1t", "f-u-c-k", "willow" },
            familySafe: false);

        Assert.Equal(new[] { "sparkle", "willow" }, result.Safe);
    }

    // --- AC-02: family-safe honored -----------------------------------------------

    [Fact]
    public async Task Family_safe_session_drops_non_family_safe_output_that_non_family_safe_keeps()
    {
        var moderator = BuildModerator();
        var items = new[] { "moss", "gun", "ember", "kill" };

        // Non-family-safe: "gun" / "kill" pass the profanity filter and are kept.
        var relaxed = await moderator.ModerateAsync(items, familySafe: false);
        Assert.Equal(new[] { "moss", "gun", "ember", "kill" }, relaxed.Safe);

        // Family-safe: the stricter standard drops the mild-but-not-kid terms, keeping
        // strictly fewer than the relaxed run (AC-02).
        var strict = await moderator.ModerateAsync(items, familySafe: true);
        Assert.Equal(new[] { "moss", "ember" }, strict.Safe);
        Assert.True(strict.Safe.Count < relaxed.Safe.Count);
    }

    // --- AC-04: drop-and-continue + enough-left -----------------------------------

    [Fact]
    public async Task Partially_unsafe_batch_keeps_survivors()
    {
        var moderator = BuildModerator();

        var result = await moderator.ModerateAsync(
            new[] { "acorn", "bitch", "fern", "crap", "petal" },
            familySafe: false);

        Assert.Equal(new[] { "acorn", "fern", "petal" }, result.Safe);
        Assert.True(result.Sufficient);
    }

    [Fact]
    public async Task Too_few_survivors_flips_sufficient_false()
    {
        // A caller (e.g. the jumble) that needs a richer set raises the floor; with
        // only two survivors and a floor of 3, Sufficient flips false so it falls back.
        var moderator = BuildModerator(minimumSafeItems: 3);

        var result = await moderator.ModerateAsync(
            new[] { "acorn", "fern", "shit", "crap", "damn" },
            familySafe: false);

        Assert.Equal(new[] { "acorn", "fern" }, result.Safe);
        Assert.False(result.Sufficient);
    }

    [Fact]
    public async Task All_dropped_batch_is_never_sufficient_with_empty_safe_list()
    {
        var moderator = BuildModerator();

        var result = await moderator.ModerateAsync(
            new[] { "shit", "crap", "bitch" },
            familySafe: false);

        Assert.Empty(result.Safe);
        Assert.False(result.Sufficient);
    }

    [Fact]
    public async Task Empty_input_is_never_sufficient()
    {
        var moderator = BuildModerator();

        var result = await moderator.ModerateAsync(Array.Empty<string>(), familySafe: true);

        Assert.Empty(result.Safe);
        Assert.False(result.Sufficient);
    }

    // --- AC-05: Content Safety optional second layer ------------------------------

    [Fact]
    public async Task With_content_safety_absent_behavior_equals_the_hard_filter()
    {
        // The NoOp screen (config absent) allows everything - so the result equals the
        // hard filter + family-safe alone (AC-05).
        var noScreen = BuildModerator(contentSafety: new NoOpAiContentSafetyScreen());

        var result = await noScreen.ModerateAsync(
            new[] { "moss", "shit", "ember" },
            familySafe: false);

        Assert.Equal(new[] { "moss", "ember" }, result.Safe);
    }

    [Fact]
    public async Task With_content_safety_present_it_can_drop_an_otherwise_clean_item()
    {
        // A configured screen (here a fake that rejects one clean word) adds strictness
        // ON TOP of the hard filter (AC-05).
        var moderator = BuildModerator(contentSafety: new RejectingScreen("ember"));

        var result = await moderator.ModerateAsync(
            new[] { "moss", "ember", "glint" },
            familySafe: false);

        Assert.Equal(new[] { "moss", "glint" }, result.Safe);
    }

    // --- AC-06: no content / PII leaks in the result ------------------------------

    [Fact]
    public async Task Result_exposes_only_survivors_and_the_flag_no_rejected_text()
    {
        var moderator = BuildModerator();

        var result = await moderator.ModerateAsync(
            new[] { "moss", "shit" },
            familySafe: false);

        // The rejected word never appears anywhere in the returned result.
        Assert.DoesNotContain("shit", result.Safe);
        Assert.Equal(new[] { "moss" }, result.Safe);
        // The record carries exactly the survivors + a bool - no rejection detail.
        Assert.True(result.Sufficient);
    }

    /// <summary>
    /// A fake Content Safety screen that drops one specific (otherwise clean) word, to
    /// prove the configured second layer adds strictness on top of the hard gate.
    /// </summary>
    private sealed class RejectingScreen : IAiContentSafetyScreen
    {
        private readonly string _reject;

        public RejectingScreen(string reject) => _reject = reject;

        public ValueTask<bool> IsAllowedAsync(string item, bool familySafe, CancellationToken cancellationToken = default)
            => new(!string.Equals(item, _reject, StringComparison.OrdinalIgnoreCase));
    }
}
