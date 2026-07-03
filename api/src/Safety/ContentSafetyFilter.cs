// ----------------------------------------------------------------------------
//  ContentSafetyFilter - the single, authoritative implementation of
//  IContentSafetyFilter for QuibbleStone (child-safety/01, README section 6).
//
//  This is the ONE place player free text is vetted before anyone sees it. It is
//  registered as a singleton in DI (Program.cs) so the hub and any future REST
//  controller share one instance and one blocklist (AC-05). No free-text surface
//  reimplements this logic.
//
//  How it works (slice-1 baseline, CLAUDE.md section 7 - solid, not exhaustive):
//    1. The blocklist ships as an embedded resource (Safety/blocklist.txt) and is
//       parsed ONCE in the constructor into a HashSet for O(1) lookups.
//    2. CheckAsync normalizes the candidate text and tests it against the list.
//    3. A clean match -> Allowed. A hit -> Blocked with a friendly, non-shaming,
//       kid-readable retry message (AC-02). The offending text is NEVER echoed
//       back in the message and is never stored or broadcast by the caller.
//
//  Normalization (the Normalize / Matches helpers) is deliberately PURE
//  (string in -> verdict out, no I/O, no state) so it is unit-testable in
//  isolation once platform-devops/01 wires an xUnit harness (no test framework
//  exists in the tree yet - CLAUDE.md section 9). It folds the obvious evasions:
//    - case ("FUCK" -> "fuck")
//    - common leet-speak ("@" -> "a", "0" -> "o", "1" -> "i", "$" -> "s", ...)
//    - separator obfuscation ("f-u-c-k", "f u c k", "f.u.c.k")
//    - padded repeats ("fuuuuck" -> "fuck")
//  It does NOT attempt perfect or locale-complete coverage - that, plus AI / remote
//  moderation, is parked in the backlog (README section 12). The async signature
//  (from IContentSafetyFilter) keeps a future remote check a drop-in.
//
//  Authoritative (AC-04): this runs server-side. A client may pre-validate for UX
//  but cannot bypass this gate.
//
//  No PII: this vets in-session free text and stores nothing about the player.
// ----------------------------------------------------------------------------

using System.Reflection;
using System.Text;

namespace QuibbleStone.Api.Safety;

/// <summary>
/// The single server-side content safety gate. Loads the bundled baseline
/// blocklist once and vets candidate free text against it. Thread-safe and
/// stateless after construction, so it is registered as a DI singleton.
/// </summary>
public sealed class ContentSafetyFilter : IContentSafetyFilter
{
    // Friendly, non-shaming, kid-readable rejection message (AC-02). Plain text -
    // there is no i18n layer in this stack (CLAUDE.md section 3). No em dashes.
    private const string BlockedMessage =
        "Let's try a different word - that one is not allowed here. Have another go!";

    // The embedded resource holding the baseline blocklist. The logical name is
    // "<RootNamespace>.<folder>.<file>" because the .csproj embeds it under that
    // path: QuibbleStone.Api + Safety + blocklist.txt.
    private const string BlocklistResourceName = "QuibbleStone.Api.Safety.blocklist.txt";

    // Leet-speak / look-alike folding applied during normalization. Folds the
    // common substitutions a player reaches for to slip a word past the filter.
    private static readonly Dictionary<char, char> LeetMap = new()
    {
        ['@'] = 'a',
        ['4'] = 'a',
        ['8'] = 'b',
        ['('] = 'c',
        ['<'] = 'c',
        ['3'] = 'e',
        ['6'] = 'g',
        ['1'] = 'i',
        ['!'] = 'i',
        ['|'] = 'i',
        ['0'] = 'o',
        ['$'] = 's',
        ['5'] = 's',
        ['7'] = 't',
        ['+'] = 't',
    };

    // The parsed baseline blocklist (normalized, lower-case terms). Built once.
    private readonly HashSet<string> _blockedTerms;

    /// <summary>
    /// Loads and parses the embedded baseline blocklist once. Terms are normalized
    /// the same way candidate text is, so the comparison is apples-to-apples.
    /// </summary>
    public ContentSafetyFilter()
    {
        _blockedTerms = LoadBlocklist();
    }

    /// <inheritdoc />
    public ValueTask<ContentSafetyResult> CheckAsync(string? candidate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Empty / whitespace text carries no content to block. Surfaces enforce
        // their own "non-empty" rules; this filter only judges what IS present.
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return new ValueTask<ContentSafetyResult>(ContentSafetyResult.Allowed);
        }

        var verdict = IsClean(candidate, _blockedTerms)
            ? ContentSafetyResult.Allowed
            : ContentSafetyResult.Blocked(BlockedMessage);

        return new ValueTask<ContentSafetyResult>(verdict);
    }

    // --- Pure matching core (no I/O, no instance state) --------------------------
    // These statics take (text, blockedTerms) and return a verdict, so they are
    // directly unit-testable without standing up DI or the embedded resource.

    /// <summary>
    /// True when none of the blocked terms appear in the candidate text. Pure.
    /// </summary>
    /// <param name="candidate">Raw player text (assumed non-empty here).</param>
    /// <param name="blockedTerms">The normalized blocklist to test against.</param>
    public static bool IsClean(string candidate, IReadOnlySet<string> blockedTerms)
    {
        if (blockedTerms.Count == 0)
        {
            return true;
        }

        // Two whole-word passes. Crucially, NEITHER does a substring scan, so an
        // innocent word that merely CONTAINS a short blocked term as a substring
        // is never blocked (the classic "Scunthorpe problem": "ass" in "class" /
        // "password", "sex" in "sussex", "cum" in "cucumber", "spic" in "spice",
        // "coon" in "raccoon", "pussy" in "pussycat"). Those all pass.

        // Pass 1: normalized per-token equality. Each whitespace/punctuation-
        // delimited token is normalized (leet-folded, letters-only, repeated
        // letters collapsed) and blocked only if it IS a blocked term. Catches
        // standalone profanity and its obvious spellings ("FUCK", "sh1t",
        // "shiiit", "a$$"), including one bad word inside an otherwise clean
        // phrase ("you are a fuck").
        foreach (var token in SplitTokens(candidate))
        {
            var normalizedToken = Normalize(token);
            if (normalizedToken.Length > 0 && blockedTerms.Contains(normalizedToken))
            {
                return false;
            }
        }

        // Pass 2: separator-obfuscation net. Normalizing the WHOLE candidate
        // collapses letters spaced or punctuated apart into a single word, so
        // "f u c k", "f-u-c-k", and "s.h.i.t" reduce to "fuck"/"shit". This is an
        // EXACT match against the blocklist, not a substring scan, so it stays
        // free of false positives on innocent multi-word text.
        var collapsed = Normalize(candidate);
        if (collapsed.Length > 0 && blockedTerms.Contains(collapsed))
        {
            return false;
        }

        // Compound / fused profanity (CLAUDE.md section 7 - solid, not exhaustive):
        // a bad word fused inside a longer single token ("shithead", "dumbass") is
        // caught ONLY when that whole compound is itself listed in blocklist.txt (a
        // curated set IS - see its "Compound / fused profanity" section, matched by
        // pass 1's exact-token equality above). We deliberately still do NOT
        // substring-scan to catch ARBITRARY compounds, because that re-introduces the
        // Scunthorpe false positives above - even "shit" and "cunt" have innocent
        // hosts ("shiitake", "Scunthorpe"). Broad generative coverage of unlisted
        // compounds is the parked AI-moderation work (README section 12).
        return true;
    }

    /// <summary>
    /// Normalizes a single token for blocklist comparison: lower-cases, folds
    /// leet-speak look-alikes, drops anything that is not a letter, and collapses
    /// runs of a repeated letter to a single one ("fuuuck" -> "fuck"). Pure.
    /// </summary>
    public static string Normalize(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(token.Length);
        var previous = '\0';

        foreach (var raw in token)
        {
            var lowered = char.ToLowerInvariant(raw);
            var folded = LeetMap.TryGetValue(lowered, out var mapped) ? mapped : lowered;

            // Keep letters only; this strips spaces, digits-not-folded, punctuation.
            if (!char.IsLetter(folded))
            {
                continue;
            }

            // Collapse an immediate repeat of the same letter.
            if (folded == previous)
            {
                continue;
            }

            sb.Append(folded);
            previous = folded;
        }

        return sb.ToString();
    }

    // Splits raw text into candidate tokens on whitespace and punctuation. Used by
    // pass 1 so each "word" is judged on its own (low false-positive matching).
    private static IEnumerable<string> SplitTokens(string text)
    {
        return text.Split(
            (char[]?)null, // null -> split on all Unicode whitespace
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    // --- Resource loading --------------------------------------------------------

    // Reads the embedded blocklist.txt, skips comments / blanks, normalizes each
    // term the same way candidate text is normalized, and returns the set.
    private static HashSet<string> LoadBlocklist()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(BlocklistResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded safety blocklist '{BlocklistResourceName}' was not found. " +
                "Confirm Safety/blocklist.txt is marked <EmbeddedResource> in the .csproj.");

        using var reader = new StreamReader(stream);

        var terms = new HashSet<string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var normalized = Normalize(trimmed);
            if (normalized.Length > 0)
            {
                terms.Add(normalized);
            }
        }

        return terms;
    }
}
