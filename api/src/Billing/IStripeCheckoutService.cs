// ----------------------------------------------------------------------------
//  IStripeCheckoutService - the ONE seam that creates a Stripe Checkout Session for
//  either mode (billing-entitlements/03, issue #72, AC-02). Stories 02 (tip jar) and
//  04 (gated purchase) both call this - never a second integration.
//
//  CONFIG-PRESENCE SPLIT: Program.cs registers StripeCheckoutService when a Stripe
//  secret key is configured, else DisabledStripeCheckoutService (returns a clean
//  "not available" result). Callers branch on CheckoutSessionResult.Enabled, never
//  on config directly.
//
//  BillingMetadata carries the correlation keys the checkout STAMPS onto the Stripe
//  session + subscription and the webhook mapper READS back - the seam that lets the
//  webhook resolve which capabilities to grant to which purchaser without a second
//  lookup service.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Creates a Stripe Checkout Session for a one-time payment or a subscription
/// (billing-entitlements/03, AC-02). One method, both modes.
/// </summary>
public interface IStripeCheckoutService
{
    /// <summary>True when Stripe is configured (a real session can be created).</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Creates a Checkout Session for <paramref name="request"/> and returns the
    /// hosted checkout URL to redirect to. When billing is not configured, returns
    /// <see cref="CheckoutSessionResult.Disabled"/> rather than throwing (AC / config
    /// split), so the caller can show a friendly "not available" state.
    /// </summary>
    /// <param name="request">What to buy (mode, price, capability keys, purchaser email, redirect URLs).</param>
    /// <param name="ct">Cancellation for the Stripe call.</param>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default);
}

/// <summary>
/// The Stripe metadata keys the checkout STAMPS and the webhook mapper READS - the
/// correlation seam carrying which capability keys a purchase grants and to which
/// purchaser (billing-entitlements/03). Stamped on BOTH the session (for the initial
/// checkout.session.completed) and the subscription (so renewal / past_due / canceled
/// lifecycle events can resolve the same capabilities). Prefixed to avoid clashing
/// with any Stripe-reserved metadata.
/// </summary>
public static class BillingMetadata
{
    /// <summary>Metadata key holding the comma-separated capability keys a purchase unlocks (empty for a tip).</summary>
    public const string CapabilitiesKey = "qs_capabilities";

    /// <summary>Metadata key holding the purchaser email the resulting grant is keyed to.</summary>
    public const string PurchaserKey = "qs_purchaser";

    /// <summary>
    /// Metadata key holding the ProductCatalog product id a purchase is for
    /// (billing-entitlements/08). Rides into Stripe the same way the capabilities +
    /// purchaser already do, so the webhook / resync can record which product produced
    /// a grant (PlanId) without a second lookup. Empty / absent for a legacy checkout.
    /// </summary>
    public const string ProductKey = "qs_product";

    /// <summary>Joins capability keys into the single metadata string value (comma-separated).</summary>
    public static string JoinCapabilities(IEnumerable<string> capabilityKeys) => string.Join(',', capabilityKeys);

    /// <summary>Splits a capabilities metadata value back into keys, dropping blanks. Null/empty => no keys.</summary>
    public static IReadOnlyList<string> SplitCapabilities(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
