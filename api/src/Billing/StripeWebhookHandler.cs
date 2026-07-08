// ----------------------------------------------------------------------------
//  StripeWebhookHandler - the ISOLATED, Stripe-SDK-free domain core of the billing
//  webhook (billing-entitlements/03, issue #72). It acts on a normalized
//  BillingEvent (produced once at the edge by StripeEventMapper from a verified
//  Stripe.Event), so all of its logic is unit-testable without live Stripe or signed
//  payloads, and lifting it into an Azure Function later is a move of this class, not
//  a rewrite (AC-04).
//
//  WHAT IT DOES:
//    - Idempotency (AC-05): skips an event id already processed (Stripe delivers
//      at-least-once); records the id after a successful apply.
//    - Grants keyed to the account (AC-06): ensures the purchaser Account exists
//      (checkout naturally creates it - story 04) and writes grants keyed off the
//      stable account.Id (accounts-identity/05), the SAME id billing-entitlements/01's
//      session-creation gate reads (an email change never orphans the grants).
//    - Grants NOTHING when there is nothing to grant: a tip (empty capability keys,
//      story 02 AC-02) or an anonymous purchase is acknowledged but writes no grant.
//    - Subscription lifecycle (AC-08, ADR 0002 Decisions C/D):
//        * CheckoutCompleted / SubscriptionRenewed -> write/extend the lease to the
//          period end (a one-time pack is permanent: validThrough = null).
//        * SubscriptionPastDue -> extend the lease by the grace window, never expire
//          it mid-ride (a failed card must not lock a family in the car).
//        * SubscriptionCanceled -> set validThrough to now so the lease reads expired
//          at the NEXT session-creation - a read-time consequence, never a live
//          mid-session revoke (billing-entitlements/01 AC-03).
//
//  DEFENSIVE INVARIANT: a subscription grant is NEVER written with a null (permanent)
//  lease - if a subscription event somehow lacks a period end, it falls back to a
//  grace-window lease, so a subscription can never accidentally become permanent.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Applies a verified, normalized <see cref="BillingEvent"/> to the entitlement grant
/// store (billing-entitlements/03): idempotent (AC-05), keyed to the purchaser account
/// (AC-06), grants nothing for a tip (story 02 AC-02), and runs the subscription
/// lifecycle lease math (AC-08). No Stripe SDK dependency - unit-testable in isolation.
/// </summary>
public sealed class StripeWebhookHandler
{
    private readonly IEntitlementGrantStore _grants;
    private readonly IAccountStore _accounts;
    private readonly IProcessedEventStore _processed;
    private readonly StripeOptions _options;

    /// <summary>Constructs the handler over the grant store, account store, idempotency ledger, and options (grace window).</summary>
    public StripeWebhookHandler(
        IEntitlementGrantStore grants,
        IAccountStore accounts,
        IProcessedEventStore processed,
        StripeOptions options)
    {
        _grants = grants;
        _accounts = accounts;
        _processed = processed;
        _options = options;
    }

    /// <summary>
    /// Applies <paramref name="billingEvent"/> to the grant store and returns the outcome.
    /// Idempotent per event id (AC-05). Never throws on a benign / unrecognized event -
    /// it is acknowledged as <see cref="WebhookOutcome.Ignored"/>.
    /// </summary>
    public async Task<WebhookOutcome> HandleAsync(BillingEvent billingEvent, CancellationToken ct = default)
    {
        if (billingEvent.Kind == BillingEventKind.Ignored)
        {
            // An event type we do not act on - acknowledge without recording it.
            return WebhookOutcome.Ignored;
        }

        // Idempotency (AC-05): a redelivered event id is a no-op.
        if (await _processed.HasProcessedAsync(billingEvent.EventId, ct))
        {
            return WebhookOutcome.AlreadyProcessed;
        }

        var email = billingEvent.PurchaserEmail?.Trim();
        if (string.IsNullOrEmpty(email) || billingEvent.CapabilityKeys.Count == 0)
        {
            // Nothing to grant: an anonymous purchase, or an entitlement-neutral one
            // (a tip - story 02 AC-02, which passes no capability keys). Record the id
            // so a redelivery stays a no-op, then acknowledge.
            await _processed.MarkProcessedAsync(billingEvent.EventId, ct);
            return WebhookOutcome.Ignored;
        }

        // Ensure the purchaser account exists (checkout naturally creates it - story 04)
        // and key grants off its stable id (account.Id, AC-06 / billing-01 contract).
        var account = await _accounts.CreateOrGetAsync(email, ct);
        var now = DateTimeOffset.UtcNow;

        switch (billingEvent.Kind)
        {
            case BillingEventKind.CheckoutCompleted:
            case BillingEventKind.SubscriptionRenewed:
                var validThrough = ResolveLeaseEnd(billingEvent.Source, billingEvent.PeriodEnd, now);
                foreach (var capability in billingEvent.CapabilityKeys)
                {
                    await _grants.PutGrantAsync(account.Id, new EntitlementGrant(capability, validThrough, billingEvent.Source), ct);
                }
                break;

            case BillingEventKind.SubscriptionPastDue:
                // Extend the lease by the grace window rather than expire it (AC-08 /
                // Decision D). Never SHORTEN an already-longer lease.
                //
                // Bounded-ratchet tradeoff (review WARN-001), consciously accepted for a
                // toy (CLAUDE.md section 10): the grace anchor is wall-clock `now`, so if
                // this exact event were reprocessed (a crash between the grant write below
                // and MarkProcessedAsync, or a concurrent redelivery) the lease would push
                // out by the redelivery gap - an over-grant of hours, never a shorten,
                // never a corruption, never a double-charge. Anchoring to `now` (not to the
                // grant's stored end) is deliberate: it guarantees a failed card always buys
                // a full grace window from the moment it failed, which matters more here
                // than making reprocessing a perfect fixed point.
                var graceEnd = now.AddDays(_options.PastDueGraceDays);
                var existing = await _grants.GetGrantsAsync(account.Id, ct);
                var byCapability = existing.ToDictionary(g => g.CapabilityKey, StringComparer.Ordinal);
                foreach (var capability in billingEvent.CapabilityKeys)
                {
                    var currentEnd = byCapability.TryGetValue(capability, out var cur) ? cur.ValidThrough : null;
                    var extended = currentEnd is { } end && end > graceEnd ? end : graceEnd;
                    await _grants.PutGrantAsync(account.Id, new EntitlementGrant(capability, extended, GrantSource.Subscription), ct);
                }
                break;

            case BillingEventKind.SubscriptionCanceled:
                // Let the lease pass (validThrough = now) so the NEXT session-creation
                // read falls back to free - never a live mid-session revoke (AC-08 /
                // billing-01 AC-03).
                foreach (var capability in billingEvent.CapabilityKeys)
                {
                    await _grants.PutGrantAsync(account.Id, new EntitlementGrant(capability, now, GrantSource.Subscription), ct);
                }
                break;
        }

        await _processed.MarkProcessedAsync(billingEvent.EventId, ct);
        return WebhookOutcome.Processed;
    }

    /// <summary>
    /// The lease end for a fresh grant / renewal: a one-time purchase is PERMANENT
    /// (null), a subscription lasts until its period end - and a subscription with a
    /// missing period end falls back to a grace-window lease, so a subscription is
    /// NEVER accidentally written as permanent.
    /// </summary>
    private DateTimeOffset? ResolveLeaseEnd(GrantSource source, DateTimeOffset? periodEnd, DateTimeOffset now)
    {
        if (source != GrantSource.Subscription)
        {
            return null; // one-time pack: permanent
        }
        return periodEnd ?? now.AddDays(_options.PastDueGraceDays);
    }
}
