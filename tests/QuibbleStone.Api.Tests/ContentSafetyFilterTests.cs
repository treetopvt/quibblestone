// ----------------------------------------------------------------------------
//  ContentSafetyFilterTests - unit tests for the child-safety/01 content filter.
//
//  These exercise the REAL ContentSafetyFilter, which loads the bundled baseline
//  blocklist embedded in the API assembly - so the tests cover the actual shipped
//  word list, not a stub. Two things are locked in here:
//
//    1. Profanity (standalone, leet-spelled, repeat-padded, or separator-
//       obfuscated) is blocked with a friendly, non-shaming message.
//    2. REGRESSION: innocent words that merely contain a short blocked term as a
//       substring ("class", "password", "spice", "pussycat", "peacock",
//       "sussex", "cucumber", "scunthorpe") are NOT blocked. The pre-fix filter
//       over-blocked these via a whole-string substring scan (the Scunthorpe
//       problem); the fix matches whole words / whole obfuscated strings only.
//
//  Known baseline gap (asserted below so it is explicit, not accidental):
//  profanity fused inside a longer single token ("fuckface") is not caught - a
//  documented Slice-1 limitation, since substring-matching it would re-introduce
//  the false positives above.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class ContentSafetyFilterTests
{
    private readonly ContentSafetyFilter _filter = new();

    [Theory]
    // Plainly clean words.
    [InlineData("wizard")]
    [InlineData("banana")]
    [InlineData("sparkly")]
    [InlineData("dinosaur")]
    [InlineData("Maple")]
    // Regression: innocent words that CONTAIN a short blocked term as a substring.
    [InlineData("class")]      // "ass"
    [InlineData("password")]   // "ass"
    [InlineData("grass")]      // "ass"
    [InlineData("compass")]    // "ass"
    [InlineData("spice")]      // "spic"
    [InlineData("pussycat")]   // "pussy"
    [InlineData("peacock")]    // "cock"
    [InlineData("scrap")]      // "crap"
    [InlineData("sussex")]     // "sex"
    [InlineData("cucumber")]   // "cum"
    [InlineData("raccoon")]    // "coon"
    [InlineData("scunthorpe")] // "cunt"
    public async Task Allows_clean_and_innocent_substring_words(string text)
    {
        var result = await _filter.CheckAsync(text);
        Assert.True(result.IsAllowed, $"Expected '{text}' to be allowed");
        Assert.Null(result.Message);
    }

    [Theory]
    // Standalone profanity.
    [InlineData("fuck")]
    [InlineData("shit")]
    [InlineData("ass")]
    [InlineData("bitch")]
    // Case + leet + repeat folding.
    [InlineData("FUCK")]
    [InlineData("Sh1t")]
    [InlineData("fuuuck")]
    [InlineData("a$$")]
    [InlineData("n1gger")]
    // Separator obfuscation (whole-string collapse).
    [InlineData("f u c k")]
    [InlineData("f-u-c-k")]
    [InlineData("s.h.i.t")]
    // One bad word inside an otherwise clean phrase (per-token match).
    [InlineData("you are a fuck")]
    public async Task Blocks_profanity_standalone_and_obfuscated(string text)
    {
        var result = await _filter.CheckAsync(text);
        Assert.False(result.IsAllowed, $"Expected '{text}' to be blocked");
        Assert.NotNull(result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Allows_empty_or_whitespace(string? text)
    {
        var result = await _filter.CheckAsync(text);
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Blocked_message_is_friendly_and_never_echoes_the_word()
    {
        var result = await _filter.CheckAsync("shit");

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Message);
        // Friendly / encouraging, and it must never repeat the offending word back.
        Assert.Contains("try", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shit", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Known_gap_compound_embedded_profanity_is_not_caught()
    {
        // Documented Slice-1 limitation: a blocked term fused inside a longer
        // single token is not substring-matched (that would over-block innocent
        // words). This test pins the CURRENT behavior so a future change that
        // closes the gap is a deliberate, reviewed decision - not a silent drift.
        var result = await _filter.CheckAsync("fuckface");
        Assert.True(result.IsAllowed);
    }

    [Theory]
    [InlineData("FUUUCK", "fuck")] // case + repeat collapse
    [InlineData("HELLO", "helo")]  // repeat collapse on an innocent word
    [InlineData("sh1t", "shit")]   // leet fold
    [InlineData("f@t", "fat")]     // leet fold
    [InlineData("f-u-c-k", "fuck")] // separators dropped, letters folded
    public void Normalize_folds_case_leet_repeats_and_separators(string input, string expected)
    {
        Assert.Equal(expected, ContentSafetyFilter.Normalize(input));
    }
}
