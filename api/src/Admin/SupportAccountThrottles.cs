// ----------------------------------------------------------------------------
//  SupportAccountThrottles - the PER-TARGET-ACCOUNT throttles the support console's
//  resend-magic-link (sysadmin-console/07, AC-03) and Stripe-resync (AC-07) verbs
//  enforce IN-CODE, keyed on the TARGET account - independent of which operator IP
//  triggers them.
//
//  WHY A SECOND AXIS (the review-flagged vectors): the public request endpoints these
//  verbs reuse are bounded PER-IP (SignInRateLimit / StripeResyncRateLimit). An operator
//  surface adds a distinct risk those per-IP limiters do not bound: a SINGLE operator
//  session (one IP) hammering the SAME account.
//    - Resend (AC-03b, the email-bomb vector): one operator resending a magic link over
//      and over to ONE inbox floods that inbox regardless of the per-IP limit. This cap
//      bounds resends PER TARGET ACCOUNT per window.
//    - Resync (AC-07, the Stripe-hammer vector): repeated clicks / repeated tickets for
//      ONE account fan out Stripe CustomerService.List + SubscriptionService.List calls.
//      This debounces resync PER TARGET ACCOUNT to a minimum interval.
//  The controller ALSO carries the per-IP [EnableRateLimiting] policy on each action, so
//  both axes apply (caller-side per-IP + target-side per-account), exactly as the story's
//  Technical Notes prescribe.
//
//  IN-CODE, NOT A NAMED RATE-LIMIT POLICY (deliberate): ASP.NET allows only ONE
//  [EnableRateLimiting] per endpoint (already spent on the per-IP policy), and the
//  partition here is a request BODY / route value (the target account), not the caller's
//  IP - so this is the "equivalent in-code check against a small per-account counter" the
//  story's Technical Notes explicitly allow, mirroring FamilyDeviceRedeemGlobalThrottle's
//  in-code shape. Both are tiny, thread-safe, and driven by an injectable TimeProvider so
//  tests advance the window deterministically.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// A per-target-account resend cap (sysadmin-console/07, AC-03b): allows at most
/// <see cref="PermitLimit"/> resend-magic-link actions to the SAME account within a rolling
/// <see cref="Window"/>, independent of the operator's IP - closing the email-bomb vector a
/// purely per-IP limiter does not bound. Thread-safe; a singleton in DI. The per-IP axis stays
/// on the action via [EnableRateLimiting(SignInRateLimit.PolicyName)].
/// </summary>
public sealed class SupportResendAccountThrottle
{
    /// <summary>Permitted resends to one account within <see cref="Window"/> (a handful, per AC-03b).</summary>
    public const int PermitLimit = 3;

    /// <summary>The rolling window the <see cref="PermitLimit"/> applies over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    // accountId -> the timestamps of resends still inside the window. A tiny list per account
    // (bounded by PermitLimit + in-flight), pruned on each check. ConcurrentDictionary keys the
    // per-account state; the small list is mutated under a per-account lock.
    private readonly ConcurrentDictionary<Guid, List<DateTimeOffset>> _hits = new();
    private readonly TimeProvider _clock;

    /// <summary>Constructs the throttle over the system clock (the DI default).</summary>
    public SupportResendAccountThrottle() : this(TimeProvider.System) { }

    /// <summary>Constructs the throttle over an injectable clock so a test advances the window deterministically.</summary>
    public SupportResendAccountThrottle(TimeProvider clock) => _clock = clock;

    /// <summary>
    /// Tries to record one resend against <paramref name="accountId"/>. Returns true and counts it
    /// when the account is under the cap for the current window; false (and records nothing) once the
    /// cap is reached - the caller returns 429 without sending. Prunes expired timestamps first, so
    /// the window is genuinely rolling.
    /// </summary>
    public bool TryAcquire(Guid accountId)
    {
        var now = _clock.GetUtcNow();
        var window = _hits.GetOrAdd(accountId, _ => new List<DateTimeOffset>());
        lock (window)
        {
            window.RemoveAll(at => now - at >= Window);
            if (window.Count >= PermitLimit)
            {
                return false;
            }
            window.Add(now);
            return true;
        }
    }
}

/// <summary>
/// A per-target-account resync debounce (sysadmin-console/07, AC-07): allows a Stripe resync for
/// one account at most once per <see cref="MinInterval"/>, independent of the operator's IP - so
/// repeated clicks or repeated tickets for ONE account cannot hammer the Stripe API. Thread-safe;
/// a singleton in DI. The per-IP / global axis stays on the action via
/// [EnableRateLimiting(StripeResyncRateLimit.PolicyName)].
/// </summary>
public sealed class SupportResyncAccountThrottle
{
    /// <summary>The minimum interval between resyncs for the SAME account (AC-07).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    // accountId -> the last time a resync was admitted for it. Point state per account.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastResync = new();
    private readonly TimeProvider _clock;

    /// <summary>Constructs the throttle over the system clock (the DI default).</summary>
    public SupportResyncAccountThrottle() : this(TimeProvider.System) { }

    /// <summary>Constructs the throttle over an injectable clock so a test advances the interval deterministically.</summary>
    public SupportResyncAccountThrottle(TimeProvider clock) => _clock = clock;

    /// <summary>
    /// Tries to admit one resync for <paramref name="accountId"/>. Returns true and stamps "now"
    /// when no resync ran for this account within <see cref="MinInterval"/>; false (stamping nothing)
    /// when a resync is still inside the debounce window - the caller returns 429 without calling
    /// Stripe. An AddOrUpdate keeps the check-and-stamp atomic per account under concurrency.
    /// </summary>
    public bool TryAcquire(Guid accountId)
    {
        var now = _clock.GetUtcNow();
        var admitted = false;
        _lastResync.AddOrUpdate(
            accountId,
            _ =>
            {
                admitted = true;
                return now;
            },
            (_, last) =>
            {
                if (now - last >= MinInterval)
                {
                    admitted = true;
                    return now;
                }
                return last;
            });
        return admitted;
    }
}
