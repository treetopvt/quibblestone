// ----------------------------------------------------------------------------
//  AdminBillingResyncController - the OPERATOR-only endpoint that runs a per-account
//  "resync from Stripe" (billing-entitlements/08, issue #215, ADR 0003 Layer 2). It is
//  the recovery path sysadmin-console/07's future "per-account Stripe resync" support
//  verb will call; this story ships the endpoint + service so it is independently
//  testable (curl / an integration test) without waiting on that console screen.
//
//  OPERATOR-ONLY, NEVER AUTOMATIC (AC-06): the single action is behind the REAL
//  [Authorize(Policy = OperatorSession.PolicyName)] boundary - EXACTLY like
//  AdminEntitlementsController and StripeModeController - so a purchaser credential fails
//  to unprotect under the operator purpose and never reaches here, and an unauthenticated
//  caller gets 401. It runs ONLY when an operator explicitly POSTs it: no schedule, no
//  side effect of any other request (webhooks remain the routine source of truth).
//
//  RATE-LIMITED (AC-06d): the action opts into StripeResyncRateLimit (a GLOBAL fixed
//  window), so a scripted / accidental loop cannot fan out unbounded CustomerService.List
//  + SubscriptionService.List traffic against Stripe. A call beyond the budget gets 429.
//
//  IDENTITY IN, NEVER EMAIL-STEERABLE (AC-04): the target is an AccountId (the durable
//  spine accounts-identity/05 minted - grants already key off it). The controller resolves
//  it through IAccountStore.GetByIdAsync (never a raw string typed at call time), confirming
//  the account exists before any Stripe read. The service then matches Stripe subscriptions
//  by the qs_purchaser metadata OUR checkout stamped for that account, never by a Stripe
//  customer's bare email (see StripeReconciliationService).
//
//  DEGRADED-PATH NOTE (accounts-identity/05 has landed): the input is the stable AccountId.
//  Were this built before the AccountId spine, the input would instead be the account's
//  canonical email resolved via IAccountStore.GetByIdentityAsync - a small, contract-
//  compatible parameter swap, not a rewrite. Recorded here so the swap is not forgotten.
//
//  ANONYMITY FIREWALL (AC-07): this surface operates ENTIRELY on the purchaser plane
//  (account id in, grant rows out). It injects only IAccountStore + the resync service,
//  imports NOTHING from api/src/Rooms or the hubs, and returns PII-free counts only - no
//  nickname, room code, or session id.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Billing;

namespace QuibbleStone.Api.Admin;

/// <summary>The resync request body (billing-entitlements/08): the target account's stable id.</summary>
/// <param name="AccountId">The stable AccountId to reconcile (accounts-identity/05). Resolved via IAccountStore - never a raw email typed at call time.</param>
public sealed record ResyncRequest(Guid? AccountId);

/// <summary>
/// The resync response (billing-entitlements/08): whether the account was found + billing
/// is configured, the mode the resync ran against, and PII-free counts of what changed /
/// was skipped (AC-07). No nickname / room / session ever appears here.
/// </summary>
/// <param name="AccountFound">True when an account exists for the requested id (else a clear not-found state).</param>
/// <param name="BillingConfigured">False when Stripe is not configured - nothing was read or written.</param>
/// <param name="ActiveMode">The Stripe mode the resync ran against ("test" / "live"), or null when not run.</param>
/// <param name="Reconciled">How many capability grants were written / overwritten.</param>
/// <param name="SkippedUnmatchedIdentity">Candidate subscriptions skipped for a non-matching qs_purchaser (AC-04).</param>
/// <param name="SkippedModeGuard">Existing grants left untouched by the mode / source guard (AC-05 / AC-08).</param>
/// <param name="SkippedNoMetadata">Matched subscriptions skipped for carrying no capability metadata.</param>
/// <param name="Message">A friendly operator-facing summary.</param>
public sealed record ResyncResponse(
    bool AccountFound,
    bool BillingConfigured,
    string? ActiveMode,
    int Reconciled,
    int SkippedUnmatchedIdentity,
    int SkippedModeGuard,
    int SkippedNoMetadata,
    string Message);

[ApiController]
[Route("api/admin/billing")]
[Authorize(Policy = OperatorSession.PolicyName)]
public sealed class AdminBillingResyncController : ControllerBase
{
    private readonly IAccountStore _accounts;
    private readonly IStripeReconciliationService _reconciliation;

    /// <summary>
    /// Constructs the controller over the account store (target resolution, AC-04) and the
    /// reconciliation service (billing-entitlements/08). It ORCHESTRATES those - it never
    /// reimplements them and never touches any room / player store (AC-07).
    /// </summary>
    public AdminBillingResyncController(IAccountStore accounts, IStripeReconciliationService reconciliation)
    {
        _accounts = accounts;
        _reconciliation = reconciliation;
    }

    /// <summary>
    /// POST /api/admin/billing/resync - reconcile ONE account's subscription grants from
    /// Stripe (operator-gated, rate-limited). Resolves the AccountId to an account FIRST
    /// (a read that never creates), then runs the resync. Returns a clear not-found state
    /// (not a 404 error) when no account exists, mirroring AdminEntitlementsController's
    /// lookup posture. Idempotent (AC-06): invoking it twice against the same Stripe state
    /// produces the same grants.
    /// </summary>
    [HttpPost("resync")]
    [EnableRateLimiting(StripeResyncRateLimit.PolicyName)]
    public async Task<IActionResult> Resync([FromBody] ResyncRequest? request, CancellationToken cancellationToken)
    {
        if (request?.AccountId is not { } accountId || accountId == Guid.Empty)
        {
            return BadRequest(new { message = "A target accountId is required." });
        }

        // Resolve the target through the account store (never a raw string) and confirm it
        // exists before any Stripe read (AC-04).
        var account = await _accounts.GetByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Ok(new ResyncResponse(
                AccountFound: false, BillingConfigured: false, ActiveMode: null,
                0, 0, 0, 0, "No account found for that id - nothing to resync."));
        }

        var result = await _reconciliation.ResyncAccountAsync(account, cancellationToken);
        var message = !result.BillingConfigured
            ? "Billing is not configured - nothing to resync."
            : $"Resync complete: {result.Reconciled} grant(s) reconciled, "
                + $"{result.SkippedUnmatchedIdentity} unmatched, {result.SkippedModeGuard} mode-guarded, "
                + $"{result.SkippedNoMetadata} without metadata.";

        return Ok(new ResyncResponse(
            AccountFound: true,
            BillingConfigured: result.BillingConfigured,
            ActiveMode: result.ActiveMode?.ToWire(),
            Reconciled: result.Reconciled,
            SkippedUnmatchedIdentity: result.SkippedUnmatchedIdentity,
            SkippedModeGuard: result.SkippedModeGuard,
            SkippedNoMetadata: result.SkippedNoMetadata,
            Message: message));
    }
}
