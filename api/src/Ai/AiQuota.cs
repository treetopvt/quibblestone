// ----------------------------------------------------------------------------
//  AiQuota - the REAL per-anonymous-session AI quota (ai-cost-gate STORY 03,
//  issue #122). Replaces the story-01 pass-through seam (UnlimitedAiQuota).
//
//  WHAT THIS IS: the "how many Fresh Runes left this session?" meter - the SECOND
//  gate stage (GatedAiCompletionClient calls TryConsume AFTER entitlement/02 and
//  BEFORE the transport/01). It enforces per-session fairness server-side so a
//  client cannot exceed its allowance by replaying requests (AC-01). It is DISTINCT
//  from entitlement (unlocked / not, story 02) and from the monthly spend breaker
//  ($ ceiling, story 04): quota is a per-session CALL COUNT, nothing else (AC-06).
//
//  KEY = the anonymous Room.InstanceId ONLY (AC-04, README section 6): an opaque
//  per-room GUID, never a nickname / join code / account / PII. Clearing this state
//  loses nothing about a person (there is no person recorded). The per-IP abuse
//  guard (AC-03) is a SEPARATE concern wired in Program.cs via ASP.NET Core's
//  AddRateLimiter (a transient IP bucket, never stored here, never logged); this
//  class holds no IP.
//
//  METER: TryConsume decrements and returns the server-authoritative Remaining (the
//  "N Fresh Runes left" figure GatedAiCompletionClient threads onto
//  AiGateResult.RemainingQuota). The client DISPLAYS it and soft-disables at zero -
//  it never decides it (AC-02).
//
//  FAIL-SAFE (AC-07, non-negotiable): an unreadable / unusable quota state returns
//  AiQuotaDecision.Denied (allow=false, remaining=0). It NEVER fails open into
//  unlimited calls and NEVER throws - a throw would break the round; a deny cleanly
//  degrades the caller to the deterministic fallback (AC-05).
//
//  STATE: in-memory only (AC / Out of Scope: exactness across a restart is not
//  required - only the monthly SPEND total, story 04, must persist). It mirrors
//  RoomRegistry's process-wide singleton posture; registered as a singleton in
//  Program.cs so every transient GameHub invocation shares the one counter.
//
//  THREAD-SAFETY: SignalR invokes hub methods concurrently across connections, so
//  the counter is guarded by a lock (mirroring Room's per-instance _gate lock). The
//  consume (read-check-decrement) is one atomic step under the lock, so two
//  concurrent calls for the same session can never both spend the last unit.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The real per-anonymous-session AI quota (story 03). Enforces a per-session allowance
/// keyed on the anonymous Room.InstanceId, server-side and before the transport, and
/// reports the remaining count for the client meter. control-plane/03 (#232) reads that
/// allowance LIVE from <see cref="IRuntimeSettingsService"/> (<c>ai.quota.perSession</c>,
/// code default 20) when a new session's counter is established, rather than capturing
/// <see cref="AiOptions.QuotaPerSession"/> once at construction - so an operator can
/// retune it with no redeploy (AC-05). In-memory, thread-safe, and fail-safe (deny on any
/// error). Registered as a singleton in Program.cs, replacing <see cref="UnlimitedAiQuota"/>.
/// </summary>
public sealed class AiQuota : IAiQuota
{
    // Guards the per-session remaining-count map. A single lock (rather than a
    // ConcurrentDictionary) so the read-check-decrement is ONE atomic step - two
    // concurrent consumes for the same session can never both spend the last unit.
    // Mirrors Room's per-instance _gate lock posture.
    private readonly object _gate = new();

    // Key = anonymous Room.InstanceId; value = units STILL REMAINING this session
    // (not units consumed), so a read is the meter value directly. Ordinal compare -
    // the id is an opaque GUID string. Never holds any PII (AC-04).
    private readonly Dictionary<string, int> _remaining = new(StringComparer.Ordinal);

    // control-plane/03 (#232): the per-session allowance N is no longer captured once at
    // construction - it is read LIVE from IRuntimeSettingsService (`ai.quota.perSession`,
    // code default 20) at the moment a NEW session's counter is established, so an
    // operator's override governs the next new session with no redeploy (AC-05). A session
    // already counting keeps its established allowance (no retroactive change to a session
    // in flight). The settings service's short cache keeps this cheap - not a storage
    // round-trip per call.
    private readonly IRuntimeSettingsService _settings;

    private readonly ILogger<AiQuota> _logger;

    public AiQuota(IRuntimeSettingsService settings, ILogger<AiQuota> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<AiQuotaDecision> TryConsumeAsync(string instanceId, CancellationToken ct = default)
    {
        try
        {
            // No usable session key -> we cannot confirm quota -> DENY (AC-07). Never
            // treat a missing key as unlimited. We do NOT log the id value (AC-04).
            if (string.IsNullOrEmpty(instanceId))
            {
                return AiQuotaDecision.Denied;
            }

            // FAST PATH: a session already counting reads its remaining from the map and
            // needs NO settings read - the per-session allowance only matters when a NEW
            // session's counter is established, so an existing session never pays the lookup
            // and a mid-session change never retroactively alters a session in flight (AC-05).
            if (TryConsumeExisting(instanceId, out var existingDecision))
            {
                return existingDecision;
            }

            // MISS: this session has no counter yet. Read the CURRENT effective per-session
            // allowance live (AC-05), OUTSIDE the lock (no awaiting under a lock). Clamped at
            // >= 0: a misconfigured / drifted non-positive value never becomes "unlimited" -
            // it means "no allowance" (the fail-safe side).
            var perSessionLimit = Math.Max(
                0, await _settings.GetIntAsync(SettingsCatalog.AiQuotaPerSession, ct).ConfigureAwait(false));

            lock (_gate)
            {
                // Re-check under the lock: a concurrent first-call for the SAME new session
                // may have established the counter while we read settings. If so, count
                // against the established value (its allowance, not ours) so two racing
                // first-calls never double-initialize. Otherwise seed the full allowance.
                if (!_remaining.TryGetValue(instanceId, out var left))
                {
                    left = perSessionLimit;
                }

                if (left <= 0)
                {
                    // A session at or below zero is out - pin it at zero and deny (a clean
                    // deny, never a throw, so the caller degrades to the deterministic fallback).
                    _remaining[instanceId] = 0;
                    return AiQuotaDecision.Denied;
                }

                // Consume one unit atomically; the new remaining is server-authoritative.
                left -= 1;
                _remaining[instanceId] = left;
                return new AiQuotaDecision(Allowed: true, Remaining: left);
            }
        }
        catch (Exception ex)
        {
            // Any internal failure fails SAFE to deny (AC-07), never open to unlimited.
            // Log the fact only - never the instanceId as identity (AC-04, no PII).
            _logger.LogWarning(ex, "AI quota: consume failed; failing safe to deny (degrade to fallback).");
            return AiQuotaDecision.Denied;
        }
    }

    /// <summary>
    /// The lock-guarded fast path for a session that ALREADY has a counter: atomically
    /// consumes one unit (or denies at zero) and returns true, without touching settings.
    /// Returns false for a session not yet seen, so the caller reads the current allowance
    /// and establishes it (the slow path). Keeps the read-check-decrement one atomic step.
    /// </summary>
    private bool TryConsumeExisting(string instanceId, out AiQuotaDecision decision)
    {
        lock (_gate)
        {
            if (!_remaining.TryGetValue(instanceId, out var left))
            {
                decision = default;
                return false;
            }

            if (left <= 0)
            {
                _remaining[instanceId] = 0;
                decision = AiQuotaDecision.Denied;
                return true;
            }

            left -= 1;
            _remaining[instanceId] = left;
            decision = new AiQuotaDecision(Allowed: true, Remaining: left);
            return true;
        }
    }
}
