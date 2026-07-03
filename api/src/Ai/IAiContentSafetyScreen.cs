// ----------------------------------------------------------------------------
//  IAiContentSafetyScreen - the OPTIONAL, config-gated second moderation layer for
//  AI output (ai-cost-gate story 05, issue #124, ADR 0001 decision B).
//
//  WHAT THIS IS: the seam for Azure AI Content Safety (contextual ML moderation of
//  hate / violence / sexual / self-harm), wired as a SECOND layer BEHIND the always-
//  on hard gate (the deterministic IContentSafetyFilter blocklist + family-safe
//  rule composed in AiOutputModerator). Per ADR 0001 decision B, the existing filter
//  is the ENFORCED hard gate NOW; Content Safety earns its place on the bigger free-
//  text payloads later (whole templates in ai-on-demand-generation/01-02) and is
//  turned on by a CONFIG FLIP, not a code change.
//
//  THE CONFIG-PRESENCE / NO-OP CONTRACT (AC-05): this mirrors the ITelemetrySink /
//  IAiCompletionClient idiom in Program.cs exactly. Two implementations sit behind
//  the one interface:
//    - NoOpAiContentSafetyScreen : the DEFAULT (local dev, CI, a fresh clone, and
//                                  today's deployed footprint). It allows every item -
//                                  so with NO `ContentSafety:Endpoint` configured the
//                                  moderator behaves IDENTICALLY to running the hard
//                                  filter + family-safe alone (AC-05).
//    - (Azure Content Safety)    : the real, config-gated screen, a documented DROP-IN
//                                  registered in Program.cs when `ContentSafety:Endpoint`
//                                  is present. It is NOT built in this story: story 06
//                                  provisions the Azure resource (optional Bicep) and
//                                  the Azure.AI.ContentSafety SDK is added THEN, so the
//                                  app takes on no heavy, unvalidatable dependency now.
//                                  See OPEN_QUESTIONS in the story hand-off.
//
//  WHY AN INTERFACE, NOT A DIRECT SDK CALL: keeping the second layer behind this seam
//  means the always-on hard gate (AC-01) never depends on a remote service being
//  reachable, and turning Content Safety on is a one-line DI swap in Program.cs the
//  day the resource exists - not a rewrite of the moderator.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The optional, config-gated Content Safety second layer over AI output (story 05,
/// ADR 0001 B). <see cref="AiOutputModerator"/> runs each item that already passed
/// the always-on hard gate (blocklist + family-safe) through this screen too. The
/// default <see cref="NoOpAiContentSafetyScreen"/> allows everything, so with no
/// <c>ContentSafety:Endpoint</c> configured the moderator behaves exactly as the hard
/// filter alone (AC-05). The real Azure AI Content Safety implementation is a
/// documented drop-in registered when the config is present (story 06 provisions it).
/// </summary>
public interface IAiContentSafetyScreen
{
    /// <summary>
    /// Screens ONE already-hard-gated AI item through the contextual second layer.
    /// Returns true if the item is allowed to survive, false to DROP it. Async and
    /// cancellation-aware because the real implementation is a remote call. A screen
    /// MUST fail to the SAFE side (drop, or - if it cannot judge - defer to the hard
    /// gate that already ran) and NEVER throw into the moderation loop.
    /// </summary>
    /// <param name="item">A single AI item that already passed the hard gate. Never shown before this returns.</param>
    /// <param name="familySafe">The round's family-safe toggle - the real screen tightens its severity thresholds when true.</param>
    /// <param name="cancellationToken">Cancellation for the (remote, in the real impl) check.</param>
    /// <returns>True to keep the item; false to drop it.</returns>
    ValueTask<bool> IsAllowedAsync(string item, bool familySafe, CancellationToken cancellationToken = default);
}

/// <summary>
/// The DEFAULT Content Safety screen: a pure pass-through that allows every item.
/// Registered whenever <c>ContentSafety:Endpoint</c> is absent (the default now), so
/// the second layer is a no-op and the always-on hard gate in
/// <see cref="AiOutputModerator"/> is the whole moderation story - identical behavior
/// to before this seam existed (AC-05). Stateless, so it is a DI singleton. This is
/// NOT a weakening of moderation: the hard gate (blocklist + family-safe) still runs
/// on every item regardless; this layer only ADDS strictness when configured.
/// </summary>
public sealed class NoOpAiContentSafetyScreen : IAiContentSafetyScreen
{
    /// <inheritdoc />
    public ValueTask<bool> IsAllowedAsync(string item, bool familySafe, CancellationToken cancellationToken = default)
    {
        // No second layer configured: defer entirely to the always-on hard gate that
        // already ran. Allow the item through (the hard gate already vetted it).
        return new ValueTask<bool>(true);
    }
}
