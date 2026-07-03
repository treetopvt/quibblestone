// ----------------------------------------------------------------------------
//  AiCostTelemetry - the SHARED vocabulary + PURE property/metric builders for
//  the anonymous AI cost-ATTRIBUTION custom event (ai-cost-gate story 04, issue
//  #123, AC-05 / AC-06 / AC-07 / AC-08). Deliberately mirrors UsageTelemetry.
//
//  WHAT THIS IS (and what it is NOT): the ONE place that shapes the single
//  "AiCall" custom event the spend circuit-breaker emits AFTER each completed AI
//  call. Like UsageTelemetry it owns NO transport and NO state - it is a pure,
//  stateless helper that hands AiSpendBreaker the exact event NAME and the exact
//  property/metric bags to pass to TelemetryClient.TrackEvent, which rides
//  platform-devops/04's App Insights pipeline and its single PII scrubber
//  (PiiScrubbingTelemetryInitializer). Centralizing the shape here is what lets a
//  single unit test assert the no-PII guarantee (AC-06) instead of trusting the
//  call site to remember it.
//
//  WHAT ONE EVENT CARRIES (AC-05):
//    properties: feature ("jumble" now; "verdict" / "on-demand" reserved), model
//                (the deployment id), the anonymous instanceId (Room.InstanceId,
//                the GROUPING key), and a hot flag (AC-08).
//    metrics:    inputTokens, outputTokens, estCostUsd.
//  The FEATURE dimension ships from day one even though only the jumble exists
//  (AC-05, restated: do not defer it), so a per-feature cost breakdown is
//  answerable the moment a second feature arrives.
//
//  ANONYMOUS BY CONSTRUCTION (AC-06, README sections 3 + 6, NON-NEGOTIABLE): the
//  ONLY fields this event may ever carry are the feature tag, the model id, an
//  anonymous instance/room id, a hot flag, and the token/cost numbers. There is NO
//  field here through which a nickname, join/room code, player/connection session
//  id, IP, submitted word, or generated text could travel. The keys are chosen so
//  they are NOT in PiiScrubbingTelemetryInitializer's sensitive-key set (like the
//  allowed mode / context / deviceId keys): "instanceId" is the anonymous room
//  instance id, NOT a "sessionId"/"connectionId" (those stay dropped). The scrubber
//  is the backstop; this builder is written so there is nothing to scrub.
//
//  QUERYABILITY (AC-07 / AC-08, no dashboard required): "instanceId" as a property
//  (not a metric) makes a per-anonymous-session cost distribution a plain
//  `summarize sum(estCostUsd) by instanceId` query; "feature" makes the per-feature
//  breakdown a `by feature` query; the "hot" flag surfaces a disproportionately-
//  spending anonymous session as `where hot == 'true'`. Concentration is measured
//  over the anonymous instanceId ONLY, never identity.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Telemetry;

/// <summary>
/// Pure, stateless vocabulary + property/metric builders for the anonymous AI
/// cost-attribution custom event (story 04). No transport, no state: the spend
/// breaker passes the returned name + property/metric bags straight to
/// <c>TelemetryClient.TrackEvent</c>, so every attribution event rides
/// platform-devops/04's App Insights pipeline and its single PII scrubber (AC-05,
/// AC-06). The only fields that can leave here are the feature tag, the model id,
/// an anonymous instance/room id, a hot flag, and the token/cost numbers - all
/// anonymous by construction.
/// </summary>
public static class AiCostTelemetry
{
    /// <summary>Custom-event name: exactly ONE per completed AI call (AC-05).</summary>
    public const string AiCallEvent = "AiCall";

    /// <summary>The feature tag ("jumble" now; "verdict" / "on-demand" reserved). AC-05.</summary>
    public const string FeatureProperty = "feature";

    /// <summary>The model / deployment id that served the call (e.g. "gpt-4o-mini"). AC-05.</summary>
    public const string ModelProperty = "model";

    /// <summary>
    /// The anonymous session/room instance id (Room.InstanceId) - the GROUPING key
    /// for the per-session cost distribution (AC-07/AC-08). Explicitly ALLOWED past
    /// the scrubber: it is an anonymous room-instance id, NOT a "sessionId" /
    /// "connectionId" (those stay in PiiScrubbingTelemetryInitializer's sensitive set
    /// and are dropped). NEVER a nickname / join code / account (README section 6).
    /// </summary>
    public const string InstanceIdProperty = "instanceId";

    /// <summary>
    /// The concentration/abuse flag (AC-08): "true" when this anonymous session's
    /// cumulative estimated spend has crossed the hot threshold. Queryable
    /// (`where hot == 'true'`); measured over the anonymous instanceId ONLY.
    /// </summary>
    public const string HotProperty = "hot";

    /// <summary>Prompt (input) token count for the call, as a custom metric. AC-05.</summary>
    public const string InputTokensMetric = "inputTokens";

    /// <summary>Completion (output) token count for the call, as a custom metric. AC-05.</summary>
    public const string OutputTokensMetric = "outputTokens";

    /// <summary>The estimated USD cost of the call, as a custom metric (AC-05, AC-07).</summary>
    public const string EstCostUsdMetric = "estCostUsd";

    /// <summary>
    /// Builds the anonymous property bag for one AI cost-attribution event (AC-05,
    /// AC-06). Carries ONLY the feature tag, the model id, the anonymous instance id
    /// (when present), and the hot flag - there is intentionally no parameter, and no
    /// key, through which PII or content could travel. A null/blank instance id is
    /// simply omitted rather than recorded empty (the event still counts).
    /// </summary>
    /// <param name="feature">The attribution feature tag ("jumble", later "verdict" / "on-demand").</param>
    /// <param name="model">The model / deployment id that served the call.</param>
    /// <param name="instanceId">The anonymous session/room instance id (grouping key); omitted when null/blank.</param>
    /// <param name="hot">The AC-08 concentration flag for this anonymous session.</param>
    public static Dictionary<string, string> BuildProperties(
        string feature,
        string model,
        string instanceId,
        bool hot)
    {
        var properties = new Dictionary<string, string>
        {
            [FeatureProperty] = feature,
            [ModelProperty] = model,
            [HotProperty] = hot ? "true" : "false",
        };

        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            properties[InstanceIdProperty] = instanceId;
        }

        return properties;
    }

    /// <summary>
    /// Builds the metric bag for one AI cost-attribution event (AC-05): the input and
    /// output token counts and the estimated USD cost. Token counts are clamped to
    /// non-negative; the cost is passed through as-is (already non-negative from
    /// <see cref="Ai.AiCostEstimator"/>). Emitting cost as a METRIC is what makes a
    /// per-feature / per-session cost sum a plain App Insights aggregation (AC-07).
    /// </summary>
    /// <param name="inputTokens">Prompt (input) tokens the provider reported.</param>
    /// <param name="outputTokens">Completion (output) tokens the provider reported.</param>
    /// <param name="estCostUsd">The estimated USD cost of the call.</param>
    public static Dictionary<string, double> BuildMetrics(
        int inputTokens,
        int outputTokens,
        decimal estCostUsd)
    {
        return new Dictionary<string, double>
        {
            [InputTokensMetric] = Math.Max(0, inputTokens),
            [OutputTokensMetric] = Math.Max(0, outputTokens),
            [EstCostUsdMetric] = (double)estCostUsd,
        };
    }
}
