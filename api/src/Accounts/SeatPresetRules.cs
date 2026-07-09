// ----------------------------------------------------------------------------
//  SeatPresetRules - the pure, shared validation rules for a seat preset's
//  nickname + Guardian variant (accounts-identity/08, issue #228).
//
//  WHY IT EXISTS: AC-04 / AC-07 require a preset nickname to obey the EXACT SAME
//  length cap and Guardian-variant normalization as a manually typed display name.
//  Those rules live in the game hub today (GameHub.MaxDisplayNameLength = 14 and its
//  private NormalizeVariant over the six known variants). The hub is the play-plane
//  boundary and the preset endpoints are the account plane, so rather than reach
//  across into gameplay code (or fork a second, drifting copy inline in the
//  controller), the SHAPE rules that both planes must agree on are stated here once,
//  as small pure statics the preset controller calls. The CONTENT-safety filter is
//  the genuinely shared DI service (IContentSafetyFilter) and is applied by the
//  controller, not here - this type is only the length + variant shape.
//
//  KEPT IN SYNC (deliberately, with a pointer): MaxNicknameLength mirrors
//  GameHub.MaxDisplayNameLength and web's PlayerIdentityFields.MAX_NAME_LENGTH (14);
//  KnownVariants / the "teal" default mirror GameHub.KnownVariants /
//  GameHub.NormalizeVariant. If the display-name cap or the variant set ever moves,
//  move it in all three places (there is no single cross-plane home for a value the
//  play plane and the account plane must both honor).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Pure shape rules for a seat preset's nickname + Guardian variant, mirroring the
/// game hub's display-name cap and variant normalization so a preset name obeys the
/// SAME length + variant rules as a manually typed one (accounts-identity/08,
/// AC-04/AC-07). Content safety is applied separately by the controller via the
/// shared IContentSafetyFilter.
/// </summary>
public static class SeatPresetRules
{
    /// <summary>
    /// Max preset nickname length - kept in sync with GameHub.MaxDisplayNameLength
    /// and web's PlayerIdentityFields.MAX_NAME_LENGTH (14). A preset name is a
    /// display name, so it obeys the same cap (AC-07).
    /// </summary>
    public const int MaxNicknameLength = 14;

    // The six known Guardian variants (case-insensitive), mirroring GameHub.KnownVariants.
    private static readonly HashSet<string> KnownVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "purple", "gold", "coral", "teal", "sand", "plum",
    };

    /// <summary>The default variant an unknown / empty value normalizes to (mirrors the hub).</summary>
    public const string DefaultVariant = "teal";

    /// <summary>
    /// Normalize a client-supplied Guardian variant to one of the six known values
    /// (case-insensitive), defaulting to "teal" for null / empty / unrecognized input
    /// and keeping the lowercase canonical form - byte-for-byte the hub's rule
    /// (GameHub.NormalizeVariant), so a preset's stored variant is indistinguishable
    /// from a manually chosen one.
    /// </summary>
    public static string NormalizeVariant(string? variant)
    {
        if (string.IsNullOrWhiteSpace(variant) || !KnownVariants.Contains(variant))
        {
            return DefaultVariant;
        }

        return variant.ToLowerInvariant();
    }
}
