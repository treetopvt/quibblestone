// ----------------------------------------------------------------------------
//  ClosedAiSpendGuard - the always-OPEN-breaker fail-safe spend guard (ai-cost-gate
//  integration, Gate-1 review WARN-001 on story 04).
//
//  WHY THIS EXISTS: the real spend breaker (AiSpendBreaker, story 04) needs the
//  persisted monthly total in Table Storage to enforce the $20 ceiling. That store
//  is selected by the presence of Telemetry:StorageConnectionString. The AI
//  TRANSPORT, however, is selected INDEPENDENTLY by the presence of Ai:Endpoint. So
//  there is a partial-provisioning state - a real Ai:Endpoint configured but NO
//  storage connection - where the real Foundry client would ship BILLABLE calls
//  while the spend guard fell back to the always-open NoOpAiSpendGuard: real AI with
//  NO ceiling. That is a fail-OPEN money hole and it contradicts the charter's
//  load-bearing rule (the moment any AI call ships, it goes behind the cost gate,
//  including the spend circuit-breaker) and story 04's own fail-to-the-safe-side
//  posture (AC-09).
//
//  This guard closes that hole: when a real AI endpoint is configured but the spend
//  total cannot be tracked (no storage), Program.cs registers THIS guard instead of
//  the NoOp. It reports the breaker OPEN (IsUnderCeilingAsync => false), so the gate
//  degrades every AI call to the deterministic fallback rather than call AI with no
//  ceiling. It is the same "unreadable total => treat as at-ceiling" fail-safe the
//  breaker itself applies, hoisted to the misconfiguration case. RecordAsync is a
//  no-op (there is nothing to persist and no call ever reaches it, since the ceiling
//  check always degrades first).
//
//  This is a MISCONFIGURATION guard, not a normal operating mode: the intended
//  deployment provisions both the endpoint and storage together (story 06's Bicep),
//  in which case AiSpendBreaker runs and this guard is never registered.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The fail-safe spend guard for the partial-provisioning state where a real AI
/// endpoint is configured but no spend-total store is (Gate-1 review WARN-001).
/// Reports the breaker permanently OPEN so no ungated AI call can ship, mirroring
/// the breaker's own "unreadable total =&gt; at-ceiling" fail-safe. Registered by
/// Program.cs only when <c>Ai:Endpoint</c> is present and
/// <c>Telemetry:StorageConnectionString</c> is absent.
/// </summary>
public sealed class ClosedAiSpendGuard : IAiSpendGuard
{
    /// <inheritdoc />
    public Task<bool> IsUnderCeilingAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task RecordAsync(AiCompletionResult result, string feature, string instanceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
