// ----------------------------------------------------------------------------
//  GatedAiCompletionClient - the ONE ordered AI gate pipeline every AI feature
//  routes through (ai-cost-gate, the seam foldeded into story 01's foundation).
//
//  WHAT THIS IS: the orchestrator that composes the DOCUMENTED, ordered gate path.
//  A real AI consumer NEVER calls the raw transport (IAiCompletionClient) directly
//  and NEVER stands up its own quota / breaker / filter - it calls THIS, which runs
//  the stages IN ORDER:
//
//    1. entitlement   - captured at SESSION-CREATION (story 02), UPSTREAM of this
//                       call, carried on the session; NOT re-checked per call here.
//    2. quota         - IAiQuota (story 03): per-anonymous-session "N left" + fail-safe.
//    3. spend ceiling - IAiSpendGuard.IsUnderCeilingAsync (story 04): breaker open =>
//                       fall back; fail-safe (unreadable total = at-ceiling).
//    4. transport     - IAiCompletionClient.CompleteAsync (story 01): the actual call.
//    5. spend record  - IAiSpendGuard.RecordAsync (story 04): estimate $ from the
//                       returned token usage, persist the monthly total, emit ONE
//                       anonymous attribution event.
//    6. moderate      - IAiOutputModerator.ModerateAsync (story 05): drop unsafe /
//                       non-family-safe output BEFORE any child sees it; signal
//                       whether enough safe items survived.
//
//  DEGRADE, NEVER BILL OR BREAK: any stage that says "no" (out of quota, breaker
//  open, transport unavailable, too few safe survivors) lands on ONE graceful
//  outcome - an envelope with FellBack = true and IsAvailable = false, so the
//  consumer runs its deterministic fallback (the jumble reshuffle). No error, no
//  charge, no unmoderated text.
//
//  CONSUMERS, NOT NEW GATES: this whole file is the gate. Every future AI feature
//  (jumble, verdict, on-demand tales, voices, packs) is a CONSUMER of this pipeline.
//  A second proxy / quota / filter anywhere is a smell to flag (implementation.md
//  cross-cutting concerns).
//
//  GENERIC SEAM (story 01 scope): no consumer exists yet in this feature (the
//  jumble is a later feature), so this orchestrator's job here is to establish the
//  ORDER + the ENVELOPE, keyed on the anonymous instanceId. It stays generic - no
//  jumble parsing. The default stages are all no-op/pass-through, so with zero
//  config a gated call returns a clean "unavailable + fell back" envelope.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The composed AI gate pipeline (the ordered path stories 03/04/05 fill in). A
/// consumer calls <see cref="CompleteGatedAsync"/> with a generic
/// <see cref="AiCompletionRequest"/> plus the anonymous session id, a feature tag,
/// and the family-safe flag, and gets back an <see cref="AiGateResult"/> envelope.
/// The four stages are constructor-injected via their interfaces so Wave-2 leaf
/// builders swap in concrete implementations without touching this orchestrator.
/// Registered as a singleton in Program.cs.
/// </summary>
public sealed class GatedAiCompletionClient
{
    private readonly IAiQuota _quota;
    private readonly IAiSpendGuard _spendGuard;
    private readonly IAiCompletionClient _transport;
    private readonly IAiOutputModerator _moderator;
    private readonly ILogger<GatedAiCompletionClient> _logger;

    public GatedAiCompletionClient(
        IAiQuota quota,
        IAiSpendGuard spendGuard,
        IAiCompletionClient transport,
        IAiOutputModerator moderator,
        ILogger<GatedAiCompletionClient> logger)
    {
        _quota = quota;
        _spendGuard = spendGuard;
        _transport = transport;
        _moderator = moderator;
        _logger = logger;
    }

    /// <summary>
    /// Runs the ordered gate path for one AI call. Entitlement (story 02) is assumed
    /// already captured on the session upstream - this method starts at quota. Any
    /// stage that denies lands on a clean fell-back envelope (degrade, never throw).
    /// </summary>
    /// <param name="request">The generic completion request (no jumble specifics - AC-07).</param>
    /// <param name="instanceId">The anonymous session/room id (Room.InstanceId) - the ONLY key quota + attribution use (README section 6, no PII).</param>
    /// <param name="feature">The attribution feature tag ("jumble", later "verdict" / "on-demand").</param>
    /// <param name="familySafe">The round's family-safe toggle - passed to moderation (story 05).</param>
    /// <param name="cancellationToken">Cancellation threaded to every async stage (AC-05).</param>
    public async Task<AiGateResult> CompleteGatedAsync(
        AiCompletionRequest request,
        string instanceId,
        string feature,
        bool familySafe,
        CancellationToken cancellationToken = default)
    {
        // Stage 2: quota (story 03). Per-anonymous-session fairness + fail-safe deny.
        // A leaf stage is contractually required to FAIL SAFE by RETURNING a deny,
        // never by throwing (story 03 AC-07 / story 04 AC-09 / story 05 fail-safe).
        // We defend that invariant here regardless: a stage that throws must still
        // degrade to the fallback, never leak an unhandled exception into gameplay
        // ("degrade, never break" - Gate-1 review S-3). Cancellation is cooperative
        // (a dropped client / shed round) and is allowed to propagate.
        AiQuotaDecision quota;
        try
        {
            quota = _quota.TryConsume(instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI gate: quota stage threw for feature {Feature}; failing safe to fallback.", feature);
            return AiGateResult.FellBackWith(0);
        }
        if (!quota.Allowed)
        {
            _logger.LogDebug("AI gate: quota exhausted for feature {Feature}; falling back.", feature);
            return AiGateResult.FellBackWith(quota.Remaining);
        }

        try
        {
            // Stage 3: spend ceiling (story 04). Breaker open => fall back. Fail-safe:
            // the guard treats an unreadable total as at-ceiling and returns false.
            if (!await _spendGuard.IsUnderCeilingAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("AI gate: spend ceiling reached for feature {Feature}; falling back.", feature);
                return AiGateResult.FellBackWith(quota.Remaining);
            }

            // Stage 4: transport (story 01). Fails soft to Unavailable, never throws.
            var completion = await _transport.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            if (!completion.IsAvailable)
            {
                _logger.LogDebug("AI gate: transport unavailable for feature {Feature}; falling back.", feature);
                return AiGateResult.FellBackWith(quota.Remaining);
            }

            // Stage 5: spend record + attribution (story 04). Best-effort - a recording
            // failure NEVER discards the already-successful call and never blocks the
            // response (metering does not gate gameplay). Guarded in its OWN try so a
            // record throw cannot fall back a result we already have in hand.
            try
            {
                await _spendGuard.RecordAsync(completion, feature, instanceId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI gate: spend-record stage failed for feature {Feature} (swallowed - metering never gates gameplay).", feature);
            }

            // Stage 6: moderate output (story 05). Split the completion into items and
            // vet every one BEFORE returning; too few safe survivors => fall back. The
            // splitting here is a generic newline split - a real consumer (ai-on-demand-
            // generation/05) parses its own payload shape, then re-moderates through the
            // same seam; this keeps the orchestrator generic while proving the ordering.
            var items = SplitItems(completion.Text);
            var moderation = await _moderator.ModerateAsync(items, familySafe, cancellationToken).ConfigureAwait(false);
            if (!moderation.Sufficient)
            {
                _logger.LogDebug("AI gate: insufficient safe output for feature {Feature}; falling back.", feature);
                return AiGateResult.FellBackWith(quota.Remaining);
            }

            return new AiGateResult(
                IsAvailable: true,
                RemainingQuota: quota.Remaining,
                FellBack: false,
                Output: moderation.Safe)
            {
                // Diagnostic usage (for the probe's measurement); anonymous token counts.
                InputTokens = completion.InputTokens,
                OutputTokens = completion.OutputTokens,
                ModelId = completion.ModelId,
            };
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation (dropped client / shed round) - propagate it
            // rather than masking it as a fallback (AC-05); the caller is already gone.
            throw;
        }
        catch (Exception ex)
        {
            // A spend-guard, transport, or moderator stage threw despite the fail-safe
            // contract. Degrade to the graceful fallback rather than break the round.
            _logger.LogWarning(ex, "AI gate: a gate stage threw for feature {Feature}; failing safe to fallback.", feature);
            return AiGateResult.FellBackWith(quota.Remaining);
        }
    }

    /// <summary>
    /// Generic newline split into non-empty, trimmed items. Deliberately payload-
    /// agnostic (no jumble/JSON parsing - AC-07); a real consumer parses its own
    /// shape and re-moderates through the same seam.
    /// </summary>
    private static IReadOnlyList<string> SplitItems(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}

/// <summary>
/// The envelope returned by <see cref="GatedAiCompletionClient.CompleteGatedAsync"/>.
/// Carries whether a usable, moderated AI result came back
/// (<see cref="IsAvailable"/>), the remaining per-session quota for the meter
/// (<see cref="RemainingQuota"/>), whether the pipeline degraded to the fallback
/// (<see cref="FellBack"/>), and the moderated output items. On any fall-back
/// <see cref="IsAvailable"/> is false, <see cref="FellBack"/> is true, and
/// <see cref="Output"/> is empty - the consumer runs its deterministic path.
/// </summary>
/// <param name="IsAvailable">True only when a usable, moderated AI result is present.</param>
/// <param name="RemainingQuota">Per-session units left (for the "N left" meter).</param>
/// <param name="FellBack">True when any gate stage degraded to the fallback.</param>
/// <param name="Output">The moderated, safe-to-display items. Empty on fall-back.</param>
public sealed record AiGateResult(
    bool IsAvailable,
    int RemainingQuota,
    bool FellBack,
    IReadOnlyList<string> Output)
{
    /// <summary>
    /// DIAGNOSTIC only (not needed by real consumers): the input token count of the
    /// underlying call, surfaced so the throwaway probe can confirm the token-usage
    /// cost estimate WITHOUT a raw, gate-bypassing transport call (PR #132 review).
    /// 0 on any fall-back. Anonymous - a token count, never content.
    /// </summary>
    public int InputTokens { get; init; }

    /// <summary>DIAGNOSTIC only: the output token count of the underlying call. 0 on fall-back.</summary>
    public int OutputTokens { get; init; }

    /// <summary>DIAGNOSTIC only: the model id the underlying call used. Empty on fall-back.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>
    /// Builds the graceful fall-back envelope (degrade, never break): not available,
    /// fell back, no output, carrying the remaining quota so the meter stays honest.
    /// The diagnostic usage fields stay at their zero/empty defaults.
    /// </summary>
    public static AiGateResult FellBackWith(int remainingQuota) =>
        new(IsAvailable: false, RemainingQuota: remainingQuota, FellBack: true, Output: Array.Empty<string>());
}
