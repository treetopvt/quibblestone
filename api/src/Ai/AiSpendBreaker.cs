// ----------------------------------------------------------------------------
//  AiSpendBreaker - the REAL spend circuit-breaker + cost-attribution recorder
//  (ai-cost-gate story 04, issue #123). The concrete IAiSpendGuard that actually
//  enforces the $20/month ceiling, replacing the story-01 NoOpAiSpendGuard seam.
//
//  THE THIRD GATE STAGE, TWO RESPONSIBILITIES (see IAiSpendGuard / the gate order
//  in GatedAiCompletionClient):
//
//    1. IsUnderCeilingAsync (BEFORE the transport call) - reads the PERSISTED
//       running UTC-month spend total (TableStorageMonthlySpendStore, survives a
//       process recycle) and compares it to AiOptions.MonthlyCeilingUsd. At >= 100%
//       of the ceiling the breaker is OPEN: return false so every AI feature
//       degrades to its deterministic fallback for the REST of the UTC month. A new
//       UTC month keys a fresh total row, so the breaker closes and AI resumes with
//       no sweep job (AC-03). FAIL-SAFE (AC-09): an UNREADABLE total (the store
//       returned null) is treated as AT-ceiling -> false. We never call AI blind.
//
//    2. RecordAsync (AFTER a real call) - estimates the call's cost from story 01's
//       returned token usage x the per-model rates (AiCostEstimator, AC-01), adds it
//       to the persisted monthly total (best-effort, AC-02/AC-09), and emits EXACTLY
//       ONE anonymous App Insights attribution event (AiCostTelemetry, AC-05) so a
//       per-feature and per-anonymous-session cost breakdown is answerable by a plain
//       query (AC-07). Best-effort throughout: a metering/telemetry failure NEVER
//       faults the already-successful call (metering does not gate gameplay).
//
//  HOT-SESSION CONCENTRATION SIGNAL (AC-08): alongside the persisted month-wide
//  total, the breaker keeps a lightweight IN-PROCESS per-session accumulator keyed
//  ONLY on the anonymous instanceId (Room.InstanceId - never identity, README
//  section 6). When a single anonymous session's cumulative estimated spend crosses
//  AiOptions.HotSessionThresholdUsd the attribution event is flagged hot=true, so a
//  disproportionately-spending session surfaces in a plain `where hot == 'true'`
//  query in addition to being rate-limited by the per-session quota (story 03). This
//  is a soft VISIBILITY signal, not an enforcement gate; it is per-process (it does
//  not persist), which is fine because it exists only to make concentration visible.
//
//  APP-ESTIMATE ALERTING (AC-10, documented seam): the running monthly estimate this
//  breaker persists (and the estCostUsd metric it emits) is the basis for an OPTIONAL
//  App Insights metric alert at 25 / 50 / 75 / 100% of the ceiling - a faster,
//  PRE-BILLING warning that is DISTINCT from story 06's authoritative Azure Cost
//  Management budget + action-group alerts (the two are reconciled periodically).
//  Wiring the actual alert resource is story 06's Bicep; this story establishes and
//  documents the app-estimate seam (the persisted total + the estCostUsd metric).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using QuibbleStone.Api.Settings;
using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The concrete spend circuit-breaker + cost-attribution recorder (story 04). Reads
/// the persisted UTC-month total to open/close the breaker at the configured ceiling
/// (fail-safe on an unreadable total), and after each real call estimates the cost,
/// accrues the monthly total, and emits one anonymous App Insights attribution event
/// (feature + model + anonymous instanceId + hot flag + token/cost metrics).
/// Registered as the <see cref="IAiSpendGuard"/> in Program.cs when a storage
/// connection string is configured; otherwise the app keeps the in-memory guard.
/// </summary>
public sealed class AiSpendBreaker : IAiSpendGuard
{
    private readonly IMonthlySpendStore _store;
    private readonly AiOptions _options;
    // control-plane/03 (#232): the monthly spend ceiling is read LIVE from here
    // (`ai.spend.monthlyCeilingUsd`, code default 20) on every breaker check, rather than
    // from the once-bound AiOptions.MonthlyCeilingUsd, so an operator can retune the
    // ceiling with no redeploy (AC-05). The per-model rates + hot threshold still come
    // from AiOptions (unmigrated). The settings service's short cache keeps this cheap.
    private readonly IRuntimeSettingsService _settings;
    private readonly TelemetryClient _telemetry;
    private readonly ILogger<AiSpendBreaker> _logger;
    private readonly TimeProvider _timeProvider;

    // In-process per-anonymous-session cumulative estimated spend (AC-08). Keyed ONLY
    // on the anonymous instanceId; used solely to raise the hot flag. Not persisted
    // (a concentration VISIBILITY signal, not an enforcement gate) and bounded in
    // practice by the number of live/recent anonymous rooms.
    private readonly ConcurrentDictionary<string, decimal> _sessionSpendUsd = new(StringComparer.Ordinal);

    /// <summary>
    /// Constructs the breaker over the persisted monthly-total store, the bound AI
    /// options (ceiling + per-model rates + hot threshold), the injected App Insights
    /// <see cref="TelemetryClient"/> (as GameHub takes it), and a logger.
    /// </summary>
    /// <param name="store">The persisted running monthly-total store (Table Storage in production).</param>
    /// <param name="options">The bound AI options (per-model rates + hot threshold; the ceiling now lives on a settings key).</param>
    /// <param name="settings">The runtime settings service the monthly ceiling is read live from (control-plane/03).</param>
    /// <param name="telemetry">The App Insights client the attribution event rides (through the PII scrubber).</param>
    /// <param name="logger">Logs swallowed metering failures server-side (AC-09).</param>
    /// <param name="timeProvider">Clock for the UTC month key; defaults to <see cref="TimeProvider.System"/> (overridable in tests).</param>
    public AiSpendBreaker(
        IMonthlySpendStore store,
        AiOptions options,
        IRuntimeSettingsService settings,
        TelemetryClient telemetry,
        ILogger<AiSpendBreaker> logger,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _options = options;
        _settings = settings;
        _telemetry = telemetry;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<bool> IsUnderCeilingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var total = await _store
                .TryReadMonthTotalUsdAsync(CurrentMonthKey(), cancellationToken)
                .ConfigureAwait(false);

            // FAIL-SAFE (AC-09): an unreadable total is treated as AT-ceiling. We never
            // gamble that there is budget when we cannot see the running total.
            if (total is null)
            {
                _logger.LogWarning("AI spend total unreadable; opening breaker (fail-safe - treating as at-ceiling).");
                return false;
            }

            // control-plane/03 (#232, AC-05): read the CURRENT effective ceiling live so an
            // operator's override governs a NEW check after the settings cache window elapses
            // (no redeploy). The code default (20) keeps a fresh clone identical (AC-01).
            var monthlyCeilingUsd = await _settings
                .GetDecimalAsync(SettingsCatalog.AiSpendMonthlyCeilingUsd, cancellationToken)
                .ConfigureAwait(false);

            // Breaker opens at >= 100% of the configured ceiling (AC-03).
            return total.Value < monthlyCeilingUsd;
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation (a dropped client / shed round) - propagate.
            throw;
        }
        catch (Exception ex)
        {
            // Any unexpected failure ALSO fails to the safe side (AC-09): degrade
            // rather than call AI blind. The store already fails safe on a read error;
            // this is belt-and-braces for anything the store did not catch.
            _logger.LogWarning(ex, "AI spend ceiling check failed; opening breaker (fail-safe - treating as at-ceiling).");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        AiCompletionResult result,
        string feature,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        // AC-01: estimate $ from the returned token usage x the per-model rates.
        var estCostUsd = AiCostEstimator.EstimateUsd(result, _options);

        // AC-02: accrue the persisted monthly total (best-effort). A store failure
        // MUST NOT fault the caller - metering never gates an already-successful call
        // (AC-09) - so we swallow + log, mirroring the ITelemetrySink posture.
        try
        {
            await _store.AddAsync(CurrentMonthKey(), estCostUsd, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "AI spend total write failed for feature {Feature} (swallowed - metering never gates gameplay).",
                feature);
        }

        // AC-08: update the in-process per-anonymous-session accumulator and decide
        // the hot flag. Keyed ONLY on the anonymous instanceId (never identity).
        var hot = UpdateSessionConcentration(instanceId, estCostUsd);

        // AC-05: emit EXACTLY ONE anonymous attribution event. Best-effort and
        // non-throwing (TrackEvent only enqueues; it no-ops cleanly with no connection
        // string) - a telemetry fault never faults the call (AC-09). The model id
        // falls back to the configured deployment if the result did not carry one.
        var model = string.IsNullOrWhiteSpace(result.ModelId) ? _options.Deployment : result.ModelId;
        try
        {
            _telemetry.TrackEvent(
                AiCostTelemetry.AiCallEvent,
                AiCostTelemetry.BuildProperties(feature, model, instanceId, hot),
                AiCostTelemetry.BuildMetrics(result.InputTokens, result.OutputTokens, estCostUsd));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "AI cost attribution event failed for feature {Feature} (swallowed - telemetry never gates gameplay).",
                feature);
        }
    }

    /// <summary>
    /// Adds this call's estimate to the anonymous session's in-process running total
    /// and returns whether that total has crossed the hot threshold (AC-08). A blank
    /// instance id is not accumulated (nothing anonymous to group on) and is never hot.
    /// </summary>
    private bool UpdateSessionConcentration(string instanceId, decimal estCostUsd)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        var sessionTotal = _sessionSpendUsd.AddOrUpdate(
            instanceId,
            estCostUsd,
            (_, previous) => previous + estCostUsd);

        return sessionTotal >= _options.HotSessionThresholdUsd;
    }

    /// <summary>
    /// The current UTC month row key ("YYYY-MM"). Keying by UTC month is what makes a
    /// new month's total reset to zero naturally (a fresh row), so the breaker closes
    /// and AI resumes at the month boundary (AC-03).
    /// </summary>
    private string CurrentMonthKey() =>
        _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
