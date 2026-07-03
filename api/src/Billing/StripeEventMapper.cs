// ----------------------------------------------------------------------------
//  StripeEventMapper - the ONE place Stripe SDK types are translated into the
//  normalized, SDK-free BillingEvent the domain handler acts on (billing-
//  entitlements/03, issue #72). This is the thin Stripe-coupled edge (with the
//  controller): keep ALL Stripe.* type knowledge here so StripeWebhookHandler stays
//  pure and testable, and lifting the pair into an Azure Function later is a move,
//  not a rewrite (AC-04).
//
//  MAPPING (AC-03/AC-08):
//    - checkout.session.completed        -> CheckoutCompleted (mode -> source)
//    - invoice.paid                      -> SubscriptionRenewed (extend to period end)
//    - customer.subscription.updated     -> SubscriptionPastDue when status == past_due
//    - customer.subscription.deleted     -> SubscriptionCanceled
//    - anything else                     -> Ignored
//  Capability keys + purchaser email come from the metadata the checkout STAMPED
//  (BillingMetadata) onto the session and (for subscriptions) the subscription.
//
//  NOTE: the exact Stripe.* property surface is verified at build time against the
//  pinned Stripe.net version; the correctness of the live mapping is manual /
//  integration verified (the story's test table), while the DOMAIN behavior it feeds
//  is unit-tested via BillingEvent in StripeWebhookHandlerTests.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;
using Stripe;
using Stripe.Checkout;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Translates a verified <see cref="Stripe.Event"/> into a normalized
/// <see cref="BillingEvent"/> (billing-entitlements/03). The single Stripe-typed edge
/// feeding the SDK-free <see cref="StripeWebhookHandler"/>.
/// </summary>
public static class StripeEventMapper
{
    /// <summary>
    /// Maps a verified Stripe event to a <see cref="BillingEvent"/>. An event type or
    /// shape we do not act on maps to <see cref="BillingEventKind.Ignored"/> (a no-op).
    /// </summary>
    public static BillingEvent ToBillingEvent(Event stripeEvent)
    {
        var id = stripeEvent.Id;

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed" when stripeEvent.Data.Object is Session session:
            {
                var source = string.Equals(session.Mode, "subscription", StringComparison.Ordinal)
                    ? GrantSource.Subscription
                    : GrantSource.OneTime;
                return new BillingEvent(
                    id,
                    BillingEventKind.CheckoutCompleted,
                    source,
                    PurchaserOf(session.Metadata, session.CustomerEmail),
                    CapabilitiesOf(session.Metadata),
                    // The initial subscription period end is not on the session; the
                    // immediately-following invoice.paid sets the real lease end. The
                    // handler falls back to a grace-window lease in the meantime, so a
                    // subscription is never granted as permanent.
                    PeriodEnd: null);
            }

            case "invoice.paid" when stripeEvent.Data.Object is Invoice invoice:
            {
                return new BillingEvent(
                    id,
                    BillingEventKind.SubscriptionRenewed,
                    GrantSource.Subscription,
                    PurchaserOf(invoice.Metadata, invoice.CustomerEmail),
                    CapabilitiesOf(invoice.Metadata),
                    PeriodEnd: PeriodEndOf(invoice));
            }

            case "customer.subscription.updated" when stripeEvent.Data.Object is Subscription updated
                && string.Equals(updated.Status, "past_due", StringComparison.Ordinal):
            {
                return new BillingEvent(
                    id,
                    BillingEventKind.SubscriptionPastDue,
                    GrantSource.Subscription,
                    PurchaserOf(updated.Metadata, customerEmail: null),
                    CapabilitiesOf(updated.Metadata),
                    PeriodEnd: null);
            }

            case "customer.subscription.deleted" when stripeEvent.Data.Object is Subscription deleted:
            {
                return new BillingEvent(
                    id,
                    BillingEventKind.SubscriptionCanceled,
                    GrantSource.Subscription,
                    PurchaserOf(deleted.Metadata, customerEmail: null),
                    CapabilitiesOf(deleted.Metadata),
                    PeriodEnd: null);
            }

            default:
                return new BillingEvent(id, BillingEventKind.Ignored, GrantSource.OneTime, null, [], null);
        }
    }

    // The purchaser email: prefer the metadata the checkout stamped, fall back to the
    // Stripe customer email on the object.
    private static string? PurchaserOf(IDictionary<string, string>? metadata, string? customerEmail)
    {
        if (metadata is not null && metadata.TryGetValue(BillingMetadata.PurchaserKey, out var stamped) && !string.IsNullOrWhiteSpace(stamped))
        {
            return stamped;
        }
        return customerEmail;
    }

    private static IReadOnlyList<string> CapabilitiesOf(IDictionary<string, string>? metadata)
    {
        if (metadata is not null && metadata.TryGetValue(BillingMetadata.CapabilitiesKey, out var joined))
        {
            return BillingMetadata.SplitCapabilities(joined);
        }
        return [];
    }

    // The renewal period end from the invoice's line items (the latest line's period
    // end). Null if the invoice carries no line period (a defensive fall-through the
    // handler treats as a grace-window lease rather than permanent).
    private static DateTimeOffset? PeriodEndOf(Invoice invoice)
    {
        var end = invoice.Lines?.Data?
            .Select(line => line.Period?.End)
            .Where(e => e is not null)
            .DefaultIfEmpty(null)
            .Max();
        return end is { } value ? new DateTimeOffset(value, TimeSpan.Zero) : null;
    }
}
