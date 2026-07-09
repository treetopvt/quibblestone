// ----------------------------------------------------------------------------
//  ClaimCodeGeneratorTests - pure unit tests for the vault recovery claim-code
//  primitive (keepsake-vault/03, AC-02, issue #230).
//
//  Covers ClaimCodeGenerator in isolation (no store, no controller): the minted
//  code's shape (9 glyphs, every glyph in the shared 31-glyph alphabet), the
//  canonical <-> grouped-display round-trip (Format), and normalization of a
//  human-typed code back to canonical form (hyphens / spaces / case tolerated,
//  wrong length or an out-of-alphabet glyph rejected with null - AC-02's "the
//  code is a bearer secret, typed by a human" requirement).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Vault;

public sealed class ClaimCodeGeneratorTests
{
    // ---- Generate: shape + alphabet membership --------------------------------

    [Fact]
    public void Generate_produces_nine_glyphs_all_drawn_from_the_alphabet()
    {
        var code = ClaimCodeGenerator.Generate();

        Assert.Equal(ClaimCodeGenerator.CodeLength, code.Length);
        Assert.All(code, c => Assert.Contains(c, ClaimCodeGenerator.Alphabet));
    }

    [Fact]
    public void Generate_excludes_the_ambiguous_lookalike_glyphs()
    {
        // The shared SlugGenerator alphabet deliberately drops O/0/I/1/l so a spoken
        // or handwritten code is never ambiguous.
        foreach (var ambiguous in new[] { 'O', '0', 'I', '1', 'L' })
        {
            Assert.DoesNotContain(ambiguous, ClaimCodeGenerator.Alphabet);
        }
    }

    [Fact]
    public void Generate_does_not_mint_the_same_code_twice_in_a_row()
    {
        // Not a formal randomness proof - just a sanity check that two successive
        // draws are not trivially identical (a real CSPRNG floor, per the header
        // comment on ClaimCodeGenerator).
        var a = ClaimCodeGenerator.Generate();
        var b = ClaimCodeGenerator.Generate();
        Assert.NotEqual(a, b);
    }

    // ---- Format: canonical -> grouped display ---------------------------------

    [Fact]
    public void Format_groups_a_canonical_code_into_three_hyphenated_blocks()
    {
        Assert.Equal("ABC-DEF-GHJ", ClaimCodeGenerator.Format("ABCDEFGHJ"));
    }

    [Fact]
    public void Format_returns_a_wrong_length_value_unchanged_defensively()
    {
        Assert.Equal("ABC", ClaimCodeGenerator.Format("ABC"));
        Assert.Equal(string.Empty, ClaimCodeGenerator.Format(string.Empty));
    }

    // ---- Normalize: human-typed -> canonical, or null on malformed input ------

    [Theory]
    [InlineData("k5q-2nx-8cp", "K5Q2NX8CP")]
    [InlineData("K5Q-2NX-8CP", "K5Q2NX8CP")]
    [InlineData("k5q 2nx 8cp", "K5Q2NX8CP")]
    [InlineData("k5q2nx8cp", "K5Q2NX8CP")]
    public void Normalize_tolerates_hyphens_spaces_and_case(string typed, string expectedCanonical)
    {
        Assert.Equal(expectedCanonical, ClaimCodeGenerator.Normalize(typed));
    }

    [Fact]
    public void Normalize_round_trips_with_Format()
    {
        var canonical = ClaimCodeGenerator.Generate();
        var displayed = ClaimCodeGenerator.Format(canonical);

        Assert.Equal(canonical, ClaimCodeGenerator.Normalize(displayed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K5Q2NX8C")]      // one glyph short
    [InlineData("K5Q2NX8CPX")]    // one glyph over
    public void Normalize_rejects_the_wrong_number_of_glyphs(string? typed)
    {
        Assert.Null(ClaimCodeGenerator.Normalize(typed));
    }

    [Fact]
    public void Normalize_rejects_a_code_carrying_an_out_of_alphabet_glyph()
    {
        // "O" is not in the alphabet (an ambiguous lookalike for 0) - it is skipped
        // as a non-glyph, leaving only 8 valid glyphs, so the shape check fails.
        Assert.Null(ClaimCodeGenerator.Normalize("K5Q2NX8CO"));
    }
}
