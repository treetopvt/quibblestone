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

    // ---- Migrated operational knobs (control-plane/03, #232) ------------------
    //
    //  The seven hardcoded operational constants ADR 0003's audit named, migrated onto
    //  settings keys so an operator can retune them at runtime (no redeploy) while each
    //  keeps its former hardcoded value as the CODE DEFAULT below - so a fresh clone with
    //  no override behaves bit-for-bit as before (AC-01). The READ SITE of each knob now
    //  asks IRuntimeSettingsService for the current effective value at the point of use
    //  (see PublishedTalesController, AiQuota, AiSpendBreaker, SeatGraceService, and the
    //  Program.cs rate-limiter factories) rather than a value captured once at startup.
    //
    //  EVERY numeric key here carries Bounds (ADR 0003 "the control plane cannot disable
    //  its own safety rails"): a PUT outside the range is rejected before any write. The
    //  two rate-limit-permit keys (AI per-IP, operator-login) are ADDITIONALLY clamped to
    //  [1, sane-max] at their read site (AC-08) - belt AND suspenders, so a zero-or-huge
    //  value can never disable or crash the limiter even if it somehow reaches the read
    //  path. The AI monthly spend ceiling is bounded AND confirmation-gated (a spend rail
    //  is never an accidental one-field PUT).

    /// <summary>Report auto-hide threshold (control-plane/03): reports that push a public tale to "under review". Read in PublishedTalesController.Report. Code default 3.</summary>
    public const string ModerationTaleAutoHideThreshold = "moderation.tale.autoHideThreshold";

    /// <summary>AI per-IP rate-limit permits per minute (control-plane/03): read + CLAMPED [1, 10000] inside the Program.cs AiPerIp partition factory (AC-08). Code default 30.</summary>
    public const string AiRateLimitPerIpPermitPerMinute = "ai.rateLimit.perIpPermitPerMinute";

    /// <summary>AI per-anonymous-session call quota (control-plane/03): read live when a new session's allowance is established (AiQuota). Code default 20.</summary>
    public const string AiQuotaPerSession = "ai.quota.perSession";

    /// <summary>AI monthly spend ceiling in USD (control-plane/03): read live per breaker check (AiSpendBreaker). Bounded AND confirmation-gated (a spend rail). Code default 20.</summary>
    public const string AiSpendMonthlyCeilingUsd = "ai.spend.monthlyCeilingUsd";

    /// <summary>Seat grace window in seconds (control-plane/03): read live when a NEW disconnect schedules eviction (SeatGraceService); an in-flight timer keeps its original window. Code default 180.</summary>
    public const string SessionSeatGraceWindowSeconds = "session.seatGraceWindowSeconds";

    /// <summary>Public tale TTL in days (control-plane/03): read live at the publish stamp (PublishedTalesController.Publish); an already-published tale keeps its original expiry. Code default 30.</summary>
    public const string TalesTtlDays = "tales.ttlDays";

    /// <summary>Operator-login per-IP rate-limit permits per minute (control-plane/03): read + CLAMPED [1, 10000] inside the Program.cs OperatorLogin partition factory (AC-08). Code default 5.</summary>
    public const string AdminOperatorLoginRateLimitPermitPerMinute = "admin.operatorLogin.rateLimitPermitPerMinute";

    /// <summary>The sane floor/ceiling every rate-limit-permit knob is clamped into at its read site (control-plane/03, AC-08), independent of the catalog Bounds above.</summary>
    public const int RateLimitPermitClampMin = 1;

    /// <summary>See <see cref="RateLimitPermitClampMin"/>.</summary>
    public const int RateLimitPermitClampMax = 10_000;

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

        // ---- Migrated operational knobs (control-plane/03, #232) -------------
        // Each default MATCHES the former hardcoded constant (asserted knob-by-knob by
        // KnobMigrationRegressionTests), so no override = identical behavior (AC-01).

        // Report auto-hide threshold (was PublishedTalesController.AutoHideThreshold = 3).
        // Min 1: a threshold of 0 would auto-hide every tale on its first report - a value
        // that disables the "a human reported this" signal, so the floor keeps it a real
        // crowd threshold. Max 1000: generous headroom, never absurd.
        new SettingDefinition(
            ModerationTaleAutoHideThreshold,
            SettingType.Int,
            CodeDefault: 3,
            Description: "Number of anonymous reports that push a public tale into the neutral 'under review' state (control-plane/03). Governs a NEW report after the cache window; already-hidden tales are unaffected.",
            Bounds: new SettingBounds(1, 1000)),

        // AI per-IP rate-limit permits per minute (was Program.cs aiPerIpPermitPerWindow = 30).
        // Bounds mirror the read-site clamp [1, 10000] (AC-08): the catalog rejects a bad PUT,
        // and the factory lambda clamps whatever it reads as the independent safety net.
        new SettingDefinition(
            AiRateLimitPerIpPermitPerMinute,
            SettingType.Int,
            CodeDefault: 30,
            Description: "AI per-IP request permits per minute (control-plane/03). A NEW rate-limit partition picks up an override; an in-flight partition finishes its window under the old value (documented lag). Clamped [1, 10000] at the read site.",
            Bounds: new SettingBounds(RateLimitPermitClampMin, RateLimitPermitClampMax)),

        // AI per-anonymous-session quota (was AiOptions.QuotaPerSession = 20). Min 0 is a
        // legitimate "no allowance" (every AI call falls back - the fail-safe side, never
        // "unlimited"); the monthly spend ceiling backstops a large value.
        new SettingDefinition(
            AiQuotaPerSession,
            SettingType.Int,
            CodeDefault: 20,
            Description: "AI 'Fresh Runes' call allowance per anonymous session (control-plane/03). A NEW session picks up an override; a session already counting keeps its established allowance.",
            Bounds: new SettingBounds(0, 100_000)),

        // AI monthly spend ceiling in USD (was AiOptions.MonthlyCeilingUsd = 20). A spend
        // RAIL: a very high ceiling weakens the abuse backstop, so Max is bounded; Min 0 is
        // the safe side (breaker always open -> AI off). Confirmation-gated: never a
        // one-field flip (ADR 0003 "cannot disable its own safety rails").
        new SettingDefinition(
            AiSpendMonthlyCeilingUsd,
            SettingType.Decimal,
            CodeDefault: 20m,
            Description: "AI monthly spend ceiling in USD (control-plane/03). The breaker opens at 100% of this for the rest of the UTC month. A NEW check after the cache window uses the new value; a call in flight is unaffected. Confirmation-gated.",
            Bounds: new SettingBounds(0m, 1000m),
            RequiresConfirmation: true),

        // Seat grace window in seconds (was SeatGraceService.DefaultGraceWindow = 180s).
        // Min 1: a zero-second window would evict a seat the instant it drops, defeating
        // "don't lose the room"; Max 3600 (1 hour) is a generous upper tuning bound.
        new SettingDefinition(
            SessionSeatGraceWindowSeconds,
            SettingType.Int,
            CodeDefault: 180,
            Description: "Seconds a dropped seat is held before eviction (control-plane/03). A NEW disconnect after an override schedules the new window; a grace timer already running keeps its original window.",
            Bounds: new SettingBounds(1, 3600)),

        // Public tale TTL in days (was PublishedTalesController.TaleTtl = 30 days). Min 1
        // (a tale must live at least a day); Max 3650 (10 years) caps an absurd value while
        // leaving a keepsake ephemeral.
        new SettingDefinition(
            TalesTtlDays,
            SettingType.Int,
            CodeDefault: 30,
            Description: "Days a published public tale is retained before it expires (control-plane/03). A NEW tale is stamped with the current value at publish; an already-published tale keeps its original expiry.",
            Bounds: new SettingBounds(1, 3650)),

        // Operator-login per-IP rate-limit permits per minute (was OperatorLoginRateLimit.PermitLimit = 5).
        // Bounds mirror the read-site clamp [1, 10000] (AC-08), same posture as the AI per-IP key.
        new SettingDefinition(
            AdminOperatorLoginRateLimitPermitPerMinute,
            SettingType.Int,
            CodeDefault: 5,
            Description: "Operator-login-link request permits per minute per IP (control-plane/03). A NEW partition picks up an override; an in-flight partition finishes under the old value (documented lag). Clamped [1, 10000] at the read site.",
            Bounds: new SettingBounds(RateLimitPermitClampMin, RateLimitPermitClampMax)),
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
