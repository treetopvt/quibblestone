// ----------------------------------------------------------------------------
//  IAiCompletionClient - the SINGLE server-side seam through which EVERY AI call
//  in QuibbleStone flows (ai-cost-gate story 01, issue #120, the foundation of
//  the whole gate).
//
//  WHAT THIS IS: the transport proxy. When the API needs AI-generated text, it
//  calls THIS interface - never an AI SDK directly, and NEVER from the browser.
//  Because every AI call is ours to see, it becomes ours to meter (stories 03/04)
//  and moderate (story 05). It wraps Azure AI Foundry (Azure OpenAI, gpt-5-mini -
//  ADR 0001 picked gpt-4o-mini, superseded by availability) but the SEAM is
//  provider-agnostic: the model/provider is a swappable config value (AiOptions),
//  not baked into any caller.
//
//  WHY SERVER-SIDE (AC-01, non-negotiable, CLAUDE.md section 4): the provider key
//  lives in Key Vault or the App Service managed identity. NOTHING in web/ ever
//  holds a provider key or calls the AI provider - secrets that ship to the
//  browser (a VITE_* var) are forbidden. The browser asks the API; the API is the
//  only thing that talks to the model.
//
//  WHY GENERIC (AC-07): the request is "system instruction + prompt + max output
//  tokens" and the result is "text + token usage + model id + availability".
//  There is DELIBERATELY no jumble / word-bank / category shape here - that lives
//  in the first CONSUMER (ai-on-demand-generation/05), not in the proxy. Future AI
//  features (verdict, on-demand tales, packs, voices) reuse this same shape. If
//  jumble specifics leak in, that is a smell to flag.
//
//  THE NO-OP CONTRACT (AC-04): two implementations sit behind this one interface -
//    - FoundryAiCompletionClient : the real Foundry-backed transport, registered
//                                  when `Ai:Endpoint` is configured.
//    - NoOpAiCompletionClient    : the DEFAULT (local dev, CI, before provisioning)
//                                  when no AI config is present. It returns a typed
//                                  "unavailable" result cleanly so the app builds +
//                                  runs with ZERO AI config and every consumer falls
//                                  back to its deterministic path.
//  Program.cs picks one at startup on config presence - EXACTLY the ITelemetrySink
//  idiom (Program.cs ~line 123).
//
//  CONSUMERS MUST GO THROUGH THE GATE, NEVER AROUND IT: this transport is only
//  ONE stage. A real AI consumer does NOT call CompleteAsync directly - it calls
//  the GatedAiCompletionClient (this file's sibling), which runs the DOCUMENTED
//  ordered path: entitlement (captured at session-creation, story 02) -> quota
//  (story 03) -> spend-guard ceiling check (story 04) -> THIS transport -> spend
//  record + attribution (story 04) -> moderate output (story 05). Every future AI
//  feature is a CONSUMER of that gate, not a new gate.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The single server-side AI transport seam (ai-cost-gate/01). Callers pass a
/// generic <see cref="AiCompletionRequest"/> and receive an
/// <see cref="AiCompletionResult"/> carrying the generated text AND the call's
/// token usage + model id (AC-02) plus a typed availability flag (AC-06). There is
/// one real implementation (Foundry) and one no-op, resolved from DI by config
/// presence (AC-04). Real consumers route through the gate (see
/// <see cref="GatedAiCompletionClient"/>), never this transport directly.
/// </summary>
public interface IAiCompletionClient
{
    /// <summary>
    /// Completes one generic prompt. Fully async and honors the
    /// <paramref name="cancellationToken"/> (AC-05) so a dropped client or a shed
    /// round does not leak a provider call. Fails SOFT (AC-06): a provider timeout /
    /// error / rate-limit yields <see cref="AiCompletionResult.Unavailable"/> - it
    /// NEVER throws an unhandled exception into gameplay.
    /// </summary>
    /// <param name="request">The generic completion request (system instruction, prompt, max output tokens). No jumble specifics (AC-07).</param>
    /// <param name="cancellationToken">Cancellation for the call (AC-05).</param>
    /// <returns>The generated text plus token usage + model id, or a typed unavailable result.</returns>
    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// A generic AI completion request (AC-07). Deliberately provider- and
/// feature-agnostic: a family-safe system instruction, the user prompt, and a hard
/// cap on output tokens. There is NO word-bank / jumble / category field - the
/// first consumer (ai-on-demand-generation/05) builds those specifics on top of
/// this shape; the proxy stays reusable by every future AI feature.
/// </summary>
/// <param name="SystemInstruction">The system-role instruction (brand voice + family-safe rules). Set by the CONSUMER, applied by the transport.</param>
/// <param name="Prompt">The user-role prompt for this call.</param>
/// <param name="MaxOutputTokens">A hard cap on generated tokens - bounds both latency and per-call cost.</param>
public sealed record AiCompletionRequest(
    string SystemInstruction,
    string Prompt,
    int MaxOutputTokens);

/// <summary>
/// The result of an AI completion. The token usage and model id are surfaced on
/// OUR OWN type (AC-02), NOT buried in the SDK response, so the spend
/// circuit-breaker (story 04) can estimate $ per call deterministically from
/// <see cref="InputTokens"/> / <see cref="OutputTokens"/> + the per-model rates in
/// <see cref="AiOptions"/>. <see cref="IsAvailable"/> is the typed "did we get a
/// real completion?" flag (AC-06): false means the provider was unavailable OR no
/// AI is configured, and the consumer must fall back to its deterministic path.
/// </summary>
/// <param name="Text">The generated text. Empty string when <see cref="IsAvailable"/> is false.</param>
/// <param name="InputTokens">Prompt (input) token count reported by the provider. 0 when unavailable.</param>
/// <param name="OutputTokens">Completion (output) token count reported by the provider. 0 when unavailable.</param>
/// <param name="ModelId">The model / deployment that served the call (e.g. "gpt-5-mini"). Empty when unavailable.</param>
/// <param name="IsAvailable">True only when a real completion came back; false on no-op or any soft failure (AC-06).</param>
public sealed record AiCompletionResult(
    string Text,
    int InputTokens,
    int OutputTokens,
    string ModelId,
    bool IsAvailable)
{
    /// <summary>
    /// The shared typed "AI unavailable" result (AC-04, AC-06): no text, zero token
    /// usage, not available. Returned by the no-op client, and by the real client
    /// whenever a provider timeout / error / rate-limit is caught (fail soft, never
    /// throw into gameplay). Consumers treat this as "fall back to deterministic".
    /// </summary>
    public static AiCompletionResult Unavailable { get; } =
        new(string.Empty, 0, 0, string.Empty, false);
}
