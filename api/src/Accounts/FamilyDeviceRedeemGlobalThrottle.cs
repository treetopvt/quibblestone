// ----------------------------------------------------------------------------
//  FamilyDeviceRedeemGlobalThrottle - the GLOBAL ceiling layer of the family-device
//  redeem / refresh rate limiting (accounts-identity/09, issue #229; ADR 0003 security
//  posture: "enumerable codes get ... a GLOBAL ceiling, not only a per-IP limiter -
//  per-IP is defeated by IP rotation").
//
//  WHY IN-CODE (not a second rate-limit policy): ASP.NET Core allows only ONE
//  [EnableRateLimiting] policy per endpoint, and the per-IP limiter already claims it.
//  So the aggregate, cross-IP ceiling lives here as a tiny thread-safe fixed-window
//  counter the redeem + refresh actions check FIRST, before touching the code / token
//  stores. An IP-rotating attacker who slips past the per-IP limiter still hits this one
//  process-wide budget. Deterministic and unit-testable without the middleware.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A thread-safe, process-wide fixed-window counter bounding the AGGREGATE redeem /
/// refresh rate across all callers (accounts-identity/09). A singleton the controller
/// consults before each redeem/refresh; when the window's budget is spent every caller
/// is turned away (429) until the window rolls, regardless of source IP.
/// </summary>
public sealed class FamilyDeviceRedeemGlobalThrottle
{
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private readonly object _gate = new();

    private DateTimeOffset _windowStart;
    private int _countInWindow;

    /// <summary>The DI constructor: the ADR-mandated global ceiling + window (FamilyDeviceRedeemRateLimit).</summary>
    public FamilyDeviceRedeemGlobalThrottle()
        : this(FamilyDeviceRedeemRateLimit.GlobalPermitLimit, FamilyDeviceRedeemRateLimit.Window)
    {
    }

    /// <summary>The test constructor: an explicit (typically tiny) limit + window so the ceiling is exercisable fast.</summary>
    public FamilyDeviceRedeemGlobalThrottle(int permitLimit, TimeSpan window)
    {
        _permitLimit = permitLimit;
        _window = window;
        _windowStart = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Tries to consume one global permit. Returns true when the current window still has
    /// budget (and consumes one), false when the aggregate ceiling is reached. The window
    /// rolls forward lazily on the first call after it elapses. Anonymous by construction:
    /// it counts calls, never a caller identity or IP.
    /// </summary>
    public bool TryAcquire()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _windowStart >= _window)
            {
                _windowStart = now;
                _countInWindow = 0;
            }

            if (_countInWindow >= _permitLimit)
            {
                return false;
            }

            _countInWindow += 1;
            return true;
        }
    }
}
