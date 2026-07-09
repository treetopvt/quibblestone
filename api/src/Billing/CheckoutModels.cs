// ----------------------------------------------------------------------------
//  Checkout request/result models for the Stripe Checkout seam
//  (billing-entitlements/03, issue #72). ONE service, ONE method, both modes
//  (AC-02): a one-time payment (tip jar, add-on pack) and a recurring subscription
//  (family plan) differ only by the CheckoutMode and whether a period-bound lease
//  results - not by a second integration.
//
//  The CAPABILITY KEYS a purchase grants ride ON the request (stamped into the
//  Stripe session + subscription metadata by StripeCheckoutService, read back by
//  the webhook). Story 04 owns the price/product -> capability-keys MAP that fills
//  this in; the tip jar (story 02) passes an EMPTY list, so it grants nothing.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Billing;

/// <summary>Stripe Checkout Session mode - a one-time payment or a recurring subscription (AC-02).</summary>
public enum CheckoutMode
{
    /// <summary>A one-time payment (a tip or an add-on pack) - Stripe mode "payment".</summary>
    Payment,

    /// <summary>A recurring subscription (the family plan) - Stripe mode "subscription".</summary>
    Subscription,
}

/// <summary>
/// What to buy in a Checkout Session (billing-entitlements/03, AC-02). The same
/// service handles both modes off this one request. <see cref="CapabilityKeys"/> is
/// what the webhook will grant on success - empty for a tip (grants nothing).
/// </summary>
/// <param name="Mode">One-time payment or subscription.</param>
/// <param name="PriceId">The Stripe Price id being purchased (story 04's map supplies this).</param>
/// <param name="SuccessUrl">Where Stripe redirects on success.</param>
/// <param name="CancelUrl">Where Stripe redirects on cancel/abandon (AC-07: no grant on this path).</param>
/// <param name="CapabilityKeys">The catalog capability keys this purchase unlocks; EMPTY grants nothing (a tip).</param>
/// <param name="PurchaserEmail">Optional purchaser email to prefill checkout and key the resulting grant to (AC-06).</param>
/// <param name="ProductId">The ProductCatalog product id this checkout is for (billing-entitlements/08). Stamped as <c>qs_product</c> onto the session + subscription metadata so the resulting grant records its PlanId. Null/empty for a checkout with no product id.</param>
public sealed record CheckoutRequest(
    CheckoutMode Mode,
    string PriceId,
    string SuccessUrl,
    string CancelUrl,
    IReadOnlyList<string> CapabilityKeys,
    string? PurchaserEmail = null,
    string? ProductId = null)
{
    /// <summary>The grant source implied by the mode: a subscription lease vs a permanent one-time grant.</summary>
    public GrantSource Source => Mode == CheckoutMode.Subscription ? GrantSource.Subscription : GrantSource.OneTime;
}

/// <summary>
/// The result of creating a Checkout Session. On success <see cref="Url"/> is the
/// Stripe-hosted checkout URL the client redirects to. When billing is not
/// configured (no Stripe key) <see cref="Enabled"/> is false and the caller shows a
/// friendly "not available" state rather than erroring (the config-presence split).
/// </summary>
/// <param name="Enabled">False when Stripe is not configured (no secret key) - billing is off.</param>
/// <param name="Url">The Stripe-hosted Checkout URL to redirect to (present on success).</param>
/// <param name="SessionId">The Checkout Session id (present on success), for correlation.</param>
public sealed record CheckoutSessionResult(bool Enabled, string? Url, string? SessionId)
{
    /// <summary>The result returned when billing is not configured - a clean, non-error "off" state.</summary>
    public static CheckoutSessionResult Disabled { get; } = new(Enabled: false, Url: null, SessionId: null);
}
