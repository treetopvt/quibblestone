// ----------------------------------------------------------------------------
//  SystemConfigPresence - the "is this infrastructure actually configured" floor
//  for the system-scope capability flags (control-plane/02, issue #213).
//
//  WHY THIS TYPE EXISTS (ADR 0003 Layer 1, "the control plane cannot disable its
//  own safety rails"): a system flag (ai.enabled / publishing.enabled /
//  email.enabled) may force a CONFIGURED capability OFF (a kill switch), but it may
//  NEVER enable one whose underlying infrastructure is not wired up. Config-presence
//  is the FLOOR: the effective system flag is (config-presence AND the settings
//  flag), so a settings override can only ever narrow, never widen, what the
//  deployment's actual configuration already allows.
//
//  These three booleans were, before this story, three inline conditions in three
//  different shapes at three separate Program.cs call sites (the AI endpoint, the
//  published-tales storage connection, the email provider). This story EXTRACTS the
//  boolean each condition already computes into one injectable value - constructed
//  ONCE where those options are already bound, reusing the SAME expressions (never
//  re-derived a second way), and registered as a singleton alongside the existing
//  config-presence branches. SystemFlagEvaluator ANDs each field against the
//  matching settings flag; nothing else consumes it.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// The config-presence floor for the system-scope capability flags (control-plane/02): whether the
/// AI endpoint, the published-tales storage, and an email provider are each actually configured for
/// this deployment. Constructed once in Program.cs from the SAME expressions the existing
/// config-presence branches use, and ANDed against the matching settings flag by
/// <see cref="SystemFlagEvaluator"/> so an operator override can force a configured capability OFF
/// but can never enable an unconfigured one (ADR 0003 - config-presence is the floor).
/// </summary>
/// <param name="AiConfigured">True when an AI endpoint is configured (<c>Ai:Endpoint</c> present).</param>
/// <param name="PublishingConfigured">True when published-tales storage is configured (<c>PublishedTales:StorageConnectionString</c> present).</param>
/// <param name="EmailConfigured">True when an email provider is configured (<c>EmailOptions.IsConfigured</c>).</param>
public sealed record SystemConfigPresence(
    bool AiConfigured,
    bool PublishingConfigured,
    bool EmailConfigured);
