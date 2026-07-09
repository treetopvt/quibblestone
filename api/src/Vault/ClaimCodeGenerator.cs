// ----------------------------------------------------------------------------
//  ClaimCodeGenerator - mints, formats, and normalizes the human-friendly vault
//  recovery claim code (keepsake-vault/03, AC-02/AC-07, issue #230).
//
//  A claim code lets a family recover a claimed vault (and every tale in it) onto a
//  NEW device by typing the code - the account-free recovery path (AC-02). It is a
//  BEARER SECRET exactly like the vault id (ADR 0003 "Handles are secrets"), so it
//  MUST be cryptographically random and unguessable, but it is ALSO typed by a human
//  reading it off one device onto another, so it must stay short and readable.
//
//  A NEW, SIBLING GENERATOR TO SlugGenerator - NOT an edit to it (Technical Notes):
//    - Same 31-glyph unambiguous alphabet (SlugGenerator.Alphabet, O/0/I/1/l
//      removed) and the SAME unbiased RandomNumberGenerator.GetInt32 pick primitive,
//      so a code stays readable aloud / typo-resistant with a real CSPRNG floor.
//    - But at LENGTH 9, NOT the published-tale slug's 12: a slug is tuned for a URL
//      nobody types by hand; a claim code is typed by hand. 31^9 (~2.6e13) is NOT
//      brute-force-proof at typeable length on its own - AC-03's three controls
//      (per-IP limiter, global ceiling, per-code burn) plus AC-07's 7-day validity
//      window are what make it infeasible in practice, never the keyspace alone.
//    - SlugGenerator itself is UNCHANGED (its length/alphabet are tuned for public
//      tale links); this is a separate primitive over the shared alphabet + RNG.
//
//  DISPLAY vs CANONICAL (AC-02): the CANONICAL stored/compared value is the raw
//  9-glyph string (uppercase alphabet only). For HUMANS it is displayed grouped into
//  three 3-character blocks separated by hyphens (e.g. "K5Q-2NX-8CP") so it is easy
//  to read and type. On redemption the typed value is NORMALIZED back to the
//  canonical form (hyphens / spaces / lowercase all tolerated) before comparison, so
//  a family can type it however they naturally do.
//
//  Pure, static, dependency-light so it is trivially unit-tested (alphabet
//  membership, length, grouping round-trip, normalization).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using QuibbleStone.Api.PublishedTales;

namespace QuibbleStone.Api.Vault;

/// <summary>
/// Mints, formats, and normalizes the human-friendly vault recovery claim code
/// (keepsake-vault/03). A cryptographically random, unguessable bearer handle
/// (AC-02) - a length-9 SIBLING of <see cref="SlugGenerator"/> over the SAME 31-glyph
/// alphabet and RNG primitive, NOT an edit to it. Pure and static - no state, no DI.
/// </summary>
public static class ClaimCodeGenerator
{
    /// <summary>
    /// The claim-code alphabet: reused from <see cref="SlugGenerator.Alphabet"/> (the
    /// unambiguous 31-glyph set, look-alikes O/0/I/1/l removed) so a code stays easy
    /// to read aloud and type without look-alike confusion. Reused, never redefined,
    /// so the two generators cannot drift.
    /// </summary>
    public const string Alphabet = SlugGenerator.Alphabet;

    /// <summary>
    /// The claim-code length: 9 glyphs over the 31-glyph alphabet (31^9 ~ 2.6e13),
    /// deliberately SHORTER than <see cref="SlugGenerator.SlugLength"/> (12) because a
    /// claim code is typed by hand off one device onto another. The keyspace alone is
    /// not brute-force-proof at this length - AC-03's three controls plus AC-07's
    /// validity window are what make it infeasible.
    /// </summary>
    public const int CodeLength = 9;

    /// <summary>The size of each displayed block (AC-02): three blocks of three glyphs.</summary>
    public const int BlockSize = 3;

    /// <summary>
    /// Mints one fresh, canonical (ungrouped) claim code: <see cref="CodeLength"/>
    /// glyphs, each picked with an unbiased cryptographic RNG from
    /// <see cref="Alphabet"/> (the same primitive <see cref="SlugGenerator"/> uses).
    /// Never sequential, never time-derived. The store persists / compares this
    /// canonical value; <see cref="Format"/> produces the human display form.
    /// </summary>
    public static string Generate()
    {
        Span<char> code = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            code[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(code);
    }

    /// <summary>
    /// Formats a canonical code for HUMAN display (AC-02): groups it into blocks of
    /// <see cref="BlockSize"/> separated by hyphens (e.g. "K5Q-2NX-8CP"). A value that
    /// is not exactly <see cref="CodeLength"/> canonical glyphs is returned unchanged
    /// (defensive - callers pass a freshly minted / stored canonical code).
    /// </summary>
    /// <param name="canonicalCode">The raw 9-glyph canonical code.</param>
    /// <returns>The hyphen-grouped display form, e.g. "K5Q-2NX-8CP".</returns>
    public static string Format(string canonicalCode)
    {
        if (canonicalCode.Length != CodeLength)
        {
            return canonicalCode;
        }

        var blocks = new List<string>(CodeLength / BlockSize);
        for (var i = 0; i < canonicalCode.Length; i += BlockSize)
        {
            blocks.Add(canonicalCode.Substring(i, BlockSize));
        }
        return string.Join('-', blocks);
    }

    /// <summary>
    /// Normalizes a human-typed code back to the canonical form for comparison
    /// (AC-02): strips everything that is not an alphabet glyph (hyphens, spaces,
    /// stray punctuation) and upper-cases, so a family can type "k5q 2nx 8cp",
    /// "K5Q-2NX-8CP", or "k5q2nx8cp" and all resolve to the same stored value.
    /// Returns null when the result is not exactly <see cref="CodeLength"/> valid
    /// glyphs (too short / too long / carrying a glyph outside the alphabet) so a
    /// malformed submission is rejected before any store lookup.
    /// </summary>
    /// <param name="submitted">The raw code as typed by a human (any grouping / case).</param>
    /// <returns>The canonical 9-glyph code, or null when the input is not a valid code shape.</returns>
    public static string? Normalize(string? submitted)
    {
        if (string.IsNullOrEmpty(submitted))
        {
            return null;
        }

        Span<char> canonical = stackalloc char[CodeLength];
        var length = 0;
        foreach (var raw in submitted)
        {
            var c = char.ToUpperInvariant(raw);
            if (Alphabet.IndexOf(c) < 0)
            {
                // A glyph outside the alphabet (hyphen, space) is a grouping / typing
                // artifact and is simply skipped. A glyph that is neither a separator
                // nor in the alphabet (e.g. an ambiguous 'O'/'0') still fails the
                // final length check below rather than silently mapping to something.
                continue;
            }

            if (length >= CodeLength)
            {
                // More valid glyphs than a code holds - a malformed / over-long input.
                return null;
            }
            canonical[length++] = c;
        }

        return length == CodeLength ? new string(canonical) : null;
    }
}
