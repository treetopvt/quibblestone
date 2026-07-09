// ----------------------------------------------------------------------------
//  SettingsCatalog - the static, APPEND-ONLY registry of every runtime settings key
//  (control-plane/01, issue #197). Mirrors EntitlementCatalog's static-const-list
//  shape: one place, string-keyed, never one-off booleans scattered per feature.
//
//  OWNERSHIP BY WAVE (the load-bearing contract): this story (01) owns the MECHANISM
//  and registers only the scaffolding/example keys below - enough to prove the catalog
//  end to end (a bounded numeric knob, a confirmation-gated kill switch, a plain
//  string). It owns NO production knob. Story 02 (system flags: publishing.enabled /
//  ai.enabled / email.enabled) and story 03 (knob migration: auto-hide threshold, AI
//  per-IP / per-session / monthly-ceiling, seat grace, tale TTL, operator-login rate)
//  each APPEND their keys to `All` in a later wave (same file, serialized - never
//  concurrent). Every numeric key a later story adds MUST supply a Bounds that keeps
//  the knob meaningful (ADR 0003 "cannot disable its own safety rails"); a numeric key
//  with no Bounds is a review blocker in that story.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The static registry of runtime settings keys (control-plane/01). <see cref="All"/> is the
/// single append-only list; <see cref="TryGet"/> is the point lookup the controller uses to
/// reject an unknown key. Story 02 / 03 append production keys here in later waves.
/// </summary>
public static class SettingsCatalog
{
    // ---- Scaffolding / example keys (control-plane/01) ------------------------
    //
    //  These `example.*` keys exist ONLY to prove the mechanism (bounds enforcement,
    //  the confirmation gate, the default-fallback / cache round-trip) end to end with
    //  zero production knobs. They are operator-only and harmless. Story 02 / 03 append
    //  the real keys; these examples may be retired once real keys exercise every shape.

    /// <summary>Scaffolding: a bounded integer knob (proves AC-08's Bounds enforcement).</summary>
    public const string ExampleThreshold = "example.sampleThreshold";

    /// <summary>Scaffolding: a bounded decimal knob (proves numeric bounds on the Decimal type).</summary>
    public const string ExampleRate = "example.sampleRate";

    /// <summary>Scaffolding: a confirmation-gated kill switch (proves AC-10's RequiresConfirmation gate).</summary>
    public const string ExampleEnabled = "example.enabled";

    /// <summary>Scaffolding: a plain string knob (proves the String type + default fallback).</summary>
    public const string ExampleLabel = "example.label";

    // ---- System-scope capability flags (control-plane/02, #213) ---------------
    //
    //  The first system-scope keys (ADR 0003 Layer 1): app-wide kill switches an
    //  operator can force OFF. In PRECEDENCE they win over any account grant - a system
    //  force-off can never be re-enabled by a grant, for any session - but that
    //  precedence is IMPLEMENTED as a post-compose filter (baseline + grants compose
    //  first, then the system flag SUBTRACTS), not a pre-grant branch (see
    //  SystemFlagEvaluator / StoredValueEntitlementService). Each defaults to true (the
    //  code default) so shipping the keys changes zero observed
    //  behavior - the EFFECTIVE value is (this flag) AND the config-presence floor
    //  (SystemConfigPresence), so a flag can force a configured capability OFF but can
    //  never enable an unconfigured one (SystemFlagEvaluator owns that AND). All three
    //  are confirmation-gated (RequiresConfirmation): a kill switch is never an
    //  accidental one-field PUT (ADR 0003 "cannot disable its own safety rails", the
    //  same posture as ExampleEnabled). Only ai.enabled has a live consuming capability
    //  key (ai.onDemand) in this story; publishing.enabled / email.enabled are reserved
    //  and effective-value-correct until a future story wires a capability against them.

    /// <summary>System kill switch for AI capabilities (control-plane/02): forces every <c>ai.*</c> capability OFF when false. Effective value is ANDed with AI config-presence.</summary>
    public const string AiEnabled = "ai.enabled";

    /// <summary>System kill switch for publishing (control-plane/02): reserved (no live capability key yet). Effective value is ANDed with published-tales config-presence.</summary>
    public const string PublishingEnabled = "publishing.enabled";

    /// <summary>System kill switch for email (control-plane/02): reserved (no live capability key yet). Effective value is ANDed with email-provider config-presence.</summary>
    public const string EmailEnabled = "email.enabled";

    /// <summary>
    /// Every registered settings key (control-plane/01). Append-only: story 02 and story 03 add
    /// their production keys to this list in later waves. Order is display order for the admin
    /// GET; it carries no other meaning.
    /// </summary>
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        // A bounded integer: an operator may retune it within [1, 100] but can neither zero it
        // (Min 1) nor set an absurd value (Max 100) - the shape every real numeric knob follows.
        new SettingDefinition(
            ExampleThreshold,
            SettingType.Int,
            CodeDefault: 3,
            Description: "Scaffolding example (control-plane/01): a bounded integer knob proving Bounds enforcement.",
            Bounds: new SettingBounds(1, 100)),

        // A bounded decimal: same rail on the Decimal type (e.g. the shape a monetary knob uses).
        new SettingDefinition(
            ExampleRate,
            SettingType.Decimal,
            CodeDefault: 1.5m,
            Description: "Scaffolding example (control-plane/01): a bounded decimal knob proving numeric bounds.",
            Bounds: new SettingBounds(0m, 1000m)),

        // A confirmation-gated boolean: the shape every *.enabled kill switch (story 02) and the
        // AI spend ceiling (story 03) follow - a flip is never an accidental one-field PUT (AC-10).
        new SettingDefinition(
            ExampleEnabled,
            SettingType.Bool,
            CodeDefault: true,
            Description: "Scaffolding example (control-plane/01): a confirmation-gated kill switch proving the confirm gate.",
            RequiresConfirmation: true),

        // A plain string: no numeric range, no confirmation - proves the String type + default fallback.
        new SettingDefinition(
            ExampleLabel,
            SettingType.String,
            CodeDefault: "hello",
            Description: "Scaffolding example (control-plane/01): a plain string knob proving the String type."),

        // ---- System-scope capability flags (control-plane/02, #213) ----------
        // Boolean kill switches, code default true, confirmation-gated. The EFFECTIVE
        // value an operator sees / a session evaluates is this flag AND the matching
        // config-presence floor (SystemFlagEvaluator) - a flag can force a configured
        // capability OFF but can never enable an unconfigured one (ADR 0003 Layer 1).
        new SettingDefinition(
            AiEnabled,
            SettingType.Bool,
            CodeDefault: true,
            Description: "System kill switch for AI capabilities (control-plane/02). Forces every ai.* capability OFF for new sessions when false; effective only when an AI endpoint is configured.",
            RequiresConfirmation: true),

        new SettingDefinition(
            PublishingEnabled,
            SettingType.Bool,
            CodeDefault: true,
            Description: "System kill switch for publishing (control-plane/02). Reserved: no consuming capability key yet; effective only when published-tales storage is configured.",
            RequiresConfirmation: true),

        new SettingDefinition(
            EmailEnabled,
            SettingType.Bool,
            CodeDefault: true,
            Description: "System kill switch for email (control-plane/02). Reserved: no consuming capability key yet; effective only when an email provider is configured.",
            RequiresConfirmation: true),
    ];

    /// <summary>
    /// Validates the catalog's internal coherence once at type load, so a mis-declaration story
    /// 02 / 03 could introduce when they append keys breaks startup with a clear message rather
    /// than passing unnoticed (a bad cast in a typed getter, or a bounds range nothing can satisfy).
    /// Enforced: every key is unique; a <see cref="SettingBounds"/> sits only on a numeric key (a
    /// bound on a Bool / String key would be silently ignored at PUT time); a bound is well-formed
    /// (<c>Max &gt;= Min</c>); and <see cref="SettingDefinition.CodeDefault"/> is the CLR type the
    /// declared <see cref="SettingType"/> implies (so a typed getter's cast can never fail). It does
    /// NOT enforce "every numeric key has Bounds" - that stays a review blocker in story 02 / 03,
    /// not a runtime policy here.
    /// </summary>
    static SettingsCatalog()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in All)
        {
            if (!seen.Add(def.Key))
            {
                throw new InvalidOperationException($"Duplicate settings key '{def.Key}' in the catalog.");
            }

            if (def.Bounds is not null && !def.IsNumeric)
            {
                throw new InvalidOperationException(
                    $"Settings key '{def.Key}' declares Bounds but is {def.Type} - bounds apply to numeric keys only.");
            }

            if (def.Bounds is { } bounds && bounds.Max < bounds.Min)
            {
                throw new InvalidOperationException(
                    $"Settings key '{def.Key}' has Bounds with Max ({bounds.Max}) below Min ({bounds.Min}) - no value could satisfy it.");
            }

            if (!CodeDefaultMatchesType(def))
            {
                throw new InvalidOperationException(
                    $"Settings key '{def.Key}' CodeDefault does not match its declared type {def.Type} - a typed getter's cast would throw.");
            }
        }
    }

    // The CodeDefault must be the CLR type the declared SettingType implies, so
    // RuntimeSettingsService's typed getters (which cast the resolved value) can never fail on a
    // never-overridden key. Checked once at startup, not per read.
    private static bool CodeDefaultMatchesType(SettingDefinition def) => def.Type switch
    {
        SettingType.Bool => def.CodeDefault is bool,
        SettingType.Int => def.CodeDefault is int,
        SettingType.Decimal => def.CodeDefault is decimal,
        SettingType.String => def.CodeDefault is string,
        _ => false,
    };

    /// <summary>
    /// Looks up a definition by exact (ordinal) key. Returns null for an unknown key, which the
    /// controller turns into a 400 (a PUT / DELETE never invents a key outside the catalog).
    /// </summary>
    public static SettingDefinition? TryGet(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
}
