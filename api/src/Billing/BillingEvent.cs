// ----------------------------------------------------------------------------
//  BillingEvent - the NORMALIZED, Stripe-SDK-free billing event the webhook handler
//  acts on (billing-entitlements/03, issue #72).
//
//  WHY THIS EXISTS (AC-04, the Functions carve-out discipline): the value of the
//  webhook seam is that its DOMAIN logic (idempotency, which capability to grant,
//  the subscription-lifecycle validThrough math) has NO dependency on Stripe's SDK
//  types. StripeEventMapper translates a verified Stripe.Event into this plain
//  record ONCE, at the edge; StripeWebhookHandler then works purely on this. That
//  makes the whole lifecycle unit-testable WITHOUT live Stripe or signed payloads,
//  and makes lifting the handler into an Azure Function later a move of two
//  self-contained classes, not a rewrite.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The kind of billing event, normalized from Stripe's event types to the four the
/// subscription lifecycle cares about (AC-08) plus initial checkout. Anything else
/// maps to <see cref="Ignored"/> and is a no-op.
/// </summary>
public enum BillingEventKind
{
    /// <summary>An unrecognized event - acknowledged (200) but not processed.</summary>
    Ignored,

    /// <summary>Initial checkout completed (checkout.session.completed) - the first grant is written.</summary>
    CheckoutCompleted,

    /// <summary>A subscription renewal paid (invoice.paid) - extend the lease to the new period end.</summary>
    SubscriptionRenewed,

    /// <summary>A subscription went past_due - extend the lease by the grace window (never expire mid-ride).</summary>
    SubscriptionPastDue,

    /// <summary>A subscription was canceled (or grace lapsed) - let the lease pass so the next session falls back to free.</summary>
    SubscriptionCanceled,
}

/// <summary>
/// A verified, normalized billing event (billing-entitlements/03). Carries only what
/// the domain handler needs: the Stripe event id (for idempotency, AC-05), the kind,
/// the grant source, the purchaser email the grant is keyed to (AC-06), the capability
/// keys the purchase unlocks (empty for a tip, which grants nothing - story 02 AC-02),
/// the subscription period end where relevant (AC-08), and (billing-entitlements/08)
/// the plan id + Stripe subscription id the grant records for support / resync. No
/// Stripe SDK type leaks in - the mapper reads all of this off the verified event once.
/// </summary>
/// <param name="EventId">Stripe's unique event id - the idempotency key (AC-05).</param>
/// <param name="Kind">The normalized event kind.</param>
/// <param name="Source">The grant source (one-time vs subscription) for capabilities this event grants.</param>
/// <param name="PurchaserEmail">The purchaser's email the grant is keyed to; null/empty => nothing to grant (e.g. an anonymous tip).</param>
/// <param name="CapabilityKeys">The capability keys this purchase unlocks; empty => grant nothing (a tip, or an unrecognized product).</param>
/// <param name="PeriodEnd">The subscription period end (renewal/checkout of a subscription); null for a permanent one-time pack.</param>
/// <param name="PlanId">The ProductCatalog product id stamped at checkout (billing-entitlements/08's <c>qs_product</c> metadata); null when the event carries no product id (a legacy subscription, an unrecognized product). Written onto every grant so a support lookup / resync can tell which purchase produced the row.</param>
/// <param name="StripeSubscriptionId">The Stripe subscription id for a subscription-sourced event, carried through from the event shape (billing-entitlements/08); null for a one-time pack or a tip.</param>
public sealed record BillingEvent(
    string EventId,
    BillingEventKind Kind,
    GrantSource Source,
    string? PurchaserEmail,
    IReadOnlyList<string> CapabilityKeys,
    DateTimeOffset? PeriodEnd,
    string? PlanId = null,
    string? StripeSubscriptionId = null);

/// <summary>The outcome of handling a <see cref="BillingEvent"/>, for the controller's response + logging (no PII).</summary>
public enum WebhookOutcome
{
    /// <summary>Handled and applied (a grant was written / updated).</summary>
    Processed,

    /// <summary>The event id was already processed - a no-op (Stripe at-least-once redelivery, AC-05).</summary>
    AlreadyProcessed,

    /// <summary>Acknowledged but nothing to do (unrecognized kind, or a purchase that grants nothing - a tip).</summary>
    Ignored,
}
