// ----------------------------------------------------------------------------
//  BillingLeaseMath - the ONE place the subscription-lease window is computed
//  (billing-entitlements/08). Extracted from StripeWebhookHandler so the webhook
//  handler AND the per-account resync service (StripeReconciliationService) compute a
//  lease end the SAME way rather than each carrying its own copy - the story's "reuse
//  the existing lease math, do not duplicate it."
//
//  TWO ENTRY POINTS, ONE POLICY:
//    - ResolveLeaseEnd: a fresh grant / renewal from an event that already knows the
//      period end (the webhook's checkout / invoice.paid path). A one-time purchase is
//      PERMANENT (null); a subscription lasts to its period end, and a subscription
//      with NO period end falls back to a grace-window lease so it is never written as
//      permanent (the handler's defensive invariant).
//    - ResolveSubscriptionLeaseFromStatus: a resync reconciling a subscription's
//      CURRENT state read straight from Stripe, where the lease depends on the
//      subscription's status (active / past_due / canceled ...). Anchored to the
//      subscription's period end (a STABLE value) rather than wall-clock now wherever
//      possible, so re-running a resync against the same Stripe state is idempotent
//      (billing-entitlements/08 AC-06) - it never ratchets the lease out a little
//      further on each run.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Shared subscription-lease-window math (billing-entitlements/08), used by both
/// <see cref="StripeWebhookHandler"/> and <see cref="StripeReconciliationService"/> so
/// the two never drift. See the file header for the policy.
/// </summary>
public static class BillingLeaseMath
{
    /// <summary>
    /// The lease end for a fresh grant / renewal (the webhook path): a one-time purchase
    /// is PERMANENT (null); a subscription lasts until <paramref name="periodEnd"/>, and a
    /// subscription with a missing period end falls back to a grace-window lease, so a
    /// subscription is NEVER accidentally written as permanent.
    /// </summary>
    /// <param name="source">The grant source (only a subscription is time-bound).</param>
    /// <param name="periodEnd">The subscription period end, or null if the event did not carry one.</param>
    /// <param name="now">Wall-clock now (the grace-window anchor when no period end is known).</param>
    /// <param name="graceDays">The dunning grace window in days (StripeOptions.PastDueGraceDays).</param>
    public static DateTimeOffset? ResolveLeaseEnd(GrantSource source, DateTimeOffset? periodEnd, DateTimeOffset now, int graceDays)
    {
        if (source != GrantSource.Subscription)
        {
            return null; // one-time pack: permanent
        }
        return periodEnd ?? now.AddDays(graceDays);
    }

    /// <summary>
    /// The lease end a resync should write for a subscription in <paramref name="status"/>
    /// with <paramref name="currentPeriodEnd"/> (billing-entitlements/08 AC-04). Anchored to
    /// the period end (stable) rather than <paramref name="now"/> wherever possible, so a
    /// re-run against unchanged Stripe state produces the SAME lease (idempotent, AC-06):
    ///  - active / trialing: paid through the current period.
    ///  - past_due / unpaid: still in dunning - keep it unlocked through a grace window,
    ///    anchored to the period end (never expire the family mid-ride, ADR 0002 Decision D).
    ///  - anything else (canceled / incomplete / incomplete_expired / paused / unknown): let
    ///    the lease end at the last paid period (or now if none), so the NEXT session-creation
    ///    read falls back to free - a read-time lapse, never a live revoke.
    /// </summary>
    /// <param name="status">The Stripe subscription status (case-insensitive); null/blank is treated as terminal.</param>
    /// <param name="currentPeriodEnd">The subscription's current period end, or null if unknown.</param>
    /// <param name="now">Wall-clock now (used only as a fallback when no period end is known).</param>
    /// <param name="graceDays">The dunning grace window in days (StripeOptions.PastDueGraceDays).</param>
    public static DateTimeOffset ResolveSubscriptionLeaseFromStatus(
        string? status, DateTimeOffset? currentPeriodEnd, DateTimeOffset now, int graceDays)
    {
        switch ((status ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "active":
            case "trialing":
                return currentPeriodEnd ?? now.AddDays(graceDays);
            case "past_due":
            case "unpaid":
                // Grace anchored to the period end (stable) so resync stays idempotent.
                return (currentPeriodEnd ?? now).AddDays(graceDays);
            default:
                return currentPeriodEnd ?? now;
        }
    }
}
