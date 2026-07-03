// ----------------------------------------------------------------------------
//  DisabledStripeCheckoutService - the no-op checkout service registered when Stripe
//  is NOT configured (billing-entitlements/03, local dev / CI / a fresh clone with no
//  Stripe secret key). It never calls Stripe and always returns the "not available"
//  result, so the app runs with ZERO billing setup and the tip jar / paywall show a
//  clean "billing not available yet" state instead of erroring. The moment a Stripe
//  secret key is configured, Program.cs registers the real StripeCheckoutService.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The disabled <see cref="IStripeCheckoutService"/> (billing-entitlements/03): always
/// reports not-enabled and returns <see cref="CheckoutSessionResult.Disabled"/>.
/// Registered when no Stripe secret key is configured.
/// </summary>
public sealed class DisabledStripeCheckoutService : IStripeCheckoutService
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
        => Task.FromResult(CheckoutSessionResult.Disabled);
}
