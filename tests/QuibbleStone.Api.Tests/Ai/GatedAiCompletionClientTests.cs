// ----------------------------------------------------------------------------
//  GatedAiCompletionClientTests - the gate pipeline seam compiles + runs green
//  with the default (no-op / pass-through) stages (ai-cost-gate/01, the seam
//  folded into story 01).
//
//  Story 01 establishes the ORDERED gate path + the envelope; the three stage
//  services ship as seam defaults (unlimited quota, no-op spend guard, pass-through
//  moderator) so the app runs with zero config. These tests pin that:
//    - With all default stages AND the no-op transport (no AI configured), a gated
//      call returns a clean not-available / fell-back envelope - never an exception
//      (the "seam compiles + runs" guarantee Wave 2 builds against).
//    - With a transport that yields a real completion, the ordered path runs to a
//      moderated, available envelope (the happy path the leaf stages slot into).
//
//  These use small fakes for the transport rather than a live provider call.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Tests.Ai;

public class GatedAiCompletionClientTests
{
    private static GatedAiCompletionClient BuildGate(IAiCompletionClient transport) =>
        new(
            new UnlimitedAiQuota(),
            new NoOpAiSpendGuard(),
            transport,
            new PassthroughAiOutputModerator(),
            NullLogger<GatedAiCompletionClient>.Instance);

    [Fact]
    public async Task Default_stages_with_noop_transport_return_fellback_envelope_cleanly()
    {
        // Mirrors the zero-config app: no-op transport behind the default stages.
        var gate = BuildGate(new NoOpAiCompletionClient(NullLogger<NoOpAiCompletionClient>.Instance));

        var result = await gate.CompleteGatedAsync(
            new AiCompletionRequest("family-safe brand voice", "short on-theme words", MaxOutputTokens: 64),
            instanceId: "instance-abc",
            feature: "jumble",
            familySafe: true);

        Assert.False(result.IsAvailable);
        Assert.True(result.FellBack);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task Available_transport_flows_through_ordered_path_to_moderated_output()
    {
        // A transport that returns a real completion - the ordered path (quota ->
        // ceiling -> transport -> record -> moderate) runs to an available envelope.
        var transport = new StubTransport(
            new AiCompletionResult("moss\nember\nglint", InputTokens: 400, OutputTokens: 30, ModelId: "gpt-4o-mini", IsAvailable: true));

        var gate = BuildGate(transport);

        var result = await gate.CompleteGatedAsync(
            new AiCompletionRequest("family-safe brand voice", "short on-theme words", MaxOutputTokens: 64),
            instanceId: "instance-abc",
            feature: "jumble",
            familySafe: false);

        Assert.True(result.IsAvailable);
        Assert.False(result.FellBack);
        Assert.Equal(new[] { "moss", "ember", "glint" }, result.Output);
    }

    /// <summary>A transport seam that returns a fixed, pre-baked result.</summary>
    private sealed class StubTransport : IAiCompletionClient
    {
        private readonly AiCompletionResult _result;

        public StubTransport(AiCompletionResult result) => _result = result;

        public Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }
}
