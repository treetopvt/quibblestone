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
//  SYSTEM-SCOPE FILTER (control-plane/02, #213): a third concern now composes AFTER
//  the baseline + grant steps above - an app-wide system scope (kill switch /
//  not-yet-launched flag). This is a POST-COMPOSE FILTER, not an early branch: steps
//  1-4 run UNCONDITIONALLY and UNCHANGED, then SystemFlagEvaluator subtracts any
//  capability whose owning system flag reads effectively false, immediately before
//  the set is captured. So "system force-off wins over any account grant" holds for
//  every session, without ever skipping grant evaluation. The IEntitlementService
//  contract and the capture-once discipline are untouched (AC-06); only what feeds
//  the evaluation changed. The evaluator is injected (it bundles exactly the
//  IRuntimeSettingsService + SystemConfigPresence the filter needs, keeping this
//  service single-responsibility and the effective-value logic unit-testable on its
//  own).
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
    private readonly SystemFlagEvaluator _systemFlags;

    /// <summary>
    /// Constructs the stored-value service over the composed default-unlocked
    /// baseline, the purchaser-account store (accounts-identity/02, for AC-06
    /// purchaser resolution), the grant store (AC-04/AC-05), and the system-scope
    /// flag evaluator (control-plane/02, the post-compose kill-switch filter).
    /// </summary>
    /// <param name="baseline">The shipped default-unlocked service, composed as the "no grant" baseline (AC-03).</param>
    /// <param name="accounts">Resolves whether a purchaser account exists for the session's identity (AC-06) - no duplicate identity logic.</param>
    /// <param name="grants">The purchaser grant store (AC-05).</param>
    /// <param name="systemFlags">The system-scope flag evaluator (control-plane/02): the post-compose filter that force-removes any capability whose owning system flag reads effectively false.</param>
    public StoredValueEntitlementService(
        DefaultUnlockedEntitlementService baseline,
        IAccountStore accounts,
        IEntitlementGrantStore grants,
        SystemFlagEvaluator systemFlags)
    {
        _baseline = baseline;
        _accounts = accounts;
        _grants = grants;
        _systemFlags = systemFlags;
    }

    /// <inheritdoc />
    public async ValueTask<SessionEntitlements> EvaluateForSession(
        string? purchaserIdentity = null,
        CancellationToken cancellationToken = default)
    {
        // 1. The default-unlocked baseline (AC-03) - composed, not re-derived.
        var baseline = await _baseline.EvaluateForSession(purchaserIdentity, cancellationToken);
        var unlocked = new HashSet<string>(baseline.UnlockedCapabilities, StringComparer.Ordinal);

        // 2. A resolved purchaser (never in an anonymous alpha session) adds their active
        // grants on top of the baseline. Anonymous / no-account sessions skip this and keep
        // exactly the baseline - but ALL paths still fall through to the system-flag filter
        // in step 4 (a kill switch forces a capability off even for an anonymous session).
        if (!string.IsNullOrWhiteSpace(purchaserIdentity))
        {
            // 3. Confirm an entitled purchaser exists via the account store (AC-06). No
            // account -> nothing to add beyond the baseline (a valid identity string with
            // no purchase behind it must not unlock anything paid). Add every capability
            // whose lease is active right now (AC-04). Read grants keyed off the account's
            // STABLE id (account.Id, accounts-identity/05), NOT a hash of the (mutable)
            // email. LOAD-BEARING CONTRACT for the write side (stories 03-04, and the
            // operator grant/revoke #136): a grant MUST be written keyed off the same value
            // - i.e. resolve the identity to an Account first and key the grant off
            // account.Id. Because the id never changes (an email change does not move it,
            // AC-02), a write and this read always align; a grant written off any other
            // value would silently read back empty and leave a paid capability locked. An
            // expired lease is simply not added.
            var account = await _accounts.GetByIdentityAsync(purchaserIdentity, cancellationToken);
            if (account is not null)
            {
                var now = DateTimeOffset.UtcNow;
                var grants = await _grants.GetGrantsAsync(account.Id, cancellationToken);
                foreach (var grant in grants)
                {
                    if (grant.IsActiveAt(now))
                    {
                        unlocked.Add(grant.CapabilityKey);
                    }
                }
            }
        }

        // 4. The system-scope POST-COMPOSE FILTER (control-plane/02, AC-03): AFTER the
        // baseline + grant composition above completes unchanged, force-remove any
        // capability whose owning system flag reads effectively false (a kill switch, or an
        // unconfigured infra floor), immediately before the set is captured into
        // SessionEntitlements. This wins over any account grant added in step 3 - system
        // force-off has precedence - yet it never SKIPS grant evaluation: the composition
        // ran in full, and this step only ever SUBTRACTS from its result (AC-03 / AC-06).
        await _systemFlags.ApplyAsync(unlocked, cancellationToken);

        return new SessionEntitlements(unlocked);
    }
}
