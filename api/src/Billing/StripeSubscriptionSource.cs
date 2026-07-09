// ----------------------------------------------------------------------------
//  StripeSubscriptionSource - the live, Stripe-SDK-coupled edge of the resync path
//  (billing-entitlements/08, issue #215). The ONE place CustomerService /
//  SubscriptionService and the Stripe.* types live for reconciliation, mirroring how
//  StripeEventMapper isolates the webhook's Stripe coupling - so the security-critical
//  StripeReconciliationService stays pure and unit-testable.
//
//  WHAT IT DOES (AC-04 step 1-3): in the ACTIVE mode (IActiveStripeContext - reuse it,
//  do not build a second mode-resolution path), it lists Stripe customers filtered by
//  the account's email as CANDIDATES, then each candidate customer's subscriptions, and
//  projects every subscription to a normalized ReconciliationCandidate carrying its
//  qs_purchaser / qs_capabilities / qs_product metadata + status + current period end.
//  It applies NO trust decision itself - the bare email match is deliberately broad; the
//  metadata match that makes it un-steerable is the SERVICE's job (kept in tested code).
//
//  SUBSCRIPTION-ID + PERIOD-END SURFACE (pinned Stripe.net 52.x): the current period end
//  moved onto each SubscriptionItem in newer Stripe API versions, so the period end is
//  the latest item's CurrentPeriodEnd (mirroring StripeEventMapper reading it off invoice
//  line periods). Verified at build time against the pinned SDK; the live projection is
//  manual / integration verified (the story's test table), while the DECISIONS it feeds
//  are unit-tested through the normalized candidate.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Stripe;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The live <see cref="IStripeSubscriptionSource"/> (billing-entitlements/08): lists an
/// email's candidate Stripe customers + their subscriptions in the active mode and
/// projects them to normalized <see cref="ReconciliationCandidate"/>s. Registered only
/// when Stripe is configured (else <see cref="DisabledStripeSubscriptionSource"/>).
/// </summary>
public sealed class StripeSubscriptionSource : IStripeSubscriptionSource
{
    // Coarse page caps: a single account has at most a handful of Stripe customers /
    // subscriptions. Kept explicit so a runaway list cannot fan out unbounded (the
    // endpoint's rate limiter is the primary guard; this is defense in depth).
    private const int CustomerPageLimit = 100;
    private const int SubscriptionPageLimit = 100;

    private readonly IActiveStripeContext _context;
    private readonly ILogger<StripeSubscriptionSource> _logger;

    /// <summary>Constructs the source over the active-mode context (reused, not a second mode path).</summary>
    public StripeSubscriptionSource(IActiveStripeContext context, ILogger<StripeSubscriptionSource> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _context.IsBillingConfigured;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(string email, CancellationToken ct = default)
    {
        var config = await _context.GetActiveConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(config.SecretKey) || string.IsNullOrWhiteSpace(email))
        {
            // Billing on overall, but the ACTIVE mode has no secret (an operator misconfig),
            // or no email to match - nothing to reconcile against. Never call Stripe with an
            // empty key; degrade to an empty candidate set.
            return [];
        }

        // Per-call client from the active mode's key - no global mutable ApiKey state, same
        // posture as StripeCheckoutService.
        var client = new StripeClient(config.SecretKey);
        var customers = new CustomerService(client);
        var subscriptions = new SubscriptionService(client);

        var candidates = new List<ReconciliationCandidate>();

        // Step 1 (AC-04): email-matched customers are CANDIDATES only. Stripe does not verify
        // customer emails, so this list can include an attacker-created customer - the metadata
        // match downstream (in the service) is what filters that out.
        var customerList = await customers.ListAsync(
            new CustomerListOptions { Email = email, Limit = CustomerPageLimit }, cancellationToken: ct);

        foreach (var customer in customerList.Data)
        {
            // Step 2: each candidate customer's subscriptions (all statuses, so a canceled /
            // past_due subscription is reconciled to a lapsed / grace lease too).
            var subscriptionList = await subscriptions.ListAsync(
                new SubscriptionListOptions { Customer = customer.Id, Status = "all", Limit = SubscriptionPageLimit },
                cancellationToken: ct);

            foreach (var subscription in subscriptionList.Data)
            {
                candidates.Add(ToCandidate(subscription));
            }
        }

        _logger.LogDebug(
            "Resync listed {CustomerCount} candidate Stripe customer(s), {CandidateCount} subscription candidate(s).",
            customerList.Data.Count, candidates.Count);
        return candidates;
    }

    // Project a Stripe subscription to the normalized candidate the service reasons about.
    private static ReconciliationCandidate ToCandidate(Subscription subscription)
    {
        var metadata = subscription.Metadata;
        string? MetadataValue(string key) =>
            metadata is not null && metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        var capabilities = BillingMetadata.SplitCapabilities(MetadataValue(BillingMetadata.CapabilitiesKey));

        return new ReconciliationCandidate(
            SubscriptionId: subscription.Id,
            PurchaserMetadata: MetadataValue(BillingMetadata.PurchaserKey),
            CapabilityKeys: capabilities,
            ProductId: MetadataValue(BillingMetadata.ProductKey),
            Status: subscription.Status,
            CurrentPeriodEnd: CurrentPeriodEndOf(subscription));
    }

    // The subscription's current period end: the latest item's CurrentPeriodEnd (the field
    // moved onto SubscriptionItem in newer Stripe API versions). Null when no item carries
    // one (a defensive fall-through the lease math treats as terminal / grace-from-now).
    private static DateTimeOffset? CurrentPeriodEndOf(Subscription subscription)
    {
        DateTime? latest = null;
        foreach (var item in subscription.Items?.Data ?? [])
        {
            var end = item.CurrentPeriodEnd;
            if (latest is null || end > latest)
            {
                latest = end;
            }
        }
        // Stripe timestamps are UTC; carry that explicitly into the offset.
        return latest is { } value ? new DateTimeOffset(value, TimeSpan.Zero) : null;
    }
}

/// <summary>
/// The no-op <see cref="IStripeSubscriptionSource"/> registered when Stripe is not
/// configured (billing-entitlements/08). <see cref="IsEnabled"/> is false, so the
/// reconciliation service short-circuits to a clean "not configured" result and never
/// calls Stripe - the app runs with resync simply OFF and ZERO Stripe setup.
/// </summary>
public sealed class DisabledStripeSubscriptionSource : IStripeSubscriptionSource
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(string email, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReconciliationCandidate>>([]);
}
