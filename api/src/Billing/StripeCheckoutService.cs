// ----------------------------------------------------------------------------
//  StripeCheckoutService - the REAL Stripe Checkout Session creator (billing-
//  entitlements/03, issue #72). ONE method handles BOTH modes (AC-02): a one-time
//  payment (tip / pack) and a recurring subscription (family plan) differ only by the
//  Stripe mode and whether subscription metadata is attached - not by a second
//  integration.
//
//  SECRETS (AC-01): the secret key comes from StripeOptions (Key Vault-backed config),
//  used to build a per-instance StripeClient - NOT the global StripeConfiguration.ApiKey
//  (no shared mutable static). The key is never logged.
//
//  METADATA SEAM: the capability keys + purchaser email ride into the session's
//  Metadata (read back on checkout.session.completed) AND, for a subscription, into
//  SubscriptionData.Metadata (so renewal / past_due / canceled lifecycle events can
//  resolve the same capabilities). See BillingMetadata + StripeEventMapper.
//
//  The session-options building is a pure static (BuildSessionOptions) so the
//  mode/metadata mapping is unit-testable without a Stripe network call (AC-02); the
//  thin CreateAsync call is the only live-Stripe part.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The live <see cref="IStripeCheckoutService"/> (billing-entitlements/03). Creates a
/// Checkout Session for either mode via one parameterized call, stamping the capability
/// + purchaser metadata the webhook reads back. Registered only when a Stripe secret
/// key is configured.
/// </summary>
public sealed class StripeCheckoutService : IStripeCheckoutService
{
    private readonly StripeClient _client;
    private readonly ILogger<StripeCheckoutService> _logger;

    /// <summary>Constructs the service over the configured secret key (Key Vault-backed, AC-01).</summary>
    public StripeCheckoutService(StripeOptions options, ILogger<StripeCheckoutService> logger)
    {
        // Per-instance client from the configured key - no global mutable ApiKey state.
        _client = new StripeClient(options.SecretKey);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var options = BuildSessionOptions(request);
        var service = new SessionService(_client);
        var session = await service.CreateAsync(options, cancellationToken: ct);
        // Never log the session URL/id at Information (it is a redirect a user follows);
        // Debug only, no secret, no PII.
        _logger.LogDebug("Created Stripe Checkout Session in {Mode} mode.", request.Mode);
        return new CheckoutSessionResult(Enabled: true, Url: session.Url, SessionId: session.Id);
    }

    /// <summary>
    /// Builds the <see cref="SessionCreateOptions"/> for a request (AC-02). Pure and
    /// static so the mode + metadata mapping is testable without a Stripe call. A
    /// subscription additionally stamps the metadata onto SubscriptionData so lifecycle
    /// events carry the same capability keys.
    /// </summary>
    public static SessionCreateOptions BuildSessionOptions(CheckoutRequest request)
    {
        var metadata = new Dictionary<string, string>
        {
            [BillingMetadata.CapabilitiesKey] = BillingMetadata.JoinCapabilities(request.CapabilityKeys),
            [BillingMetadata.PurchaserKey] = request.PurchaserEmail ?? string.Empty,
        };

        var options = new SessionCreateOptions
        {
            Mode = request.Mode == CheckoutMode.Subscription ? "subscription" : "payment",
            LineItems = [new SessionLineItemOptions { Price = request.PriceId, Quantity = 1 }],
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            Metadata = metadata,
        };

        if (!string.IsNullOrWhiteSpace(request.PurchaserEmail))
        {
            options.CustomerEmail = request.PurchaserEmail;
        }

        // For a subscription, also stamp the metadata on the subscription itself so the
        // renewal / past_due / canceled webhook events (which reference the subscription,
        // not the original session) can resolve the same capabilities.
        if (request.Mode == CheckoutMode.Subscription)
        {
            options.SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata };
        }

        return options;
    }
}
