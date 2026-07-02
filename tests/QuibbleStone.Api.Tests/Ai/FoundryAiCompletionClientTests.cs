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

using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Tests.Ai;

public class FoundryAiCompletionClientTests
{
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
