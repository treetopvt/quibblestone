// ----------------------------------------------------------------------------
//  GameModeCatalog - the ONE server-side home for "which modes may a group round
//  be started in, and which templates each mode may draw" (group-play/05).
//
//  Why this exists: group-play/05 lets the HOST pick the mode for a room. The
//  pick is server-enforced (like the family-safe toggle) - GameHub.StartRound
//  validates the mode against THIS catalog before starting, so an unoffered or
//  unknown mode can never begin a round no matter what a crafted client sends.
//  Keeping the offered set in one place (not inlined in the hub) is what the
//  story asks for: an unoffered mode (e.g. progressive-story for now) can never
//  leak in through a second, forgotten code path.
//
//  Two facts live here, both mirrored on the WEB side by hand (no codegen):
//    - Offered      : the modes the group picker offers (web: GROUP_MODES in
//                     web/src/pages/modeRegistry.ts). Classic Blind, Word Bank,
//                     and Progressive Reveal each ride the existing distribute ->
//                     collect -> broadcast-reveal loop with NO new real-time
//                     surface (AC-04). Progressive Story is deliberately EXCLUDED
//                     (AC-05): its group "story so far" needs a live partial-fill
//                     broadcast that does not exist yet, so it is deferred rather
//                     than shipped half-working.
//    - IsEligible   : per-mode template eligibility. Word Bank may draw ONLY
//                     templates that carry a curated word bank (AC-06), mirroring
//                     the web's offerWordBankTemplates rule via the catalog's
//                     HasWordBank flag; every other offered mode draws from the
//                     full family-safe pool.
//
//  Pure static data + pure functions (no state, no DI): like TemplateCatalog it
//  is a small, hand-authored mirror. It carries NO PII (a mode id is not personal
//  data) and no template prose.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Content;

/// <summary>
/// The offered group-play modes and their per-mode template eligibility
/// (group-play/05). Mirrors web/src/pages/modeRegistry.ts's GROUP_MODES by hand.
/// </summary>
public static class GameModeCatalog
{
    /// <summary>The base mode - the safe default a null/empty/legacy request falls back to.</summary>
    public const string ClassicBlind = "classic-blind";

    /// <summary>Tap a curated word instead of typing; draws only word-bank templates (AC-06).</summary>
    public const string WordBank = "word-bank";

    /// <summary>Fill blind, then the finished tale unveils one word at a time (client-paced reveal).</summary>
    public const string ProgressiveReveal = "progressive-reveal";

    /// <summary>
    /// A KNOWN mode that is deliberately NOT offered for group play (AC-05): its
    /// "story so far" must reflect OTHER players' in-progress fills, which needs a
    /// live partial-fill broadcast (a new real-time surface) that does not exist
    /// yet. Named here only so the deferral is explicit and greppable - it is
    /// absent from <see cref="Offered"/>, so <see cref="ResolveOffered"/> rejects it.
    /// </summary>
    public const string ProgressiveStory = "progressive-story";

    /// <summary>
    /// The modes the host may start a group round in (AC-04). Case-insensitive.
    /// Progressive Story is intentionally absent (AC-05).
    /// </summary>
    public static readonly IReadOnlySet<string> Offered =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ClassicBlind,
            WordBank,
            ProgressiveReveal,
        };

    /// <summary>
    /// Resolve a client-supplied mode to its canonical offered id, or null if it
    /// is not an offered mode (an unknown id, or a known-but-deferred one like
    /// progressive-story - AC-05). A null/empty/whitespace value defaults to
    /// <see cref="ClassicBlind"/>, mirroring the defensive posture of
    /// NormalizeVariant / NormalizeLengthPreference in GameHub: a malformed client
    /// (or a legacy 3-argument caller) can only ever fall back to the base mode,
    /// never bypass the offered gate into an unoffered mode.
    /// </summary>
    public static string? ResolveOffered(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return ClassicBlind;
        }

        return Offered.Contains(mode) ? mode.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Whether <paramref name="entry"/> may be drawn for <paramref name="mode"/>
    /// (AC-06). Word Bank requires a curated word bank (mirrors the web's
    /// offerWordBankTemplates gate via <see cref="TemplateCatalogEntry.HasWordBank"/>);
    /// every other offered mode is bank-agnostic and accepts any family-safe entry.
    /// </summary>
    public static bool IsEligible(TemplateCatalogEntry entry, string mode) =>
        !string.Equals(mode, WordBank, StringComparison.OrdinalIgnoreCase) || entry.HasWordBank;
}
