// ----------------------------------------------------------------------------
//  EntitlementsController - the READ-ONLY restore/manage surface (billing-
//  entitlements/05, issue #74). A signed-in purchaser sees a plain-language list of
//  what they own, sourced from billing-entitlements/01's grant store for their
//  account. This is the last link that makes a purchase durable across a purchaser's
//  devices (paired with accounts-identity/03's sign-in).
//
//  READ ONLY (story 05 scope): there is NO write path here - no grant, no plan change,
//  no cancel. It only reads grants billing-01 defines and stories 03-04 write.
//
//  SIGNED-IN GUARD, REUSED (AC-06): the caller proves who they are with the SAME
//  purchaser credential accounts-identity/03 mints on sign-in - resolved here via the
//  shared PurchaserCredentialService (the reused guard, NOT a second auth check). The
//  credential arrives as an Authorization: Bearer value (the cross-origin-friendly path
//  the SPA holds from sign-in) or, for a same-site deployment, the HttpOnly cookie. No
//  valid credential -> 401, and NO entitlement state is shown for an unauthenticated
//  visitor (the client directs them to sign in first).
//
//  NO PII / NO PLAY HISTORY (AC-05): the payload is capability key + friendly label +
//  source + lease end ONLY. It never contains which players / nicknames used those
//  entitlements - it answers "what did the PURCHASER buy", never "who played".
//
//  DAY ONE / EMPTY (AC-03): a signed-in purchaser with zero active grants gets an empty
//  list (a friendly empty state in the UI), never an error.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Controllers;

/// <summary>One owned entitlement for the restore view: no player / session reference (AC-05).</summary>
/// <param name="Key">The capability key (e.g. "library.full", "pack.spooky").</param>
/// <param name="Label">A friendly display name (e.g. "Full Library", "Spooky Pack").</param>
/// <param name="Source">How it was obtained ("Subscription", "OneTime", "Operator").</param>
/// <param name="ValidThrough">The lease end (null = permanent); lets the UI show "active until ...".</param>
public sealed record EntitlementView(string Key, string Label, string Source, DateTimeOffset? ValidThrough);

/// <summary>Response for GET /api/account/entitlements: the signed-in purchaser's active entitlements.</summary>
public sealed record EntitlementsResult(IReadOnlyList<EntitlementView> Entitlements);

[ApiController]
[Route("api/account")]
public sealed class EntitlementsController : ControllerBase
{
    private readonly PurchaserCredentialService _credential;
    private readonly IEntitlementGrantStore _grants;
    private readonly IAccountStore _accounts;

    public EntitlementsController(
        PurchaserCredentialService credential,
        IEntitlementGrantStore grants,
        IAccountStore accounts)
    {
        _credential = credential;
        _grants = grants;
        _accounts = accounts;
    }

    /// <summary>
    /// GET /api/account/entitlements - the signed-in purchaser's active entitlements.
    /// 401 when not signed in (no valid credential), so no state leaks to an
    /// unauthenticated visitor (AC-06). A signed-in purchaser with nothing gets an empty
    /// list (AC-03).
    /// </summary>
    [HttpGet("entitlements")]
    public async Task<IActionResult> Entitlements(CancellationToken cancellationToken)
    {
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        if (email is null)
        {
            // Not signed in / expired / tampered - show nothing, direct to sign in (AC-06).
            return Unauthorized();
        }

        // Resolve the canonical account, then read grants keyed off account.Id (the
        // SAME stable id billing-01's session-creation gate reads, accounts-identity/05).
        // No account or no active grant -> a friendly empty list (AC-03), never an error.
        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            return Ok(new EntitlementsResult([]));
        }

        var now = DateTimeOffset.UtcNow;
        var entitlements = (await _grants.GetGrantsAsync(account.Id, cancellationToken))
            .Where(grant => grant.IsActiveAt(now))
            .Select(grant => new EntitlementView(
                grant.CapabilityKey,
                EntitlementLabels.LabelFor(grant.CapabilityKey),
                grant.Source.ToString(),
                grant.ValidThrough))
            .ToList();

        return Ok(new EntitlementsResult(entitlements));
    }

    // The credential: prefer the Authorization: Bearer value (the cross-origin path the
    // SPA holds from sign-in), fall back to the HttpOnly cookie (same-site deployment).
    private string? ReadCredential()
    {
        var authorization = Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = authorization[bearerPrefix.Length..].Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }
        return Request.Cookies.TryGetValue(PurchaserCredentialService.CookieName, out var cookie) ? cookie : null;
    }
}
