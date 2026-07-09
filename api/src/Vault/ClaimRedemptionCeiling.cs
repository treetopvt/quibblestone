// ----------------------------------------------------------------------------
//  ClaimRedemptionCeiling - the GLOBAL, IP-agnostic redemption ceiling for the
//  vault claim-code redeem endpoint (keepsake-vault/03, AC-03.2, issue #230).
//
//  WHY A GLOBAL CEILING, DISTINCT FROM THE PER-IP LIMITER (AC-03.2, the load-bearing
//  control): the per-IP fixed-window limiter (VaultRateLimit, AC-03.1) is defeated by
//  an attacker who ROTATES SOURCE IPS - each IP gets its own fresh per-IP budget, so
//  a distributed guesser sidesteps it entirely. What actually bounds a distributed,
//  IP-rotating brute force against the whole 31^9 keyspace is a SINGLE budget shared
//  across ALL callers: a low fixed cap on TOTAL redemption attempts per minute,
//  independent of source IP. That is this class. Tuned loose enough that legitimate
//  concurrent family recoveries never hit it, tight enough that even an attacker with
//  unlimited IPs cannot make more than <see cref="GlobalRedemptionCeiling"/> guesses
//  a minute - so the code's 7-day validity window (AC-07) elapses and it rotates
//  (AC-07) or burns (AC-03.3) long before the keyspace is meaningfully sampled.
//
//  WHY A SMALL SERVICE, NOT A RATE-LIMITER POLICY: the built-in limiter policies are
//  per-request-partitioned; applying BOTH a per-IP limiter AND an IP-agnostic global
//  one to a SINGLE endpoint would mean chaining two partitioned limiters (awkward and
//  easy to get wrong). Instead the per-IP control stays an [EnableRateLimiting] policy
//  (VaultRateLimit.RedeemPolicyName) and this global control is a tiny, directly
//  unit-testable fixed-window counter the redeem action checks first. A CONSTANT,
//  IP-agnostic bucket by construction - it reads NOTHING about the caller (AC-06: it
//  never sees an IP, an account, a vault id, or the code).
//
//  Registered as a singleton in Program.cs; a fresh SignalR/HTTP request cannot hold
//  window state, so the shared counter must live in one long-lived instance. The
//  clock is an injected TimeProvider so the fixed window is deterministic under test
//  (AC-03.2: rejects once exceeded regardless of varying source IPs).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Vault;

/// <summary>
/// A single, process-wide, IP-agnostic fixed-window ceiling on vault claim-code
/// redemption attempts (keepsake-vault/03, AC-03.2). Permits at most
/// <see cref="GlobalRedemptionCeiling"/> attempts per <see cref="Window"/> across ALL
/// callers combined - the control that bounds a distributed, IP-rotating brute force
/// the per-IP limiter cannot. Thread-safe, allocation-light, and directly
/// unit-testable via an injected <see cref="TimeProvider"/>. Reads nothing about the
/// caller (AC-06). Registered as a singleton so the window state is shared.
/// </summary>
public sealed class ClaimRedemptionCeiling
{
    /// <summary>
    /// The maximum redemption attempts permitted per <see cref="Window"/> across the
    /// WHOLE endpoint (one shared budget, not per-IP), AC-03.2. Loose enough that
    /// legitimate concurrent family recoveries never hit it, tight enough to make
    /// distributed guessing across the 31^9 keyspace impractical within a code's
    /// 7-day validity window. A settings-key candidate shipped as a code constant
    /// until control-plane/01's catalog exists.
    /// </summary>
    public const int GlobalRedemptionCeiling = 60;

    /// <summary>The fixed window the <see cref="GlobalRedemptionCeiling"/> applies over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private long _windowStartTicks;
    private int _countInWindow;

    /// <summary>
    /// Constructs the ceiling over a clock (defaulting to the system clock). Tests
    /// pass a fake <see cref="TimeProvider"/> to advance past the window
    /// deterministically.
    /// </summary>
    /// <param name="clock">The time source; defaults to <see cref="TimeProvider.System"/>.</param>
    public ClaimRedemptionCeiling(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _windowStartTicks = _clock.GetUtcNow().UtcTicks;
    }

    /// <summary>
    /// Tries to admit ONE redemption attempt against the shared budget. Returns true
    /// when the attempt is within this window's remaining budget (and consumes one
    /// permit), or false when the ceiling for the current window is already reached -
    /// the caller then returns 429 without touching the store. The window rolls over
    /// lazily: the first call at or past <see cref="Window"/> since the window started
    /// resets the count. IP-agnostic by construction (AC-03.2) - it consults nothing
    /// about the caller.
    /// </summary>
    /// <returns>True if the attempt is admitted; false if the global ceiling is exceeded.</returns>
    public bool TryAcquire()
    {
        var now = _clock.GetUtcNow().UtcTicks;
        lock (_gate)
        {
            if (now - _windowStartTicks >= Window.Ticks)
            {
                // A fresh fixed window: reset the count and anchor it at now.
                _windowStartTicks = now;
                _countInWindow = 0;
            }

            if (_countInWindow >= GlobalRedemptionCeiling)
            {
                return false;
            }

            _countInWindow++;
            return true;
        }
    }
}
