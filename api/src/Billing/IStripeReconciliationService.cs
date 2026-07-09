// ----------------------------------------------------------------------------
//  IStripeReconciliationService - the per-account "resync from Stripe" recovery path
//  (billing-entitlements/08, issue #215, ADR 0003 Layer 2). Webhooks remain the
//  ROUTINE source of truth for every grant write; this service is the OPERATOR-
//  triggered recovery action for when a webhook was missed (a sustained Stripe
//  outage), or an operator edited a subscription directly in the Stripe dashboard and
//  the local grant drifted from Stripe's authoritative state. It is NEVER a schedule,
//  NEVER a per-request check (README section 3's "not per-request" holds here too).
//
//  THE TWO BINDING SECURITY RULES (ADR 0003 "Stripe resync cannot corrupt grants",
//  revised 2026-07-08 after the adversarial review) live in the SERVICE, not the
//  Stripe-coupled edge, so they are unit-testable:
//    1. IDENTITY IS METADATA-MATCHED, NEVER EMAIL-STEERABLE (AC-04). Stripe does NOT
//       verify a customer's email, so an attacker can create their OWN Stripe customer
//       under a victim's email. We therefore treat every email-matched customer's
//       subscription as a CANDIDATE only, and reconcile ONLY a subscription whose
//       `qs_purchaser` metadata (the value OUR checkout stamped for THIS account)
//       equals the account's identity. A candidate with no matching metadata never went
//       through our checkout - it is SKIPPED and logged, never trusted.
//    2. THE STORE IS MODE-AWARE (AC-08). Before writing any capability's grant we read
//       the existing row and compare its stored Mode to the currently-active mode. A
//       row whose Mode differs (a Live grant seen while Test is active, or vice versa)
//       is left byte-for-byte untouched. A Test-mode resync can NEVER overwrite a
//       Live-derived grant, symmetrically in both directions.
//
//  ANONYMITY (AC-07): the service operates SOLELY on the purchaser plane - an account
//  identity in, Stripe customer / subscription lookups, grant rows out. It never looks
//  up, joins, or returns any player nickname, room code, or session id, and imports
//  nothing from api/src/Rooms.
//
//  SDK ISOLATION: all Stripe.* type knowledge lives behind IStripeSubscriptionSource
//  (the live impl is StripeSubscriptionSource, verified manually / by integration like
//  StripeEventMapper). The service works purely on the normalized ReconciliationCandidate,
//  so the security-critical decisions above are testable with zero live Stripe.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Reconciles ONE purchaser account's subscription-sourced entitlement grants against
/// their current Stripe state (billing-entitlements/08). Operator-triggered recovery
/// only - never scheduled, never per-request. See the file header for the two binding
/// security rules (metadata-matched identity, mode-aware store).
/// </summary>
public interface IStripeReconciliationService
{
    /// <summary>
    /// Resyncs <paramref name="account"/>'s subscription grants from Stripe (the active
    /// mode), returning a PII-free summary of what changed. Idempotent (AC-06): re-running
    /// against the same Stripe state produces the same grants, no duplicate rows.
    /// </summary>
    /// <param name="account">The already-resolved target account (Id keys the grant writes; Email matches Stripe candidates + metadata). Resolved by the caller via IAccountStore, never a raw string.</param>
    /// <param name="ct">Cancellation for the Stripe reads + grant writes.</param>
    Task<ResyncResult> ResyncAccountAsync(Account account, CancellationToken ct = default);
}

/// <summary>
/// A normalized, Stripe-SDK-free view of one candidate subscription (billing-entitlements/08).
/// Produced by <see cref="IStripeSubscriptionSource"/> for every subscription of every Stripe
/// customer matching the account's email - a CANDIDATE, not yet a trusted match. The
/// reconciliation service applies the `qs_purchaser` metadata check + the mode guard to it.
/// </summary>
/// <param name="SubscriptionId">The Stripe subscription id (recorded on the grant, AC-01).</param>
/// <param name="PurchaserMetadata">The subscription's <c>qs_purchaser</c> metadata (the value our checkout stamped), or null if absent - an unmatched/attacker-created candidate carries none.</param>
/// <param name="CapabilityKeys">The subscription's <c>qs_capabilities</c> metadata, split into keys (empty for a subscription with no recognizable metadata - skipped).</param>
/// <param name="ProductId">The subscription's <c>qs_product</c> metadata (recorded as the grant's PlanId), or null if absent.</param>
/// <param name="Status">The Stripe subscription status (active / trialing / past_due / canceled / ...), driving the lease math.</param>
/// <param name="CurrentPeriodEnd">The subscription's current period end, or null if unknown.</param>
public sealed record ReconciliationCandidate(
    string SubscriptionId,
    string? PurchaserMetadata,
    IReadOnlyList<string> CapabilityKeys,
    string? ProductId,
    string Status,
    DateTimeOffset? CurrentPeriodEnd);

/// <summary>
/// The PII-free outcome of a resync run (billing-entitlements/08) - counts only, never a
/// nickname / room / session (AC-07). Surfaced to the operator so a support action shows
/// what it did and why some candidates were skipped.
/// </summary>
/// <param name="BillingConfigured">False when Stripe is not configured / no active-mode secret - nothing was read or written.</param>
/// <param name="ActiveMode">The Stripe mode the resync ran against (null when billing is not configured).</param>
/// <param name="Reconciled">How many capability grants were written / overwritten.</param>
/// <param name="SkippedUnmatchedIdentity">Candidate subscriptions skipped because their <c>qs_purchaser</c> metadata did not match the account (AC-04 - an attacker-steered / unrelated customer).</param>
/// <param name="SkippedModeGuard">Existing grants left untouched because they are not a subscription grant in the active mode (AC-05 one-time / operator comp, or AC-08 cross-mode).</param>
/// <param name="SkippedNoMetadata">Matched subscriptions skipped because they carried no recognizable capability metadata (a pre-metadata legacy subscription).</param>
public sealed record ResyncResult(
    bool BillingConfigured,
    StripeMode? ActiveMode,
    int Reconciled,
    int SkippedUnmatchedIdentity,
    int SkippedModeGuard,
    int SkippedNoMetadata);

/// <summary>
/// The Stripe-coupled edge that lists CANDIDATE subscriptions for an email in the ACTIVE
/// mode (billing-entitlements/08). The one place CustomerService / SubscriptionService and
/// the Stripe.* types live for the resync path, mirroring how StripeEventMapper isolates the
/// webhook's Stripe coupling - so <see cref="IStripeReconciliationService"/> stays pure.
/// </summary>
public interface IStripeSubscriptionSource
{
    /// <summary>True when a real Stripe read can run (billing configured with an active-mode secret key).</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Lists EVERY subscription of EVERY Stripe customer whose email matches
    /// <paramref name="email"/> in the currently-active mode, as normalized candidates. The
    /// bare email match is deliberately broad (candidates, not winners) - the caller applies
    /// the `qs_purchaser` metadata check that makes the match un-steerable (AC-04).
    /// </summary>
    Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(string email, CancellationToken ct = default);
}
