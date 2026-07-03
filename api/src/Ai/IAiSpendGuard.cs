// ----------------------------------------------------------------------------
//  IAiSpendGuard - the spend circuit-breaker + cost recorder seam (ai-cost-gate
//  STORY 04, issue #123). Defined here in story 01 as a NO-OP seam so the gate
//  pipeline compiles and runs green with zero config; story 04 replaces the
//  default with the real estimator + monthly-total breaker + attribution event.
//
//  WHAT THIS IS (story 04's job): the THIRD gate stage and the REAL cost control.
//  Two responsibilities:
//    1. IsUnderCeilingAsync - read the running UTC-month spend total (persisted in
//       the already-provisioned Table Storage) BEFORE the transport call; at 100%
//       of the $20 config ceiling, open the breaker and degrade to the fallback for
//       the rest of the month. FAIL to the SAFE side: an unreadable total is
//       treated as AT-ceiling (returns false), never as "plenty of budget".
//    2. RecordAsync - AFTER a real call, estimate its cost from story 01's returned
//       token usage + the per-model rates in AiOptions ((in*inRate + out*outRate)/
//       1e6), add it to the persisted monthly total (ETag-safe), and emit ONE
//       App Insights attribution event (feature tag + model + token counts + est
//       cost + the anonymous Room.InstanceId, through the PII scrubber).
//
//  WHY THE BREAKER, NOT RETRIES, IS THE SPEND GUARD: the transport (story 01)
//  retries at most once; the real defense against a runaway is THIS ceiling.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The spend circuit-breaker + cost recorder (story 04). The gate calls
/// <see cref="IsUnderCeilingAsync"/> before the transport (open breaker =&gt;
/// fallback) and <see cref="RecordAsync"/> after a real call (estimate + persist +
/// attribute). Story 04 supplies the Table Storage monthly total, the estimator,
/// and the AiCostTelemetry event; story 01 ships only the contract + the
/// <see cref="NoOpAiSpendGuard"/> default.
/// </summary>
public interface IAiSpendGuard
{
    /// <summary>
    /// Returns true if the running monthly AI spend is UNDER the configured ceiling
    /// (the call may proceed), false if the breaker is open (degrade to the
    /// deterministic fallback). FAIL-SAFE (story 04 AC): if the total cannot be read,
    /// return false (treat as at-ceiling) - never gamble that there is budget.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the (Table Storage) read.</param>
    Task<bool> IsUnderCeilingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the cost + attribution of ONE completed AI call (story 04). Estimates
    /// $ from <paramref name="result"/>'s token usage + per-model rates, adds it to
    /// the persisted UTC-month total, and emits ONE anonymous attribution event.
    /// Best-effort: a recording failure MUST NOT fault the caller (metering never
    /// gates gameplay) - mirrors the ITelemetrySink fire-and-forget posture.
    /// </summary>
    /// <param name="result">The completed call's result (carries the token usage + model id story 04 costs).</param>
    /// <param name="feature">The feature tag for attribution ("jumble", later "verdict" / "on-demand"). Ships from day one even though only the jumble exists.</param>
    /// <param name="instanceId">The anonymous session/room instance id (Room.InstanceId). NO PII (README section 6).</param>
    /// <param name="cancellationToken">Cancellation for the (Table Storage) write.</param>
    Task RecordAsync(AiCompletionResult result, string feature, string instanceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The NO-OP default spend guard (story 01 seam only): always reports under the
/// ceiling, and <see cref="RecordAsync"/> is a no-op. It exists ONLY so the gate
/// pipeline compiles and runs green before story 04 lands - it is NOT a shippable
/// spend control and story 04 (#123) REPLACES it with the real monthly-total
/// breaker + estimator + attribution. Registered as the default
/// <see cref="IAiSpendGuard"/> in Program.cs until then.
/// </summary>
public sealed class NoOpAiSpendGuard : IAiSpendGuard
{
    /// <inheritdoc />
    public Task<bool> IsUnderCeilingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task RecordAsync(AiCompletionResult result, string feature, string instanceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
