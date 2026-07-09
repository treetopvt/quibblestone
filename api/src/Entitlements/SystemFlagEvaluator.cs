// ----------------------------------------------------------------------------
//  SystemFlagEvaluator - the system-scope composition unit (control-plane/02,
//  issue #213). ADR 0003 Layer 1 adds a third concern to the entitlement
//  evaluation: an app-wide system scope (a kill switch / not-yet-launched flag)
//  that can force a capability OFF for every session, regardless of any account
//  grant.
//
//  PRECEDENCE vs. IMPLEMENTATION (two different things - do not conflate them):
//    - PRECEDENCE is "system force-off wins over any account grant". If a system
//      flag reads false, no grant can turn its capability back on, for any session.
//    - The IMPLEMENTATION of that precedence is a POST-COMPOSE FILTER, not an
//      "evaluate-system-first, branch early" mechanism. StoredValueEntitlementService
//      composes its baseline + account grants EXACTLY as before (unchanged); this
//      evaluator only ever SUBTRACTS from that result afterward, immediately before
//      the set is captured into SessionEntitlements. There is deliberately no early
//      branch that skips grant evaluation when a flag is off - that would be
//      unnecessary complexity the story explicitly rejects.
//
//  CONFIG-PRESENCE IS THE FLOOR (the binding safety rail, ADR 0003): the EFFECTIVE
//  system flag is (the settings flag) AND (the matching SystemConfigPresence field).
//  A settings override can force a CONFIGURED capability OFF (a kill switch) but can
//  NEVER enable one whose infrastructure is not configured - the control plane cannot
//  enable its own unbuilt infra, only disable a built one.
//
//  SCOPE (this story): only ai.enabled has a live consuming capability key
//  (EntitlementCatalog.AiCapabilities - ai.onDemand and any future ai.* sibling).
//  publishing.enabled / email.enabled are registered and effective-value-correct
//  (IsEffectivelyEnabledAsync) but have no capability key to filter yet, exactly the
//  way ai.onDemand itself was reserved before anything consumed it.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// Composes the system-scope kill switches (control-plane/02) into the entitlement evaluation as a
/// post-compose filter: it reads each <c>*.enabled</c> flag via <see cref="IRuntimeSettingsService"/>,
/// ANDs it with the matching <see cref="SystemConfigPresence"/> field (config-presence is the floor),
/// and removes the owning capability keys from an already-composed unlocked set when the effective flag
/// reads false. Injected into <see cref="StoredValueEntitlementService"/>; registered as a singleton so
/// it shares the settings service's short read cache. The <see cref="IEntitlementService"/> contract and
/// the capture-once discipline do not change - only what feeds the evaluation does.
/// </summary>
public sealed class SystemFlagEvaluator
{
    private readonly IRuntimeSettingsService _settings;
    private readonly SystemConfigPresence _configPresence;

    /// <summary>
    /// Constructs the evaluator over the runtime settings service (the <c>*.enabled</c> flags,
    /// control-plane/01) and the deployment's config-presence floor (control-plane/02).
    /// </summary>
    /// <param name="settings">Resolves each system flag's override-or-default value.</param>
    /// <param name="configPresence">Whether each capability's underlying infrastructure is configured (the floor).</param>
    public SystemFlagEvaluator(IRuntimeSettingsService settings, SystemConfigPresence configPresence)
    {
        _settings = settings;
        _configPresence = configPresence;
    }

    /// <summary>
    /// The EFFECTIVE value of a system-scope flag (control-plane/02, AC-01 / AC-05): the settings
    /// flag AND its config-presence floor. Returns false whenever the underlying infrastructure is
    /// not configured, even if the flag is left at its <c>true</c> default or explicitly set true -
    /// a settings override can never enable an unconfigured capability (ADR 0003). Config-presence
    /// short-circuits: an unconfigured capability never even reads the flag.
    /// </summary>
    /// <param name="systemKey">One of <see cref="SettingsCatalog.AiEnabled"/> / <see cref="SettingsCatalog.PublishingEnabled"/> / <see cref="SettingsCatalog.EmailEnabled"/>.</param>
    /// <param name="ct">Cancellation for the settings read.</param>
    /// <returns>True only when the infrastructure is configured AND the flag reads true.</returns>
    public async ValueTask<bool> IsEffectivelyEnabledAsync(string systemKey, CancellationToken ct = default)
    {
        // Config-presence is the FLOOR (ADR 0003): unconfigured infra -> effectively off,
        // regardless of the flag. Short-circuit before the settings read.
        if (!ConfigPresenceFor(systemKey))
        {
            return false;
        }

        return await _settings.GetBoolAsync(systemKey, ct);
    }

    /// <summary>
    /// The POST-COMPOSE FILTER step (control-plane/02, AC-03): removes any capability whose owning
    /// system flag reads effectively false from an already-composed unlocked set, mutating it in
    /// place. Runs AFTER <see cref="StoredValueEntitlementService"/> has composed its baseline +
    /// account grants unchanged, and immediately BEFORE the set is captured into
    /// <see cref="SessionEntitlements"/> - so a system force-off wins over any account grant, for any
    /// session, without ever skipping grant evaluation. Only <c>ai.enabled</c> owns capability keys
    /// today (<see cref="EntitlementCatalog.AiCapabilities"/>); publishing / email have none yet.
    /// </summary>
    /// <param name="unlocked">The composed unlocked capability set to subtract from, mutated in place.</param>
    /// <param name="ct">Cancellation for the settings reads.</param>
    public async ValueTask ApplyAsync(HashSet<string> unlocked, CancellationToken ct = default)
    {
        // ai.enabled -> the ai.* capability family. When effectively off, force EVERY ai.* key out of
        // the composed set (ai.onDemand and any future sibling), so no account grant can hold it open.
        if (!await IsEffectivelyEnabledAsync(SettingsCatalog.AiEnabled, ct))
        {
            foreach (var capabilityKey in EntitlementCatalog.AiCapabilities)
            {
                unlocked.Remove(capabilityKey);
            }
        }

        // publishing.enabled / email.enabled are registered and effective-value-correct but own no
        // consuming capability key yet (Out of Scope) - a future story adds the filter arm here when a
        // session-scoped publish / email capability exists, exactly as ai.* is filtered above.
    }

    // Maps a system-scope flag key to its config-presence floor. An unknown key is a coding bug (the
    // caller must pass a registered system-scope key), never a runtime branch - the catalog is the
    // single source of truth for which keys are system-scope.
    private bool ConfigPresenceFor(string systemKey) => systemKey switch
    {
        SettingsCatalog.AiEnabled => _configPresence.AiConfigured,
        SettingsCatalog.PublishingEnabled => _configPresence.PublishingConfigured,
        SettingsCatalog.EmailEnabled => _configPresence.EmailConfigured,
        _ => throw new ArgumentOutOfRangeException(
            nameof(systemKey), systemKey, "Not a registered system-scope flag key (control-plane/02)."),
    };
}
