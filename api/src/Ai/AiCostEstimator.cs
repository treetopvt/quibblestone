// ----------------------------------------------------------------------------
//  AiCostEstimator - the PURE, deterministic per-call cost estimate for the AI
//  spend circuit-breaker (ai-cost-gate story 04, issue #123, AC-01).
//
//  WHAT THIS IS: the one function that turns story 01's returned token usage into
//  an estimated dollar figure, using the per-model rate constants that live beside
//  the model id in AiOptions (ADR 0001: gpt-4o-mini = 0.15 input / 0.60 output per
//  1,000,000 tokens). The formula is exactly the story's AC-01:
//
//      estUsd = (inputTokens * inputRate + outputTokens * outputRate) / 1e6
//
//  WHY A PURE HELPER (no I/O, no state, no clock): the estimate is the arithmetic
//  heart of the breaker, and keeping it a stateless static means it is trivially
//  unit-testable (a token count in, a known dollar figure out) and a model swap is
//  a ONE-PLACE rate change in AiOptions - never a literal scattered through the
//  gate. The breaker (AiSpendBreaker) composes this with the persisted monthly
//  total; the Table I/O lives THERE, not here.
//
//  WHY decimal, not double: money. We accrue a running monthly total against a $20
//  ceiling, so we estimate in decimal to avoid binary-floating-point drift creeping
//  the total off the ceiling over thousands of calls. (Table Storage has no decimal
//  column, so the persisted total round-trips through double in the store - but the
//  arithmetic here stays exact.)
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Ai;

/// <summary>
/// Pure, stateless per-call AI cost estimator (story 04 AC-01). Turns a call's
/// input/output token usage (story 01, <see cref="AiCompletionResult"/>) into an
/// estimated USD figure using the per-model rates in <see cref="AiOptions"/>
/// (<c>(in*inRate + out*outRate) / 1e6</c>). No I/O and no state, so it is directly
/// unit-testable and a model swap is a one-place rate change in config.
/// </summary>
public static class AiCostEstimator
{
    /// <summary>Tokens per rate unit: the per-model rates are priced per 1,000,000 tokens (ADR 0001).</summary>
    public const decimal TokensPerRateUnit = 1_000_000m;

    /// <summary>
    /// Estimates the USD cost of one AI call from its token counts and the per-model
    /// rates (AC-01). Token counts are clamped to non-negative so a malformed/negative
    /// usage figure can never produce a negative cost that would credit the running
    /// total back under the ceiling.
    /// </summary>
    /// <param name="inputTokens">Prompt (input) tokens the provider reported.</param>
    /// <param name="outputTokens">Completion (output) tokens the provider reported.</param>
    /// <param name="inputCostPerMillion">Input $/1M rate (AiOptions.InputCostPerMillion).</param>
    /// <param name="outputCostPerMillion">Output $/1M rate (AiOptions.OutputCostPerMillion).</param>
    /// <returns>The estimated cost in USD (always &gt;= 0).</returns>
    public static decimal EstimateUsd(
        int inputTokens,
        int outputTokens,
        decimal inputCostPerMillion,
        decimal outputCostPerMillion)
    {
        var input = Math.Max(0, inputTokens);
        var output = Math.Max(0, outputTokens);
        return (input * inputCostPerMillion + output * outputCostPerMillion) / TokensPerRateUnit;
    }

    /// <summary>
    /// Convenience overload: estimates the cost of a completed call from its
    /// <see cref="AiCompletionResult"/> token usage + the rates on
    /// <see cref="AiOptions"/> (the exact shape the breaker records).
    /// </summary>
    /// <param name="result">The completed call's result (carries the token usage).</param>
    /// <param name="options">The bound AI options (carries the per-model rates).</param>
    public static decimal EstimateUsd(AiCompletionResult result, AiOptions options) =>
        EstimateUsd(result.InputTokens, result.OutputTokens, options.InputCostPerMillion, options.OutputCostPerMillion);
}
