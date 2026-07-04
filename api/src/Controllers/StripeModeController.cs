// ----------------------------------------------------------------------------
//  StripeModeController - the operator-only REST surface that READS and FLIPS the
//  active Stripe mode (billing-entitlements/06). The operator UI (story 07) is its
//  only caller.
//
//  WHAT IT DOES:
//    GET  /api/admin/stripe-mode  -> the current active mode + when it last changed
//    POST /api/admin/stripe-mode  -> flip the active mode (Test <-> Live)
//
//  OPERATOR-GATED (AC-06): BOTH verbs require an operator credential presented in the
//  X-Operator-Secret header, checked via IOperatorGate (the interim secret gate today,
//  the real sysadmin-console/01 operator session later - a one-file swap). A missing /
//  wrong credential is 401 and NOTHING is read or changed. Reading is a SEPARATE action
//  from flipping - there is no path by which a GET changes the mode, and the flip is a
//  POST with an explicit body (never a GET side effect).
//
//  CHILD-SAFETY-ADJACENT (AC-08): the active mode is an OPERATOR-only concern. It is
//  never exposed on any player-facing surface; this admin endpoint (gated) is the only
//  place it is readable, and it carries no player / room / purchaser data.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Billing;

namespace QuibbleStone.Api.Controllers;

/// <summary>The current active Stripe mode for the operator UI (billing-entitlements/06).</summary>
/// <param name="ActiveMode">"test" or "live" - the mode active for new checkouts.</param>
/// <param name="LastChangedUtc">When the mode last changed (null if never explicitly set).</param>
/// <param name="Enabled">Whether billing is configured at all (any mode has credentials).</param>
public sealed record StripeModeView(string ActiveMode, DateTimeOffset? LastChangedUtc, bool Enabled);

/// <summary>Request to flip the active Stripe mode.</summary>
/// <param name="Mode">The target mode - "test" or "live".</param>
public sealed record StripeModeChangeBody(string? Mode);

[ApiController]
[Route("api/admin/stripe-mode")]
public sealed class StripeModeController : ControllerBase
{
    /// <summary>The header the operator credential is presented in (interim gate; see IOperatorGate).</summary>
    public const string OperatorSecretHeader = "X-Operator-Secret";

    private readonly IActiveStripeContext _context;
    private readonly IOperatorGate _gate;
    private readonly ILogger<StripeModeController> _logger;

    public StripeModeController(IActiveStripeContext context, IOperatorGate gate, ILogger<StripeModeController> logger)
    {
        _context = context;
        _gate = gate;
        _logger = logger;
    }

    /// <summary>GET /api/admin/stripe-mode - the current active mode + last-changed (operator-gated).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!await IsOperatorAsync(ct))
        {
            return Unauthorized();
        }

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
        if (!await IsOperatorAsync(ct))
        {
            return Unauthorized();
        }

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

    // Reads the presented operator credential from the header and checks it via the gate.
    private Task<bool> IsOperatorAsync(CancellationToken ct)
    {
        var presented = Request.Headers[OperatorSecretHeader].ToString();
        return _gate.IsAuthorizedAsync(presented, ct);
    }
}
