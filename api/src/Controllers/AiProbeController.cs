// ----------------------------------------------------------------------------
//  AiProbeController - a THROWAWAY diagnostic to measure ONE real AI call end to
//  end against the live provider (ADR 0001: "measure one real call and confirm the
//  token-usage cost estimate", which the planning spike could not do with no creds).
//
//  WHY THIS EXISTS (and why it is temporary): the AI cost gate ships before its
//  first real consumer (the Fresh Runes jumble, ai-on-demand-generation/05), so
//  there is no product surface that exercises a live gpt-5-mini call yet. This probe
//  is a stand-in so the owner can confirm, against the deployed UAT footprint
//  (keyless managed identity -> Azure OpenAI), that:
//    1. the keyless Foundry call actually works (managed identity + endpoint config),
//    2. the returned token usage x the configured rates matches the cost estimate
//       (AiCostEstimator - the number the $20 breaker enforces on), and
//    3. the whole gate chain runs (quota -> spend-ceiling -> transport -> record +
//       attribution -> moderation) and degrades gracefully.
//  DELETE THIS FILE when ai-on-demand-generation/05 lands - the real consumer
//  supersedes it, and a permanent AI-triggering endpoint is not something the
//  product should carry.
//
//  SAFETY (it is not a back door):
//    - OFF BY DEFAULT: it 404s unless `Ai:EnableProbe` is explicitly true (a deploy
//      input, off in appsettings). So it is invisible in any environment that has
//      not deliberately turned it on to run a one-off measurement.
//    - It routes through the SAME GatedAiCompletionClient every real consumer must
//      (no bypass), so even when enabled it is quota-limited, breaker-gated, and its
//      output is moderated - a stranger who found it could not run up the bill.
//    - It carries the AI per-IP rate-limit policy (ai-cost-gate/03), which until now
//      had no HTTP endpoint to bite on - this is the first surface that exercises it.
//    - Anonymous: it keys the gate on a transient "probe" instance id, never PII.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Controllers;

/// <summary>
/// THROWAWAY diagnostic endpoint (ADR 0001 "measure one real call"). Config-gated
/// OFF by default (`Ai:EnableProbe`); routes through the real gate so it is metered
/// + moderated; carries the AI per-IP rate-limit policy. Remove when
/// ai-on-demand-generation/05 (the first real consumer) lands.
/// </summary>
[ApiController]
[Route("api/ai/probe")]
[EnableRateLimiting(Program.AiPerIpRateLimitPolicy)]
public sealed class AiProbeController : ControllerBase
{
    // A fixed, benign, family-safe payload so the probe is deterministic and never
    // carries player free text. Small max-output keeps the measured call cheap.
    private const string ProbeSystemInstruction =
        "You are the Stonecarver, a warm family word game guide. Reply with only a comma-separated list of short, whimsical, family-safe fantasy words - no sentences, no explanations.";
    private const string ProbePrompt = "Give 8 short, family-safe, on-theme fantasy words for a word-bank round.";
    private const int ProbeMaxOutputTokens = 64;

    private readonly IAiCompletionClient _transport;
    private readonly GatedAiCompletionClient _gate;
    private readonly AiOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiProbeController> _logger;

    public AiProbeController(
        IAiCompletionClient transport,
        GatedAiCompletionClient gate,
        AiOptions options,
        IConfiguration configuration,
        ILogger<AiProbeController> logger)
    {
        _transport = transport;
        _gate = gate;
        _options = options;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/ai/probe - makes one raw transport call (for the token-usage + cost
    /// measurement the ADR asks for) and one full gated call (to prove the chain +
    /// moderation), and returns both. 404s unless `Ai:EnableProbe` is true. POST (not
    /// GET) so a browser prefetch/refresh can never silently trigger a paid call.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Probe(CancellationToken cancellationToken)
    {
        // OFF BY DEFAULT: invisible unless a deploy explicitly enabled the probe.
        if (!_configuration.GetValue<bool>("Ai:EnableProbe"))
        {
            return NotFound();
        }

        var request = new AiCompletionRequest(
            SystemInstruction: ProbeSystemInstruction,
            Prompt: ProbePrompt,
            MaxOutputTokens: ProbeMaxOutputTokens);

        // 1. RAW transport call - the ADR measurement. AiCompletionResult surfaces the
        //    input/output token counts + model id on OUR type, so we can confirm the
        //    estimate deterministically ((in*inRate + out*outRate)/1e6).
        var raw = await _transport.CompleteAsync(request, cancellationToken);
        var estCostUsd = AiCostEstimator.EstimateUsd(raw, _options);

        // 2. FULL gated call - proves the whole chain runs and degrades gracefully.
        //    Keyed on a transient anonymous "probe" instance id (never PII). feature
        //    "probe" keeps these calls separable from real "jumble" cost in App Insights.
        var probeInstanceId = "probe-" + Guid.NewGuid().ToString("N");
        var gated = await _gate.CompleteGatedAsync(
            request,
            instanceId: probeInstanceId,
            feature: "probe",
            familySafe: true,
            cancellationToken);

        _logger.LogInformation(
            "AI probe: available={Available} model={Model} inTok={In} outTok={Out} estUsd={Est} gatedFellBack={FellBack}",
            raw.IsAvailable, raw.ModelId, raw.InputTokens, raw.OutputTokens, estCostUsd, gated.FellBack);

        return Ok(new
        {
            // The measured single call (ADR): confirm tokens x rates == estimate.
            rawCall = new
            {
                available = raw.IsAvailable,
                modelId = raw.ModelId,
                inputTokens = raw.InputTokens,
                outputTokens = raw.OutputTokens,
                estCostUsd,
                inputRatePerMillion = _options.InputCostPerMillion,
                outputRatePerMillion = _options.OutputCostPerMillion,
                text = raw.Text,
            },
            // The full gate chain: quota -> ceiling -> transport -> record -> moderate.
            gatedCall = new
            {
                isAvailable = gated.IsAvailable,
                fellBack = gated.FellBack,
                remainingQuota = gated.RemainingQuota,
                moderatedOutput = gated.Output,
            },
            note = "Throwaway diagnostic (ADR 0001 'measure one real call'). Off unless Ai:EnableProbe=true. Remove when ai-on-demand-generation/05 ships.",
        });
    }
}
