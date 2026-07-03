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
    // The deployed gpt-5-mini rates: 0.25 input / 2.00 output per 1,000,000 tokens
    // (ADR 0001 picked gpt-4o-mini at 0.15/0.60; gpt-5-mini was deployed after that
    // pick was superseded by availability - see the ADR Update note + PR #131).
    private const decimal InputRate = 0.25m;
    private const decimal OutputRate = 2.00m;

    [Fact]
    public void One_million_input_tokens_costs_the_input_rate()
    {
        var est = AiCostEstimator.EstimateUsd(1_000_000, 0, InputRate, OutputRate);
        Assert.Equal(0.25m, est);
    }

    [Fact]
    public void One_million_output_tokens_costs_the_output_rate()
    {
        var est = AiCostEstimator.EstimateUsd(0, 1_000_000, InputRate, OutputRate);
        Assert.Equal(2.00m, est);
    }

    [Fact]
    public void Mixed_tokens_use_both_rates()
    {
        // (400 * 0.25 + 30 * 2.00) / 1e6 = (100 + 60) / 1e6 = 0.00016
        var est = AiCostEstimator.EstimateUsd(400, 30, InputRate, OutputRate);
        Assert.Equal(0.00016m, est);
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
        var result = new AiCompletionResult("moss\nember", InputTokens: 1000, OutputTokens: 200, ModelId: "gpt-5-mini", IsAvailable: true);

        // (1000 * 0.25 + 200 * 2.00) / 1e6 = (250 + 400) / 1e6 = 0.00065
        Assert.Equal(0.00065m, AiCostEstimator.EstimateUsd(result, options));
    }
}
