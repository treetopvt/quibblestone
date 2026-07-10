// ----------------------------------------------------------------------------
//  AdultSignalResolutionService - the ONE place the connect-time "adult unlock"
//  decision lives (accounts-identity/10, issue #247, extracted from
//  accounts-identity/09's inline OnConnectedAsync branching).
//
//  WHY IT EXISTS (AC-06, "one resolver, not a fork"): accounts-identity/09 built
//  the teen-plus gate for GROUP play, resolving the adult-unlock signal inline in
//  GameHub.OnConnectedAsync. accounts-identity/10 adds the SOLO gate, which needs
//  the SAME signal from a REST endpoint (GET /api/accounts/adult-signal). Rather
//  than let the controller grow a second copy of the three-step decision that
//  could drift from the hub's, both callers route through THIS service - so solo
//  and group can never disagree about what "an adult signal is present" means.
//
//  THE THREE-STEP DECISION (identical to story 09's OnConnectedAsync, verbatim):
//    1. A PURCHASER credential (accounts-identity/03) that resolves to an email ->
//       true, adult-by-construction (only an adult completes a magic-link sign-in,
//       ADR 0002 Decision A).
//    2. Else a FAMILY-DEVICE token (accounts-identity/09) -> that device row's
//       IsAdultConfirmedDevice flag (false is the family-safe default a freshly
//       redeemed device carries - AC-03, mirroring group play's AC-07b).
//    3. Else (neither, the overwhelming common case - a kid's tablet, a fresh or
//       incognito browser) -> false.
//
//  IDENTITY DISCARDED AT THE BOUNDARY, STRUCTURALLY (ADR 0003 "Security posture";
//  ADR 0002's load-bearing invariant): this service returns ONLY the bool. The
//  purchaser email / family AccountId it resolves along the way lives only in a
//  local for the duration of the call and is NEVER returned, stored, or logged -
//  exactly as ResolvedConnectionIdentity carries no identity-shaped field. A
//  caller cannot obtain an identity from this service even if it wanted to.
//
//  FAIL-SAFE (AC-04, non-negotiable, this is a child-safety seam): every failure
//  mode - a bad/expired/tampered credential, a storage hiccup, ANY unforeseen
//  throw - resolves to false (family-safe). Only a positive, freshly resolved
//  true unlocks. The default IS the safe state, so an absent or failed resolution
//  never has to actively "turn off" anything. The service never throws.
//
//  A SINGLETON: it owns no per-request state and composes two existing singletons
//  (PurchaserCredentialService + FamilyDeviceLinkService). Registered in
//  Program.cs, injected into GameHub and AccountsController alike.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Resolves a presented connection/request credential to the connect-time
/// adult-unlock signal (accounts-identity/10, AC-06). The SINGLE shared decision
/// both <c>GameHub.OnConnectedAsync</c> (group play) and
/// <c>AccountsController.GetAdultSignal</c> (solo play) route through, so the two
/// can never fork. Returns ONLY the bool - the resolved identity is discarded at
/// the boundary, structurally (the interface has no way to surface it).
/// </summary>
public interface IAdultSignalResolver
{
    /// <summary>
    /// Resolves the adult-unlock signal for a credential (a purchaser session
    /// credential OR a family-device token - the SAME two the hub's
    /// accessTokenFactory supplies in one slot, purchaser preferred). Returns
    /// <c>true</c> ONLY for a resolved purchaser credential (adult-by-construction)
    /// or a family-device token whose row is adult-confirmed; <c>false</c> for
    /// everything else - no credential, an unconfirmed device, or ANY failure
    /// (bad/expired token, storage error, offline). NEVER throws (AC-04 fail-safe).
    /// </summary>
    /// <param name="credential">The bearer credential (purchaser session credential or family-device token), or null/empty for an anonymous request.</param>
    /// <param name="cancellationToken">Cancels the (device-token) store read.</param>
    /// <returns><c>true</c> only on a positive, freshly resolved adult signal; <c>false</c> otherwise.</returns>
    Task<bool> ResolveAdultSignalAsync(string? credential, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="IAdultSignalResolver"/>: the three-step decision
/// extracted verbatim from accounts-identity/09's OnConnectedAsync so solo and
/// group share ONE definition (AC-06). Fail-safe to <c>false</c> on every error
/// (AC-04). See the file header for the full rationale.
/// </summary>
public sealed class AdultSignalResolutionService : IAdultSignalResolver
{
    private readonly PurchaserCredentialService _purchaserCredentials;
    private readonly FamilyDeviceLinkService _deviceLinks;

    /// <summary>Constructs the resolver over the SAME purchaser + device-link resolvers the hub uses (never a second credential check).</summary>
    public AdultSignalResolutionService(
        PurchaserCredentialService purchaserCredentials,
        FamilyDeviceLinkService deviceLinks)
    {
        _purchaserCredentials = purchaserCredentials;
        _deviceLinks = deviceLinks;
    }

    /// <inheritdoc />
    public async Task<bool> ResolveAdultSignalAsync(string? credential, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(credential))
        {
            // No credential at all - the overwhelming common case (a kid's tablet, a
            // fresh/incognito browser). No adult signal (AC-01), family-safe.
            return false;
        }

        // 1. PURCHASER credential first (adult-by-construction). ResolvePurchaserEmail
        //    already swallows a bad/expired/tampered credential into null (never
        //    throws), but the broad catch keeps AC-04 absolute even against an
        //    unforeseen throw - the resolved email is used ONLY as a presence check
        //    here and is discarded at this boundary (never returned or stored).
        try
        {
            if (!string.IsNullOrEmpty(_purchaserCredentials.ResolvePurchaserEmail(credential)))
            {
                return true;
            }
        }
        catch
        {
            // Fail-safe: an unresolvable purchaser credential is not an adult signal.
            // Fall through to the device-token branch (the credential may be one).
        }

        // 2. Else a FAMILY-DEVICE token: resolve it to the device's adult-unlock flag.
        //    ResolveAsync parses / hash-verifies / liveness-checks the token and returns
        //    null on any miss (unknown / tampered / revoked / expired) - never throws -
        //    but the broad catch keeps AC-04 absolute against a storage hiccup too. A
        //    resolved-but-unconfirmed device is false (AC-03), the family-safe default.
        try
        {
            var device = await _deviceLinks.ResolveAsync(credential, cancellationToken);
            return device?.IsAdultConfirmedDevice ?? false;
        }
        catch
        {
            // 3. Fail-safe: any failure resolving the device token is treated as no
            //    adult signal (AC-04) - family-safe, never a default-open.
            return false;
        }
    }
}
