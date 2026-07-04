// ----------------------------------------------------------------------------
//  StripeMode - which Stripe environment the app is transacting in
//  (billing-entitlements/06, the live/test mode toggle). Stripe keeps test and
//  live COMPLETELY separate (separate keys, webhooks, prices, customers), so the
//  app holds both credential sets at once (see StripeOptions.Live / .Test) and
//  exactly ONE mode is ACTIVE at a time for new checkouts.
//
//  SAFE DEFAULT (story 06 AC-05): Test. A fresh or unconfigured environment must
//  never be able to take a real charge by default - going Live is always the
//  deliberate, operator-gated action.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Billing;

/// <summary>The Stripe environment in effect (billing-entitlements/06). Default is <see cref="Test"/>.</summary>
public enum StripeMode
{
    /// <summary>Stripe test mode - test cards only, no real money (the safe default, AC-05).</summary>
    Test,

    /// <summary>Stripe live mode - real cards are charged (the deliberate, operator-gated direction).</summary>
    Live,
}

/// <summary>Wire-format helpers so "test"/"live" is parsed/rendered in exactly one place.</summary>
public static class StripeModeText
{
    /// <summary>The lowercase wire value ("test"/"live") for a mode.</summary>
    public static string ToWire(this StripeMode mode) => mode == StripeMode.Live ? "live" : "test";

    /// <summary>
    /// Parses a wire value ("test"/"live", case-insensitive) into a mode. Returns null for
    /// anything else so callers can reject an unknown value rather than default silently to Live.
    /// </summary>
    public static StripeMode? TryParse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "live" => StripeMode.Live,
        "test" => StripeMode.Test,
        _ => null,
    };
}
