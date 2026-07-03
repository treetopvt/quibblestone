// ----------------------------------------------------------------------------
//  FoundryAiCompletionClient - the REAL server-side AI transport (ai-cost-gate/01).
//
//  WHAT THIS IS: the Azure AI Foundry (Azure OpenAI) implementation of
//  IAiCompletionClient. It is the ONE place in the codebase that talks to an AI
//  provider. Registered by Program.cs ONLY when `Ai:Endpoint` is configured
//  (the config-presence branch mirroring ITelemetrySink); with no config the
//  no-op client stands in its place (AC-04).
//
//  WHY SERVER-SIDE (AC-01): the provider credential lives here, in-process, sourced
//  from the App Service managed identity (preferred) or a Key Vault-backed key -
//  NEVER the browser, NEVER a VITE_* var. The web app cannot reach the model.
//
//  AUTH (AC-03): prefer DefaultAzureCredential (managed identity / RBAC, no secret
//  to leak). If an optional `Ai:ApiKey` is configured (a Key Vault-backed app
//  setting), fall back to AzureKeyCredential. Either way the secret is config, never
//  a committed literal.
//
//  RESILIENCE - FAIL SOFT (AC-06): AI is a nicety, never load-bearing for a round.
//  A provider timeout / error / rate-limit yields AiCompletionResult.Unavailable,
//  NEVER an unhandled exception into gameplay. A sane per-call timeout (AiOptions.
//  TimeoutSeconds) caps every call and is linked to the caller's CancellationToken
//  (AC-05) so a dropped client or a shed round cancels the provider call. At MOST
//  ONE bounded retry (a single transient jitter) - the spend circuit-breaker
//  (story 04) is the real spend guard, so we do NOT retry-storm and amplify cost.
//
//  TOKEN USAGE (AC-02): the provider returns InputTokenCount / OutputTokenCount on
//  the completion; we surface them on OUR AiCompletionResult so story 04 can
//  estimate $/call. The usage is not buried in the SDK response.
//
//  GENERIC (AC-07): this transport knows nothing about jumbles / word banks. It
//  maps a generic AiCompletionRequest to system + user chat messages and returns
//  the text. The jumble prompt/parsing lives in ai-on-demand-generation/05.
//
//  CONSUMERS GO THROUGH THE GATE: a real AI feature calls GatedAiCompletionClient
//  (entitlement -> quota -> spend ceiling -> THIS -> record -> moderate), never
//  this transport directly.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace QuibbleStone.Api.Ai;

/// <summary>
/// The Foundry-backed AI transport (AC-01/02/03/05/06). Constructed only when
/// `Ai:Endpoint` is present; talks to Azure OpenAI via <c>Azure.AI.OpenAI</c>,
/// authenticating with managed identity (preferred) or an optional Key Vault-backed
/// key. Fails soft to <see cref="AiCompletionResult.Unavailable"/> on any provider
/// fault. Stateless after construction (holds one <see cref="ChatClient"/>), so it
/// is registered as a singleton.
/// </summary>
public sealed class FoundryAiCompletionClient : IAiCompletionClient
{
    private readonly ChatClient _chatClient;
    private readonly AiOptions _options;
    private readonly ILogger<FoundryAiCompletionClient> _logger;

    /// <summary>
    /// Builds the client from validated <see cref="AiOptions"/> (Program.cs only
    /// constructs this when <see cref="AiOptions.Endpoint"/> is non-empty). Picks
    /// the credential: an <see cref="AzureKeyCredential"/> when
    /// <see cref="AiOptions.ApiKey"/> is configured, else
    /// <see cref="DefaultAzureCredential"/> (the managed-identity path).
    /// </summary>
    /// <param name="clientOptions">
    /// Optional SDK client options - null in production (the SDK's defaults). This is
    /// a TEST SEAM: a test injects a capturing transport here to assert the exact
    /// request body on the wire (e.g. that the token cap ships as
    /// <c>max_completion_tokens</c>, not the model-rejected <c>max_tokens</c>).
    /// </param>
    public FoundryAiCompletionClient(
        AiOptions options,
        ILogger<FoundryAiCompletionClient> logger,
        AzureOpenAIClientOptions? clientOptions = null)
    {
        _options = options;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            // Defensive: Program.cs guards this, but never construct a real client
            // against a missing endpoint.
            throw new InvalidOperationException(
                "FoundryAiCompletionClient requires Ai:Endpoint to be configured. With no endpoint, Program.cs registers NoOpAiCompletionClient instead (AC-04).");
        }

        var endpoint = new Uri(options.Endpoint);

        AzureOpenAIClient azureClient = string.IsNullOrWhiteSpace(options.ApiKey)
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential(), clientOptions)
            : new AzureOpenAIClient(endpoint, new AzureKeyCredential(options.ApiKey), clientOptions);

        _chatClient = azureClient.GetChatClient(options.Deployment);
    }

    /// <inheritdoc />
    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(request.SystemInstruction),
            new UserChatMessage(request.Prompt),
        };

        var chatOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = request.MaxOutputTokens,
        };

        // Emit the token cap as `max_completion_tokens`, NOT the legacy `max_tokens`.
        // Reasoning-era models (gpt-5-mini, o-series) REJECT `max_tokens` with a 400
        // ("Unsupported parameter ... Use 'max_completion_tokens' instead"), and by
        // default Azure.AI.OpenAI still serializes MaxOutputTokenCount as `max_tokens`
        // for back-compat. This opt-in (per-request, on THIS SDK's AzureChatExtensions)
        // flips it to the new field so the gated call actually reaches the model. The
        // AOAI001 suppression is the SDK's "evaluation-only API" gate on that method -
        // deliberate: it is the sole supported way to send the modern field today.
#pragma warning disable AOAI001
        chatOptions.SetNewMaxCompletionTokensPropertyEnabled(true);
#pragma warning restore AOAI001

        // At MOST one bounded retry (attempt 0, then a single retry). The breaker is
        // the spend guard, not retries (AC-06) - so this never becomes a retry storm.
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // If the ORIGINAL caller cancelled (dropped client / shed round), stop
            // now - do not spend another attempt (AC-05).
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("AI completion cancelled by caller before attempt {Attempt}; returning Unavailable.", attempt);
                return AiCompletionResult.Unavailable;
            }

            // Per-ATTEMPT timeout (AC-06) linked to the caller's token (AC-05): each
            // attempt gets its OWN fresh budget, so the one bounded retry after a
            // timeout is a real second try, not an instant no-op on an already-elapsed
            // timer (Gate-1 review S-1). Whichever token fires first cancels the call
            // and the whole thing fails soft.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

            try
            {
                var result = await _chatClient.CompleteChatAsync(messages, chatOptions, timeoutCts.Token).ConfigureAwait(false);
                var completion = result.Value;

                var text = completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;

                return new AiCompletionResult(
                    Text: text,
                    InputTokens: completion.Usage?.InputTokenCount ?? 0,
                    OutputTokens: completion.Usage?.OutputTokenCount ?? 0,
                    ModelId: _options.Deployment,
                    IsAvailable: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller cancelled - not a provider fault. Fail soft, no retry.
                _logger.LogDebug("AI completion cancelled by caller; returning Unavailable (AC-05).");
                return AiCompletionResult.Unavailable;
            }
            catch (Exception ex) when (ex is OperationCanceledException or RequestFailedException or TimeoutException)
            {
                // Provider timeout / error / rate-limit. Fail SOFT (AC-06): retry AT
                // MOST once for a transient blip, otherwise surface Unavailable.
                if (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "AI completion attempt {Attempt} failed; one bounded retry (AC-06).", attempt);
                    continue;
                }

                _logger.LogWarning(ex, "AI completion unavailable after {Attempt} attempt(s); returning Unavailable (AC-06).", attempt);
                return AiCompletionResult.Unavailable;
            }
            catch (Exception ex)
            {
                // Any other provider-side fault: still fail soft into gameplay (AC-06).
                // No retry for an unexpected fault - we do not know it is transient.
                _logger.LogWarning(ex, "AI completion failed unexpectedly; returning Unavailable (AC-06).");
                return AiCompletionResult.Unavailable;
            }
        }

        // Unreachable in practice (the loop returns on every path), but keeps the
        // method total and honest.
        return AiCompletionResult.Unavailable;
    }
}
