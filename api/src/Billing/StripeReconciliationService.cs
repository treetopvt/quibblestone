// ----------------------------------------------------------------------------
//  StripeReconciliationService - the pure, unit-testable core of the per-account
//  Stripe resync (billing-entitlements/08, issue #215). It orchestrates
//  IStripeSubscriptionSource (the Stripe-coupled edge) + IActiveStripeContext (the
//  active mode) + the grant store, and enforces the TWO binding security rules
//  entirely in tested code (see IStripeReconciliationService's header):
//    - metadata-matched identity (AC-04): a candidate subscription is reconciled ONLY
//      when its qs_purchaser metadata equals the account's canonical email; a candidate
//      that matched only by the Stripe customer's bare (unverified) Email is skipped.
//    - the mode-aware guard (AC-08 + AC-05): a capability's existing grant is
//      overwritten ONLY when it is a SUBSCRIPTION grant whose stored Mode equals the
//      active mode. A one-time pack, an operator comp, or a cross-mode subscription
//      grant is left byte-for-byte untouched (read for the comparison, never written).
//
//  IDEMPOTENT (AC-06): the write path is the SAME upsert-by-capability-key PutGrantAsync
//  the webhook already uses, so re-running against unchanged Stripe state produces the
//  same rows. The pre-existing grant snapshot is read ONCE before the write loop, so the
//  guard compares against the state at resync start, not against this run's own writes.
//
//  LEASE MATH IS SHARED, NOT DUPLICATED (story Technical Notes): the lease a subscription
//  implies is computed by BillingLeaseMath.ResolveSubscriptionLeaseFromStatus - the same
//  helper module the webhook handler uses - so the two never drift.
//
//  ONLY SUBSCRIPTIONS ARE RECONCILED (AC-05): Stripe has no ongoing "active" state to
//  reconcile a one-time pack or an operator comp against, so resync only ever writes
//  Source = Subscription grants. A one-time / operator grant is untouched both because the
//  reconciled capability keys come from a subscription and because the mode guard skips a
//  non-subscription existing row.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The live <see cref="IStripeReconciliationService"/> (billing-entitlements/08). Pure
/// orchestration over the Stripe candidate source, the active-mode context, and the grant
/// store - all Stripe.* coupling stays behind <see cref="IStripeSubscriptionSource"/>. A
/// singleton.
/// </summary>
public sealed class StripeReconciliationService : IStripeReconciliationService
{
    private readonly IStripeSubscriptionSource _source;
    private readonly IActiveStripeContext _context;
    private readonly IEntitlementGrantStore _grants;
    private readonly StripeOptions _options;
    private readonly ILogger<StripeReconciliationService> _logger;

    /// <summary>Constructs the service over the candidate source, active-mode context, grant store, and options (grace window).</summary>
    public StripeReconciliationService(
        IStripeSubscriptionSource source,
        IActiveStripeContext context,
        IEntitlementGrantStore grants,
        StripeOptions options,
        ILogger<StripeReconciliationService> logger)
    {
        _source = source;
        _context = context;
        _grants = grants;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResyncResult> ResyncAccountAsync(Account account, CancellationToken ct = default)
    {
        if (!_source.IsEnabled)
        {
            // Billing is not configured (no active-mode secret) - nothing to read or write.
            // Free play is unaffected; the operator just sees a clean "not configured" result.
            return new ResyncResult(BillingConfigured: false, ActiveMode: null, 0, 0, 0, 0);
        }

        var activeMode = (await _context.GetStateAsync(ct)).Mode;
        var candidates = await _source.ListCandidatesAsync(account.Email, ct);

        // Read the account's pre-existing grants ONCE, before any write, so the mode guard
        // compares against the state at resync start (AC-08) rather than this run's own writes.
        var existing = await _grants.GetGrantsAsync(account.Id, ct);
        var byCapability = existing.ToDictionary(g => g.CapabilityKey, StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        int reconciled = 0, skippedUnmatched = 0, skippedModeGuard = 0, skippedNoMetadata = 0;

        foreach (var candidate in candidates)
        {
            // Rule 1 (AC-04): reconcile ONLY a subscription whose qs_purchaser metadata is
            // the value OUR checkout stamped for THIS account. A candidate that matched only
            // by the Stripe customer's bare, unverified Email carries no matching metadata -
            // skip it, never trust it (this is what makes an attacker's self-created customer
            // under the victim's email un-pickable).
            if (!IdentityMatches(candidate.PurchaserMetadata, account.Email))
            {
                skippedUnmatched++;
                _logger.LogInformation(
                    "Resync skipped Stripe subscription {SubscriptionId}: qs_purchaser metadata does not match the target account (candidate not stamped by our checkout).",
                    candidate.SubscriptionId);
                continue;
            }

            if (candidate.CapabilityKeys.Count == 0)
            {
                // A matched subscription with no recognizable capability metadata (a
                // pre-metadata legacy subscription). Never guessed at - skipped + warned.
                skippedNoMetadata++;
                _logger.LogWarning(
                    "Resync skipped Stripe subscription {SubscriptionId}: matched the account but carries no capability metadata.",
                    candidate.SubscriptionId);
                continue;
            }

            var leaseEnd = BillingLeaseMath.ResolveSubscriptionLeaseFromStatus(
                candidate.Status, candidate.CurrentPeriodEnd, now, _options.PastDueGraceDays);

            foreach (var capability in candidate.CapabilityKeys)
            {
                // Rule 2 (AC-08 + AC-05): overwrite ONLY a subscription grant already in the
                // active mode. A one-time pack, an operator comp, or a cross-mode subscription
                // grant is left completely untouched (read for this comparison only).
                if (byCapability.TryGetValue(capability, out var current)
                    && (current.Source != GrantSource.Subscription || current.Mode != activeMode))
                {
                    skippedModeGuard++;
                    _logger.LogInformation(
                        "Resync left grant {Capability} untouched: existing {Source}/{Mode} is not a subscription grant in the active {ActiveMode} mode.",
                        capability, current.Source, current.Mode, activeMode);
                    continue;
                }

                var grant = new EntitlementGrant(
                    capability, leaseEnd, GrantSource.Subscription,
                    PlanId: candidate.ProductId,
                    StripeSubscriptionId: candidate.SubscriptionId,
                    Mode: activeMode);
                await _grants.PutGrantAsync(account.Id, grant, ct);
                reconciled++;
            }
        }

        return new ResyncResult(
            BillingConfigured: true,
            ActiveMode: activeMode,
            Reconciled: reconciled,
            SkippedUnmatchedIdentity: skippedUnmatched,
            SkippedModeGuard: skippedModeGuard,
            SkippedNoMetadata: skippedNoMetadata);
    }

    // The qs_purchaser metadata match (AC-04): compares the stamped value to the account's
    // canonical email under the SAME trim + lowercase-invariant normalization the account
    // store uses, so a case / whitespace difference never defeats a legitimate match. A null
    // / blank metadata (an un-stamped candidate) never matches.
    private static bool IdentityMatches(string? purchaserMetadata, string accountEmail)
    {
        if (string.IsNullOrWhiteSpace(purchaserMetadata))
        {
            return false;
        }
        return string.Equals(purchaserMetadata.Trim().ToLowerInvariant(), accountEmail, StringComparison.Ordinal);
    }
}
