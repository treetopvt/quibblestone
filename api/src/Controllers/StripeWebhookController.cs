// ----------------------------------------------------------------------------
//  StripeWebhookController - the single, self-contained REST endpoint Stripe calls
//  to confirm payments and subscription lifecycle changes (billing-entitlements/03,
//  issue #72). It is request/response (Stripe POSTs here directly over HTTP), NOT a
//  hub method - so it lives with the other controllers, mapped by app.MapControllers.
//
//  ISOLATION (AC-04): this controller + StripeEventMapper + StripeWebhookHandler are
//  the whole webhook surface, with no cross-cutting dependencies, so lifting them
//  into an Azure Function later (README section 4's natural first carve-out) is a
//  move, not a rewrite. No Function project is created now.
//
//  SIGNATURE FIRST (AC-03): the raw request body is verified against the Stripe
//  signing secret (Key Vault-backed, AC-01) via Stripe's EventUtility BEFORE any
//  processing. An invalid/absent signature is a 400 and NOTHING is applied. When
//  billing is not configured (no signing secret in EITHER mode), the endpoint returns
//  503 - it is never reached in a config-off environment (Stripe is not wired to call it).
//
//  DUAL-MODE VERIFY (billing-entitlements/06 AC-04): live and test are separate Stripe
//  webhook endpoints that POST to this SAME URL, each event signed with its own mode's
//  signing secret. We cannot know a priori which mode an event came from, and we must
//  NOT branch on the currently-ACTIVE mode - a mode flip mid-flight must not orphan an
//  in-progress checkout's webhook. So we try each configured mode's signing secret and
//  accept the event if EITHER verifies; only reject (400) if BOTH fail.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Billing;
using Stripe;

namespace QuibbleStone.Api.Controllers;

/// <summary>
/// Receives Stripe webhook events (billing-entitlements/03): verifies the signature
/// against the Key Vault-backed signing secret (AC-03), maps the event to a normalized
/// <see cref="BillingEvent"/>, and applies it via <see cref="StripeWebhookHandler"/>.
/// </summary>
[ApiController]
[Route("api/stripe")]
public sealed class StripeWebhookController : ControllerBase
{
    private readonly StripeWebhookHandler _handler;
    private readonly StripeOptions _options;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        StripeWebhookHandler handler,
        StripeOptions options,
        ILogger<StripeWebhookController> logger)
    {
        _handler = handler;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/stripe/webhook - Stripe's event callback. Verifies the signature,
    /// then applies the event (idempotently). Returns 400 on a bad signature (nothing
    /// applied), 503 when billing is not configured, else 200 with the outcome.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        // Each configured mode's signing secret, distinct + non-empty (AC-04). The flat
        // fallback means an old single-mode wiring collapses to one secret. No secret in
        // any mode => billing is not configured; this path is not reached in practice
        // (Stripe is not wired to call an unconfigured app).
        var signingSecrets = new[] { _options.ForMode(StripeMode.Test).WebhookSigningSecret, _options.ForMode(StripeMode.Live).WebhookSigningSecret }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (signingSecrets.Length == 0)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // Read the RAW body - signature verification is over the exact bytes Stripe sent.
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        // Verify against each mode's secret, accepting on the FIRST that validates (AC-04):
        // the event is signed by exactly one mode, but we do not know which, and must not
        // branch on the currently-active mode.
        //
        // throwOnApiVersionMismatch: false is REQUIRED, not optional: a webhook event
        // carries the STRIPE ACCOUNT's API version (e.g. "2020-03-02"), which is
        // independent of the Stripe.net SDK's pinned version. Leaving the default (true)
        // makes ConstructEvent throw on every real event whose account version differs
        // from the SDK's - i.e. it would reject ALL real webhooks. We only need signature
        // + integrity here; the event shape we read (type + metadata + line period) is
        // stable across versions. (Caught live during the test-mode end-to-end verification
        // - the handler unit tests operate on the normalized BillingEvent, downstream of
        // ConstructEvent, so they could not surface this.)
        Event? stripeEvent = null;
        foreach (var secret in signingSecrets)
        {
            try
            {
                stripeEvent = EventUtility.ConstructEvent(payload, signature, secret, throwOnApiVersionMismatch: false);
                break;
            }
            catch (StripeException)
            {
                // This mode's secret did not validate - try the next before rejecting.
            }
        }

        if (stripeEvent is null)
        {
            // Invalid / missing signature for EVERY configured mode - reject, apply
            // nothing. Do not log the payload/secret.
            _logger.LogWarning("Rejected a Stripe webhook with an invalid or missing signature.");
            return BadRequest();
        }

        var billingEvent = StripeEventMapper.ToBillingEvent(stripeEvent);
        var outcome = await _handler.HandleAsync(billingEvent, cancellationToken);
        _logger.LogDebug("Handled Stripe event {Kind} -> {Outcome}.", billingEvent.Kind, outcome);
        return Ok(new { outcome = outcome.ToString() });
    }
}
