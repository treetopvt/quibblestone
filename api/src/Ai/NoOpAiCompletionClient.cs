// ----------------------------------------------------------------------------
//  NoOpAiCompletionClient - the DEFAULT AI transport for zero-config runs
//  (ai-cost-gate/01 AC-04).
//
//  QuibbleStone builds and runs with ZERO AI configuration (local dev, CI, a
//  fresh clone, or before the Foundry resource is provisioned by story 06). When
//  no `Ai:Endpoint` is present, Program.cs registers THIS client instead of the
//  Foundry-backed one - EXACTLY mirroring the ITelemetrySink config-presence
//  branch (Program.cs ~line 123). It reports "AI unavailable" cleanly on every
//  call, so every consumer falls back to its deterministic path and nothing in the
//  app breaks for want of an AI key.
//
//  It exists so callers can depend on IAiCompletionClient UNCONDITIONALLY - they
//  never branch on "is AI configured?"; they call CompleteAsync and, when it comes
//  back not-available, degrade gracefully (the jumble reshuffles deterministically).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The no-op AI transport (AC-04): returns <see cref="AiCompletionResult.Unavailable"/>
/// (IsAvailable = false) on every call, without ever throwing or blocking. The
/// DEFAULT when no `Ai:*` config is present, so the app runs with zero Azure /
/// zero AI setup and consumers fall back deterministically.
/// </summary>
public sealed class NoOpAiCompletionClient : IAiCompletionClient
{
    private readonly ILogger<NoOpAiCompletionClient> _logger;

    public NoOpAiCompletionClient(ILogger<NoOpAiCompletionClient> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        // No AI is configured. Log at Debug (the prompt is not echoed - it is a
        // player-adjacent surface) and return the typed unavailable result so the
        // consumer degrades to its deterministic path (AC-04).
        _logger.LogDebug(
            "AI completion (no-op client): AI is not configured; returning Unavailable (maxOutputTokens={MaxOutputTokens}).",
            request.MaxOutputTokens);

        return Task.FromResult(AiCompletionResult.Unavailable);
    }
}
