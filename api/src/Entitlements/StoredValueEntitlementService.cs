// ----------------------------------------------------------------------------
//  StoredValueEntitlementService - the REAL, stored-value implementation of the
//  session-creation entitlement seam (billing-entitlements/01, issue #70). It
//  REPLACES DefaultUnlockedEntitlementService behind the SAME IEntitlementService
//  contract (the DI swap in Program.cs is the only integration edit, AC-02) -
//  GameHub.CreateRoom, SessionEntitlements, and Room.cs are untouched.
//
//  HOW IT EVALUATES (once, at session-creation, never per-request):
//    1. Start from the DEFAULT-UNLOCKED baseline by COMPOSING
//       DefaultUnlockedEntitlementService (AC-03) - NOT re-deriving it. So "no
//       purchaser / no grant" is provably identical to today's shipped behavior:
//       the ai.* keys stay unlocked for every session, zero regression.
//    2. If a purchaser identity was resolved for this session (the value
//       GameHub.CreateRoom passes - null for every anonymous alpha session), look
//       the purchaser up via accounts-identity/02's IAccountStore (AC-06). We do
//       NOT duplicate identity / token-verification logic here - we consume the
//       already-resolved identity string and confirm an account exists.
//    3. For a real purchaser, read their grants in ONE partition read and UNLOCK
//       each capability whose lease is active right now (AC-04). An expired /
//       lapsed lease simply is not added, so it falls back to locked.
//
//  WHY THE BASELINE IS COMPOSED, NOT COPIED: if the alpha default ever changes, it
//  changes in ONE place (DefaultUnlockedEntitlementService) and this service inherits
//  it - there is no second copy of "what is unlocked by default" to drift (AC-03).
//
//  EXTENSIBILITY (AC-07): gating a NEW capability needs only (a) a catalog key and
//  (b) a grant row carrying it - no change here, to Room.cs, or to any hub method.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// The stored-value <see cref="IEntitlementService"/> (billing-entitlements/01):
/// the default-unlocked baseline (composed from <see cref="DefaultUnlockedEntitlementService"/>)
/// plus any capabilities a resolved purchaser holds an ACTIVE
/// <see cref="EntitlementGrant"/> for (AC-03/AC-04). Registered as the singleton
/// IEntitlementService in Program.cs, replacing the alpha stand-in. Anonymous
/// sessions (null purchaser) get exactly the baseline - zero behavior change.
/// </summary>
public sealed class StoredValueEntitlementService : IEntitlementService
{
    private readonly DefaultUnlockedEntitlementService _baseline;
    private readonly IAccountStore _accounts;
    private readonly IEntitlementGrantStore _grants;

    /// <summary>
    /// Constructs the stored-value service over the composed default-unlocked
    /// baseline, the purchaser-account store (accounts-identity/02, for AC-06
    /// purchaser resolution), and the grant store (AC-04/AC-05).
    /// </summary>
    /// <param name="baseline">The shipped default-unlocked service, composed as the "no grant" baseline (AC-03).</param>
    /// <param name="accounts">Resolves whether a purchaser account exists for the session's identity (AC-06) - no duplicate identity logic.</param>
    /// <param name="grants">The purchaser grant store (AC-05).</param>
    public StoredValueEntitlementService(
        DefaultUnlockedEntitlementService baseline,
        IAccountStore accounts,
        IEntitlementGrantStore grants)
    {
        _baseline = baseline;
        _accounts = accounts;
        _grants = grants;
    }

    /// <inheritdoc />
    public async ValueTask<SessionEntitlements> EvaluateForSession(
        string? purchaserIdentity = null,
        CancellationToken cancellationToken = default)
    {
        // 1. The default-unlocked baseline (AC-03) - composed, not re-derived.
        var baseline = await _baseline.EvaluateForSession(purchaserIdentity, cancellationToken);
        var unlocked = new HashSet<string>(baseline.UnlockedCapabilities, StringComparer.Ordinal);

        // 2. No purchaser resolved (every anonymous alpha session) -> baseline only.
        if (string.IsNullOrWhiteSpace(purchaserIdentity))
        {
            return new SessionEntitlements(unlocked);
        }

        // 3. Confirm an entitled purchaser exists via the account store (AC-06). No
        // account -> nothing to add beyond the baseline (a valid identity string with
        // no purchase behind it must not unlock anything paid).
        var account = await _accounts.GetByIdentityAsync(purchaserIdentity, cancellationToken);
        if (account is null)
        {
            return new SessionEntitlements(unlocked);
        }

        // 4. Add every capability whose lease is active right now (AC-04). Key the
        // grant read off the account's normalized identity so it aligns with the
        // account the store resolved. An expired lease is simply not added (locked).
        var now = DateTimeOffset.UtcNow;
        var grants = await _grants.GetGrantsAsync(account.Email, cancellationToken);
        foreach (var grant in grants)
        {
            if (grant.IsActiveAt(now))
            {
                unlocked.Add(grant.CapabilityKey);
            }
        }

        return new SessionEntitlements(unlocked);
    }
}
