// ----------------------------------------------------------------------------
//  StripeResyncRateLimit - the fixed-window rate-limit policy for the per-account
//  Stripe resync endpoint (billing-entitlements/08, issue #215, AC-06d). Mirrors the
//  shape of OperatorLoginRateLimit / CloudGalleryRateLimit (a PolicyName, a PermitLimit,
//  a Window, a partition-key function), registered in Program.cs and opted into via
//  [EnableRateLimiting] on the endpoint action.
//
//  WHY - AND WHY A GLOBAL (NOT PER-IP) PARTITION KEY: a resync fans out
//  CustomerService.List + SubscriptionService.List calls against Stripe's API. A
//  scripted or accidental loop of resync calls could fan that out unbounded and disrupt
//  concurrent LIVE webhook processing. Unlike the anonymous per-IP limiters, the abuse
//  scenario here is repeated INVOCATION against Stripe - and the operator-only auth
//  already scopes the caller to a single trusted actor - so the partition is a CONSTANT
//  GLOBAL key: the whole endpoint shares ONE small budget rather than each IP getting
//  its own (which a distinct caller could otherwise use to multiply the Stripe traffic).
//  A request beyond the budget is rejected with 429, the same posture as every other
//  throttled endpoint in the app.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The GLOBAL fixed-window rate-limit policy applied to the operator Stripe-resync
/// endpoint (billing-entitlements/08, AC-06d). Program.cs registers the policy under
/// <see cref="PolicyName"/>; the resync action opts in via [EnableRateLimiting]. Tunables
/// + partition key live here as one source of truth, unit-testable without the middleware.
/// </summary>
public static class StripeResyncRateLimit
{
    /// <summary>The named policy the resync action opts into.</summary>
    public const string PolicyName = "StripeResync";

    /// <summary>
    /// Permitted resync calls per <see cref="Window"/> across the WHOLE endpoint (one
    /// shared budget, not per-IP). Ample for an operator reconciling a few purchasers in a
    /// support session, tight enough that a scripted / accidental loop cannot fan out
    /// unbounded Stripe list traffic and disrupt live webhook processing.
    /// </summary>
    public const int PermitLimit = 5;

    /// <summary>The fixed window the <see cref="PermitLimit"/> applies over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The rate-limit partition key: a CONSTANT global bucket (NOT the caller's IP). The
    /// abuse scenario is repeated invocation against Stripe, which the operator-only auth
    /// already scopes to one trusted actor - so the whole endpoint shares one budget rather
    /// than each IP getting its own. The <see cref="HttpContext"/> is accepted (and ignored)
    /// to match the partition-key delegate shape the other limiter policies use.
    /// </summary>
    public static string PartitionKey(HttpContext context) => "stripe-resync-global";
}
