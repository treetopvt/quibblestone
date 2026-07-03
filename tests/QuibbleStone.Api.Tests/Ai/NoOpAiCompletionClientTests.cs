// ----------------------------------------------------------------------------
//  NoOpAiCompletionClientTests - the zero-config AI transport resolves cleanly
//  (ai-cost-gate/01 AC-04).
//
//  With no `Ai:*` config the app registers the no-op client so it builds + runs
//  with zero AI setup and every consumer falls back deterministically. This pins
//  that the no-op returns a typed unavailable result WITHOUT throwing - the
//  contract the config-presence branch in Program.cs depends on.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Tests.Ai;

public class NoOpAiCompletionClientTests
{
    [Fact]
    public async Task NoOp_returns_unavailable_without_throwing()
    {
        var client = new NoOpAiCompletionClient(NullLogger<NoOpAiCompletionClient>.Instance);

        var result = await client.CompleteAsync(
            new AiCompletionRequest("family-safe brand voice", "give me short words", MaxOutputTokens: 64));

        Assert.False(result.IsAvailable);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, result.InputTokens);
        Assert.Equal(0, result.OutputTokens);
    }
}
