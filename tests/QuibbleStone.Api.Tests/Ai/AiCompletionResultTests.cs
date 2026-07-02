// ----------------------------------------------------------------------------
//  AiCompletionResultTests - locks the AI proxy's own return type (ai-cost-gate/01).
//
//  The whole point of surfacing token usage + model id on OUR result type (not the
//  raw SDK response) is that the spend circuit-breaker (story 04) can estimate
//  $/call deterministically (AC-02), and that a provider fault has a TYPED "AI
//  unavailable" state (AC-06) rather than an exception into gameplay. These tests
//  pin both: a real result carries the counts + model id + IsAvailable = true, and
//  the shared Unavailable factory is not-available with zeroed usage.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Tests.Ai;

public class AiCompletionResultTests
{
    [Fact]
    public void Result_surfaces_token_usage_model_id_and_availability()
    {
        // AC-02: the token counts + model id are on our own type, ready for story 04.
        var result = new AiCompletionResult(
            Text: "moss, ember, glint",
            InputTokens: 412,
            OutputTokens: 57,
            ModelId: "gpt-4o-mini",
            IsAvailable: true);

        Assert.Equal("moss, ember, glint", result.Text);
        Assert.Equal(412, result.InputTokens);
        Assert.Equal(57, result.OutputTokens);
        Assert.Equal("gpt-4o-mini", result.ModelId);
        Assert.True(result.IsAvailable);
    }

    [Fact]
    public void Unavailable_factory_is_not_available_with_zeroed_usage()
    {
        // AC-06: the typed unavailable state - no text, no usage, not available.
        var unavailable = AiCompletionResult.Unavailable;

        Assert.False(unavailable.IsAvailable);
        Assert.Equal(string.Empty, unavailable.Text);
        Assert.Equal(0, unavailable.InputTokens);
        Assert.Equal(0, unavailable.OutputTokens);
        Assert.Equal(string.Empty, unavailable.ModelId);
    }
}
