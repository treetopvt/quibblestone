// ----------------------------------------------------------------------------
//  StripeModeController - the operator-only REST surface that READS and FLIPS the
//  active Stripe mode (billing-entitlements/06). Its only caller is the operator
//  console's Stripe-mode panel (sysadmin-console/04).
//
//  WHAT IT DOES:
//    GET  /api/admin/stripe-mode  -> the current active mode + when it last changed
//    POST /api/admin/stripe-mode  -> flip the active mode (Test <-> Live)
//
//  ONE CONSOLE, ONE AUTH (sysadmin-console/04): BOTH verbs are guarded by the REAL
//  operator authorization policy - [Authorize(Policy = OperatorSession.PolicyName)],
//  the SAME "Operator" policy AdminEntitlementsController and ReportedTalesController
//  already use. This REPLACES the interim shared-secret header gate (deleted in this
//  story): ASP.NET Core's authorization middleware now returns 401 before either
//  action runs for anyone without a valid operator session credential, so there is NO
//  manual credential check left inside the action bodies. A purchaser credential fails
//  to unprotect under the operator purpose and never reaches here (OperatorSession's
//  structural purchaser != operator guarantee).
//
//  OPERATOR-ONLY DATA, NO PII (AC-06): the active mode is an OPERATOR-only concern. It
//  is never exposed on any player-facing surface; this admin endpoint (gated) is the
//  only place it is readable, and it carries no player / room / session / purchaser
//  data of any kind - only the active mode and its last-changed timestamp.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Billing;

namespace QuibbleStone.Api.Admin;

/// <summary>The current active Stripe mode for the operator console (billing-entitlements/06).</summary>
/// <param name="ActiveMode">"test" or "live" - the mode active for new checkouts.</param>
/// <param name="LastChangedUtc">When the mode last changed (null if never explicitly set).</param>
/// <param name="Enabled">Whether billing is configured at all (any mode has credentials).</param>
public sealed record StripeModeView(string ActiveMode, DateTimeOffset? LastChangedUtc, bool Enabled);

/// <summary>Request to flip the active Stripe mode.</summary>
/// <param name="Mode">The target mode - "test" or "live".</param>
public sealed record StripeModeChangeBody(string? Mode);

[ApiController]
[Route("api/admin/stripe-mode")]
[Authorize(Policy = OperatorSession.PolicyName)]
public sealed class StripeModeController : ControllerBase
{
    private readonly IActiveStripeContext _context;
    private readonly ILogger<StripeModeController> _logger;

    public StripeModeController(IActiveStripeContext context, ILogger<StripeModeController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>GET /api/admin/stripe-mode - the current active mode + last-changed (operator-gated).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var state = await _context.GetStateAsync(ct);
        return Ok(new StripeModeView(state.Mode.ToWire(), state.LastChangedUtc, _context.IsBillingConfigured));
    }

    /// <summary>
    /// POST /api/admin/stripe-mode - flip the active mode (operator-gated). An unknown mode
    /// value is a 400 and nothing changes; the flip is stamped with a last-changed time (AC-07).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Set([FromBody] StripeModeChangeBody? body, CancellationToken ct)
    {
        var mode = StripeModeText.TryParse(body?.Mode);
        if (mode is null)
        {
            // Reject an unknown value rather than default silently (never accidentally go Live).
            return BadRequest(new { message = "Mode must be 'test' or 'live'." });
        }

        await _context.SetModeAsync(mode.Value, ct);
        // Operator action worth an audit-level trace (no secret, no PII - just the new mode).
        _logger.LogInformation("Active Stripe mode changed to {Mode}.", mode.Value.ToWire());

        var state = await _context.GetStateAsync(ct);
        return Ok(new StripeModeView(state.Mode.ToWire(), state.LastChangedUtc, _context.IsBillingConfigured));
    }
}
