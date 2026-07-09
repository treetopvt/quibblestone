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
using QuibbleStone.Api.Settings;

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
// sysadmin-console/05 (#214): the OPS scope (operations - settings / flags / Stripe mode).
// Same Operator credential boundary PLUS the Ops scope - a no-op for today's all-scopes
// operator (AC-05); a future ops-only operator is a config entry, not a rework (AC-06).
[Authorize(Policy = OperatorScopePolicy.Ops)]
public sealed class StripeModeController : ControllerBase
{
    // The action verb + target this controller logs (sysadmin-console/06 AC-01). The target is the
    // literal "stripe-mode" (a single app-wide flag - there is no per-entity target here).
    private const string ActionFlip = "stripe-mode.flip";
    private const string TargetStripeMode = "stripe-mode";

    private readonly IActiveStripeContext _context;
    private readonly IOperatorActionLog _actionLog;
    private readonly ILogger<StripeModeController> _logger;

    public StripeModeController(IActiveStripeContext context, IOperatorActionLog actionLog, ILogger<StripeModeController> logger)
    {
        _context = context;
        _actionLog = actionLog;
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
            // This validation fails BEFORE any log row is written (AC-05 - no row for a rejected flip).
            return BadRequest(new { message = "Mode must be 'test' or 'live'." });
        }

        // LOG-BEFORE-ACT (sysadmin-console/06 AC-01a): append the row BEFORE the effectful mode
        // flip. Validation (the parse above) already passed, so AC-05 holds; an append failure
        // aborts before SetModeAsync runs - a flip can never commit with no trail. Note is the new mode.
        await _actionLog.AppendAsync(
            User.Identity?.Name ?? string.Empty, ActionFlip, TargetStripeMode, mode.Value.ToWire(), ct);

        await _context.SetModeAsync(mode.Value, ct);
        // Operator action worth an audit-level trace (no secret, no PII - just the new mode).
        _logger.LogInformation("Active Stripe mode changed to {Mode}.", mode.Value.ToWire());

        var state = await _context.GetStateAsync(ct);
        return Ok(new StripeModeView(state.Mode.ToWire(), state.LastChangedUtc, _context.IsBillingConfigured));
    }
}
