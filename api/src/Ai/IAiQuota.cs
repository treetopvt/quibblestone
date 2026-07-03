// ----------------------------------------------------------------------------
//  IAiQuota - the per-anonymous-session AI quota seam (ai-cost-gate STORY 03,
//  issue #122). Defined here in story 01 as a PASS-THROUGH seam so the gate
//  pipeline compiles and runs green with zero config; story 03 replaces the
//  default with the real per-session counter + per-IP rate limiter.
//
//  WHAT THIS IS (story 03's job): the "how many Fresh Runes left this session?"
//  meter - the SECOND gate stage, checked AFTER entitlement (story 02) and BEFORE
//  the transport call. It is distinct from entitlement (unlocked / not) and from
//  the spend breaker ($ ceiling): quota answers per-session fairness. It keys ONLY
//  on the anonymous Room.InstanceId (README section 6, no PII) - never a nickname,
//  join code, or account.
//
//  FAIL-SAFE (story 03 AC): an unreadable quota falls back to DENY, never to
//  "unlimited". The default here is the opposite (always allow) ONLY because it is
//  the no-op seam before story 03 lands; it must NOT ship to a real AI consumer.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The per-anonymous-session AI quota check (story 03). The gate calls
/// <see cref="TryConsume"/> once per AI request, passing the anonymous session id
/// (Room.InstanceId); it atomically consumes one unit and reports whether the call
/// may proceed plus how many remain (for the client meter). Story 03 supplies the
/// real per-session counter (in-memory is fine - only the spend total must persist)
/// and wires the ASP.NET per-IP rate limiter alongside. Story 01 ships only the
/// contract + the <see cref="UnlimitedAiQuota"/> pass-through default.
/// </summary>
public interface IAiQuota
{
    /// <summary>
    /// Attempts to consume one AI unit for the given anonymous session. Returns an
    /// allow/deny decision plus the remaining count. Fail-SAFE: story 03's real
    /// implementation returns <see cref="AiQuotaDecision.Denied"/> when the quota
    /// cannot be read (never silently unlimited).
    /// </summary>
    /// <param name="instanceId">The anonymous session/room instance id (Room.InstanceId). NEVER a nickname / join code / account (README section 6).</param>
    /// <returns>Whether the call is allowed and how many units remain this session.</returns>
    AiQuotaDecision TryConsume(string instanceId);
}

/// <summary>
/// The outcome of an <see cref="IAiQuota.TryConsume"/> check: whether the AI call
/// may proceed and how many units remain this session (surfaced on the gate result
/// envelope for the "N Fresh Runes left" meter). Story 03 may refine the exact
/// shape; this is the minimum the pipeline needs.
/// </summary>
/// <param name="Allowed">True if the call may proceed; false if this session is out of quota.</param>
/// <param name="Remaining">Units left this session AFTER this consume (for the client meter).</param>
public readonly record struct AiQuotaDecision(bool Allowed, int Remaining)
{
    /// <summary>A shared "denied, none remaining" decision - the fail-safe default state story 03 returns when the quota is exhausted or unreadable.</summary>
    public static AiQuotaDecision Denied { get; } = new(false, 0);
}

/// <summary>
/// The PASS-THROUGH default quota (story 01 seam only): always allows, reports
/// <see cref="int.MaxValue"/> remaining. It exists ONLY so the gate pipeline
/// compiles and runs green before story 03 lands - it is NOT a shippable quota and
/// story 03 (#122) REPLACES it with the real per-session counter + per-IP rate
/// limiter. Registered as the default <see cref="IAiQuota"/> in Program.cs until
/// then.
/// </summary>
public sealed class UnlimitedAiQuota : IAiQuota
{
    /// <inheritdoc />
    public AiQuotaDecision TryConsume(string instanceId) => new(true, int.MaxValue);
}
