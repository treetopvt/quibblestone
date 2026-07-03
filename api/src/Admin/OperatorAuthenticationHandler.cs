// ----------------------------------------------------------------------------
//  OperatorAuthenticationHandler - the authentication handler backing the
//  "Operator" scheme + policy that gates every admin endpoint (sysadmin-console/01,
//  issue #135).
//
//  WHAT IT PROVES (AC-03, load-bearing): a request only authenticates as an
//  operator when it presents a credential that BOTH (a) unprotects under the
//  DEDICATED operator Data Protection purpose (OperatorSession.OperatorSessionPurpose,
//  distinct from the purchaser purpose) AND (b) carries an email that is STILL on
//  the operator allowlist at request time. A purchaser credential fails step (a) by
//  construction (wrong purpose -> unprotect fails) and never reaches step (b) - so
//  "signed in as some purchaser account" can NEVER satisfy the operator check. The
//  allowlist is re-consulted on EVERY request (not just at login), so removing an
//  operator from config revokes their live sessions on the next call (AC-05).
//
//  WHERE THE CREDENTIAL COMES FROM: the Authorization: Bearer header (the primary,
//  cross-origin-friendly path the admin SPA uses) or, failing that, the HttpOnly
//  operator cookie (a same-site deployment). Either way it is the opaque value the
//  login/verify endpoint minted via OperatorSession.Protect.
//
//  RESULTS: NoResult when there is no credential at all (let the challenge return
//  the login screen / 401), Fail when a credential is present but invalid / expired
//  / wrong-purpose / not-an-operator, Success with a minimal ClaimsPrincipal (only
//  the operator email as the name claim - AC-07, no other identity) otherwise. The
//  credential is NEVER logged (AC-05).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// The authentication handler for the "Operator" scheme (sysadmin-console/01).
/// Reads the operator credential (bearer header or cookie), unprotects it under the
/// DEDICATED operator purpose, and authenticates ONLY when the recovered email is
/// still an allowlisted operator (AC-02/AC-03/AC-05). Emits a minimal principal
/// carrying just the operator email (AC-07).
/// </summary>
public sealed class OperatorAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IOperatorAllowlist _allowlist;

    public OperatorAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDataProtectionProvider dataProtection,
        IOperatorAllowlist allowlist)
        : base(options, logger, encoder)
    {
        _dataProtection = dataProtection;
        _allowlist = allowlist;
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var credential = ExtractCredential();
        if (string.IsNullOrEmpty(credential))
        {
            // No credential at all: not a failure, just "no operator here" - the
            // policy's challenge (401) drives an unauthenticated visitor to the
            // login screen (AC-06). Nothing admin is rendered or fetched.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Step (a): unprotect under the DEDICATED operator purpose. A purchaser
        // credential (wrong purpose), a tampered value, or an expired one fails HERE
        // and never reaches the allowlist (AC-03).
        if (!OperatorSession.TryUnprotect(_dataProtection, credential, out var email))
        {
            return Task.FromResult(AuthenticateResult.Fail("The operator credential is invalid or expired."));
        }

        // Step (b): the email must STILL be an allowlisted operator. Re-checked every
        // request so a config removal revokes access immediately (AC-05). A valid
        // credential for a de-listed email is rejected.
        if (!_allowlist.IsOperator(email))
        {
            return Task.FromResult(AuthenticateResult.Fail("Not an operator."));
        }

        // A minimal principal: only the operator email as the name claim (AC-07). No
        // player / room / session / purchaser data is attached anywhere.
        var claims = new[] { new Claim(ClaimTypes.Name, email) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Pulls the operator credential from the Authorization: Bearer header (primary,
    /// cross-origin-friendly) or, failing that, the HttpOnly operator cookie
    /// (same-site). Returns null when neither is present.
    /// </summary>
    private string? ExtractCredential()
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

        if (Request.Cookies.TryGetValue(OperatorSession.CookieName, out var cookie) && !string.IsNullOrEmpty(cookie))
        {
            return cookie;
        }

        return null;
    }
}
