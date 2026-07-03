// ----------------------------------------------------------------------------
//  AiCostEstimatorTests - proves the pure per-call cost estimate is the exact
//  AC-01 formula for the deployed model's rates (ai-cost-gate/04, #123). The
//  estimator is the arithmetic heart of the spend breaker, so pinning "tokens x
//  rates yields this dollar figure" here is the cheapest, most direct coverage.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Tests.Ai;

public class AiCostEstimatorTests
{
    // The ADR 0001 gpt-4o-mini rates: 0.15 input / 0.60 output per 1,000,000 tokens.
    private const decimal InputRate = 0.15m;
    private const decimal OutputRate = 0.60m;

    [Fact]
    public void One_million_input_tokens_costs_the_input_rate()
    {
        var est = AiCostEstimator.EstimateUsd(1_000_000, 0, InputRate, OutputRate);
        Assert.Equal(0.15m, est);
    }

    [Fact]
    public void One_million_output_tokens_costs_the_output_rate()
    {
        var est = AiCostEstimator.EstimateUsd(0, 1_000_000, InputRate, OutputRate);
        Assert.Equal(0.60m, est);
    }

    [Fact]
    public void Mixed_tokens_use_both_rates()
    {
        // (400 * 0.15 + 30 * 0.60) / 1e6 = (60 + 18) / 1e6 = 0.000078
        var est = AiCostEstimator.EstimateUsd(400, 30, InputRate, OutputRate);
        Assert.Equal(0.000078m, est);
    }

    [Fact]
    public void Zero_tokens_cost_nothing()
    {
        Assert.Equal(0m, AiCostEstimator.EstimateUsd(0, 0, InputRate, OutputRate));
    }

    [Fact]
    public void Negative_token_counts_are_clamped_and_never_credit_the_total()
    {
        // A malformed/negative usage figure must never produce a negative cost that
        // would credit the running monthly total back under the ceiling.
        Assert.Equal(0m, AiCostEstimator.EstimateUsd(-500, -500, InputRate, OutputRate));
    }

    [Fact]
    public void The_result_overload_reads_the_rates_from_options()
    {
        var options = new AiOptions { InputCostPerMillion = InputRate, OutputCostPerMillion = OutputRate };
        var result = new AiCompletionResult("moss\nember", InputTokens: 1000, OutputTokens: 200, ModelId: "gpt-4o-mini", IsAvailable: true);

        // (1000 * 0.15 + 200 * 0.60) / 1e6 = (150 + 120) / 1e6 = 0.00027
        Assert.Equal(0.00027m, AiCostEstimator.EstimateUsd(result, options));
    }
}
