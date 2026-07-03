// ----------------------------------------------------------------------------
//  FoundryAiCompletionClientTests - the real transport FAILS SOFT (ai-cost-gate/01
//  AC-06).
//
//  AI is a nicety, never load-bearing: a provider timeout / error / rate-limit must
//  surface the TYPED unavailable result, NEVER an unhandled exception into
//  gameplay. This drives the real client against an unreachable endpoint (a bogus
//  host + a dummy key so it takes the AzureKeyCredential path and never needs live
//  Azure), with a short per-call timeout, and asserts it returns
//  AiCompletionResult.Unavailable without throwing. It exercises the client's own
//  catch/fail-soft path - a seam, not a live provider.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Tests.Ai;

public class FoundryAiCompletionClientTests
{
    [Fact]
    public async Task Request_uses_max_completion_tokens_and_minimal_reasoning_for_gpt5()
    {
        // Two gpt-5-mini gotchas, both asserted on the exact wire body via a capturing
        // transport (the layer the gate / IAiCompletionClient stubs never touch, which
        // is why both once reached UAT unseen and silently fell every jumble back):
        //   1. `max_completion_tokens`, NOT the model-rejected legacy `max_tokens`
        //      (Azure.AI.OpenAI serializes the cap as `max_tokens` unless opted in).
        //   2. `reasoning_effort=minimal` - else a reasoning model spends the whole
        //      token budget on hidden reasoning and returns empty content.
        // See FoundryAiCompletionClient.CompleteAsync.
        var capture = new CapturingHandler();
        var clientOptions = new AzureOpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(capture)),
        };
        var options = new AiOptions
        {
            Endpoint = "https://dummy.openai.azure.com/",
            Deployment = "gpt-5-mini",
            ApiKey = "dummy-key-not-a-real-secret",
            TimeoutSeconds = 30,
        };

        var client = new FoundryAiCompletionClient(
            options, NullLogger<FoundryAiCompletionClient>.Instance, clientOptions);

        var result = await client.CompleteAsync(
            new AiCompletionRequest("family-safe brand voice", "short on-theme words", MaxOutputTokens: 64));

        Assert.True(result.IsAvailable);
        Assert.NotNull(capture.RequestBody);
        Assert.Contains("\"max_completion_tokens\"", capture.RequestBody);
        Assert.DoesNotContain("\"max_tokens\"", capture.RequestBody);
        Assert.Contains("\"reasoning_effort\":\"minimal\"", capture.RequestBody);
    }

    /// <summary>
    /// Records the outgoing request body and returns a canned, valid chat completion -
    /// so the real client's serialization runs end to end without a live provider.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            const string completion =
                "{\"id\":\"x\",\"object\":\"chat.completion\",\"created\":0,\"model\":\"gpt-5-mini\"," +
                "\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"hi\"}}]," +
                "\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(completion, Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task Provider_fault_surfaces_unavailable_not_an_exception()
    {
        // Unreachable endpoint + a dummy key (the AzureKeyCredential path, so no live
        // Azure identity is needed). A short timeout bounds the fail-soft.
        var options = new AiOptions
        {
            Endpoint = "https://quibblestone-nonexistent.invalid/",
            Deployment = "gpt-4o-mini",
            ApiKey = "dummy-key-not-a-real-secret",
            TimeoutSeconds = 2,
        };

        var client = new FoundryAiCompletionClient(options, NullLogger<FoundryAiCompletionClient>.Instance);

        // Must NOT throw - it fails soft to the typed unavailable result (AC-06).
        var result = await client.CompleteAsync(
            new AiCompletionRequest("family-safe brand voice", "short on-theme words", MaxOutputTokens: 32));

        Assert.False(result.IsAvailable);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task Cancelled_caller_yields_unavailable_not_an_exception()
    {
        var options = new AiOptions
        {
            Endpoint = "https://quibblestone-nonexistent.invalid/",
            Deployment = "gpt-4o-mini",
            ApiKey = "dummy-key-not-a-real-secret",
            TimeoutSeconds = 2,
        };

        var client = new FoundryAiCompletionClient(options, NullLogger<FoundryAiCompletionClient>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // An already-cancelled caller (dropped client / shed round) fails soft (AC-05).
        var result = await client.CompleteAsync(
            new AiCompletionRequest("family-safe brand voice", "short on-theme words", MaxOutputTokens: 32),
            cts.Token);

        Assert.False(result.IsAvailable);
    }
}
