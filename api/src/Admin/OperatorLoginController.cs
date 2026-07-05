// ----------------------------------------------------------------------------
//  OperatorLoginController - the REST login surface for the SEPARATE operator back
//  office (sysadmin-console/01, issue #135). Three endpoints, all under /api/admin,
//  kept entirely OFF the game path and the purchaser path:
//
//    POST /api/admin/login/request  { email }  -> issue + "deliver" a magic link
//    POST /api/admin/login/verify   { token }  -> allowlist-gate + establish a session
//    GET  /api/admin/session                    -> [Operator policy] echo the operator
//
//  WHAT THIS REUSES (and never reimplements):
//    - accounts-identity/02's IMagicLinkTokenService issues + verifies the one-time,
//      HMAC-signed token. Its header comment NAMES this feature as the intended
//      SECOND consumer against a SEPARATE allowlist. We inject the SAME registered,
//      identity-neutral service (the operator email is the opaque subject) - there
//      is NO second token implementation, and NO stand-in issuer.
//    - Data Protection for the operator SESSION credential, minted under
//      OperatorSession.OperatorSessionPurpose - a DEDICATED purpose distinct from
//      AccountsController.PurchaserSessionPurpose (AC-03).
//
//  ALLOWLIST AT VERIFY, NOT ISSUE (AC-02, load-bearing): the request endpoint
//  issues a token for ANY well-formed email WITHOUT consulting the allowlist - so
//  possessing a valid link alone never grants operator scope, and the request never
//  reveals who is (or is not) an operator (no allowlist enumeration). The allowlist
//  is consulted at VERIFY, where a non-operator gets a clear "not authorized"
//  outcome and NO credential.
//
//  COLLECTS ONLY THE EMAIL (AC-07): the login flow takes nothing about the operator
//  beyond the email used to issue / verify the link - no name, no player / session
//  cross-reference. The minted credential carries only that email + an issued-at.
//
//  CHILD-SAFETY / ANONYMITY FIREWALL (non-negotiable): nothing here joins an
//  operator identity to any player nickname, room, or session. This controller
//  imports nothing from Rooms / Hubs and is never touched by the game path.
//
//  SECRETS (AC-05): the token signing key comes from config / Key Vault
//  (accounts-identity/02), the credential's key ring is framework-managed by Data
//  Protection, and the allowlist is operator-only config - NEVER a committed
//  literal, NEVER a VITE_* var. The token and the credential are NEVER logged.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Admin;

/// <summary>Request body for POST /api/admin/login/request: the operator email to send a link to.</summary>
/// <param name="Email">The operator's email. May be null/empty - handled as a neutral no-op-shaped response.</param>
public sealed record OperatorLoginRequestBody(string? Email);

/// <summary>Request body for POST /api/admin/login/verify: the token from a followed operator magic link.</summary>
/// <param name="Token">The single-use magic-link token. May be null/empty - resolves to the "link-invalid" outcome.</param>
public sealed record OperatorLoginVerifyBody(string? Token);

/// <summary>
/// Response for the request-a-link endpoint. Deliberately NEUTRAL (AC-02): the SAME
/// shape and message whether or not the email is an operator (the endpoint never
/// consults the allowlist). <see cref="DevToken"/> / <see cref="DevVerifyPath"/> are
/// populated ONLY in the Development environment (so the flow is walkable locally
/// with no email provider) and are null everywhere else.
/// </summary>
public sealed record OperatorLoginRequestResult(string Message, string? DevToken, string? DevVerifyPath);

/// <summary>
/// Response for the verify endpoint. <see cref="Outcome"/> is one of "signed-in",
/// "not-authorized", or "link-invalid". <see cref="Credential"/> - the short-lived
/// operator bearer (AC-01) - and <see cref="Email"/> are present ONLY on
/// "signed-in".
/// </summary>
public sealed record OperatorLoginVerifyResult(string Outcome, string Message, string? Email, string? Credential);

/// <summary>Response for the authenticated session echo: the signed-in operator email (AC-07 - nothing else).</summary>
public sealed record OperatorSessionResult(string Email);

[ApiController]
[Route("api/admin")]
public sealed class OperatorLoginController : ControllerBase
{
    /// <summary>
    /// Max accepted email length on the OPEN request endpoint (mirrors
    /// AccountsController.MaxEmailLength). The RFC 5321 ceiling is 254; anything
    /// longer is not a real address and would only bloat the signed token. An
    /// over-length email returns the SAME neutral shape with no token issued.
    /// </summary>
    public const int MaxEmailLength = 254;

    /// <summary>
    /// Max accepted magic-link token length on the OPEN verify endpoint (mirrors
    /// AccountsController.MaxTokenLength). A legitimate token is well under this; the
    /// cap lets an oversized payload fail fast to "link-invalid" before HMAC work.
    /// </summary>
    public const int MaxTokenLength = 1024;

    /// <summary>
    /// The web route the emailed magic link lands on for an operator (the admin
    /// login surface). The link is {LinkBaseUrl}{path}?token=... and the (future)
    /// deep-link handler on that page verifies the token. Kept as a const so the
    /// delivered link and the web route stay in one place.
    /// </summary>
    public const string MagicLinkPath = "/admin/login";

    private readonly IMagicLinkTokenService _tokens;
    private readonly IOperatorAllowlist _allowlist;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IEmailSender _email;
    private readonly EmailOptions _emailOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<OperatorLoginController> _logger;

    public OperatorLoginController(
        IMagicLinkTokenService tokens,
        IOperatorAllowlist allowlist,
        IDataProtectionProvider dataProtection,
        IEmailSender email,
        EmailOptions emailOptions,
        IWebHostEnvironment environment,
        ILogger<OperatorLoginController> logger)
    {
        _tokens = tokens;
        _allowlist = allowlist;
        _dataProtection = dataProtection;
        _email = email;
        _emailOptions = emailOptions;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/admin/login/request -> a NEUTRAL "if that email is an operator, a
    /// link is on its way" acknowledgement. Issues a fresh single-use token for the
    /// entered email (accounts-identity/02's issuer) and "delivers" it. There is no
    /// email provider wired yet, so in the Development environment ONLY the token
    /// (and a follow path) are echoed back so the flow is walkable locally.
    ///
    /// AC-02 (allowlist at verify, not issue): this NEVER consults the allowlist, so
    /// there is no existence branch and no timing tell - a token is issued for any
    /// well-formed email, and possessing the resulting link alone never grants
    /// operator scope. Rate-limited per IP (OperatorLoginRateLimit).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login/request")]
    [EnableRateLimiting(OperatorLoginRateLimit.PolicyName)]
    public async Task<IActionResult> RequestLink([FromBody] OperatorLoginRequestBody? request)
    {
        // The one neutral acknowledgement, identical for an operator and a
        // non-operator email (AC-02). It intentionally does not confirm operator
        // status - that is only ever resolved at verify.
        const string neutralMessage =
            "If that email is an operator, a sign-in link is on its way. Check your inbox.";

        var email = (request?.Email ?? string.Empty).Trim();
        if (email.Length == 0 || email.Length > MaxEmailLength)
        {
            // Empty or over-length (not a real address): return the SAME neutral
            // shape rather than an error, so it is indistinguishable from any other
            // submit (no oracle) and no oversized token is minted.
            return Ok(new OperatorLoginRequestResult(neutralMessage, DevToken: null, DevVerifyPath: null));
        }

        // Issue a single-use token bound to the email. Issue() signs the email
        // WITHOUT consulting the allowlist, so this reveals nothing about operator
        // status (AC-02). The token is never logged (AC-05).
        var token = _tokens.Issue(email);

        // accounts-identity/04: deliver the link through the SAME one email seam the
        // purchaser flow uses (AC-02), right after issuing the token. The send happens
        // WITHOUT consulting the allowlist (issue is allowlist-blind), so it reveals
        // nothing about operator status and never becomes an oracle (AC-04). With no
        // provider configured this is the NoOpEmailSender (AC-03).
        await DeliverMagicLinkAsync(email, token);

        // Development ONLY: echo the token + a follow path so the flow is walkable
        // locally with no email provider. In any non-dev environment the token is
        // delivered by email and NEVER returned here.
        if (_environment.IsDevelopment())
        {
            return Ok(new OperatorLoginRequestResult(
                neutralMessage,
                DevToken: token,
                DevVerifyPath: "/api/admin/login/verify"));
        }

        return Ok(new OperatorLoginRequestResult(neutralMessage, DevToken: null, DevVerifyPath: null));
    }

    /// <summary>
    /// POST /api/admin/login/verify -> { outcome, message, email?, credential? }.
    /// Verifies the followed magic-link token (recovering the email subject) and
    /// THEN checks the operator allowlist (AC-02). On an allowlisted email: mints the
    /// short-lived operator credential (AC-01) and returns "signed-in". On a valid
    /// token whose email is NOT an operator: returns "not-authorized" with NO
    /// credential (AC-02 - a valid link alone never grants scope). On an
    /// invalid/expired/replayed token: returns "link-invalid".
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login/verify")]
    public IActionResult Verify([FromBody] OperatorLoginVerifyBody? request)
    {
        var submittedToken = request?.Token ?? string.Empty;

        // Fail fast on an over-length token, then verify. An invalid, tampered,
        // expired, or already-used token verifies false (the service never throws)
        // and resolves to the neutral "link-invalid" outcome.
        if (submittedToken.Length > MaxTokenLength
            || !_tokens.TryVerify(submittedToken, out var email)
            || email.Length == 0)
        {
            return Ok(new OperatorLoginVerifyResult(
                Outcome: "link-invalid",
                Message: "That sign-in link did not work - it may have expired or already been used. Request a fresh link and try again.",
                Email: null,
                Credential: null));
        }

        // THE GATE (AC-02/AC-03): a verified inbox is only an operator if the email
        // is on the allowlist. A non-operator holding a perfectly valid link is
        // rejected here with no credential - proof-of-control is not authorization.
        if (!_allowlist.IsOperator(email))
        {
            return Ok(new OperatorLoginVerifyResult(
                Outcome: "not-authorized",
                Message: "That email is not authorized for the operator console.",
                Email: null,
                Credential: null));
        }

        // An allowlisted operator: mint the short-lived operator credential (AC-01)
        // under the DEDICATED operator purpose (AC-03). It carries only the operator
        // email + an issued-at (AC-07) - no player / room / session / purchaser data.
        var normalized = email.Trim().ToLowerInvariant();
        var credential = OperatorSession.Protect(_dataProtection, normalized);

        // Mirror the credential into an HttpOnly cookie for a same-site deployment.
        // Secure only outside dev (a Secure cookie is dropped over plain-http local
        // dev); SameSite=Strict as the back office is a standalone surface never
        // navigated to cross-site, and this cookie is NEVER sent to the game hub.
        Response.Cookies.Append(OperatorSession.CookieName, credential, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            MaxAge = OperatorSession.CredentialLifetime,
            Path = "/",
        });

        return Ok(new OperatorLoginVerifyResult(
            Outcome: "signed-in",
            Message: "You are signed in to the operator console.",
            Email: normalized,
            Credential: credential));
    }

    /// <summary>
    /// GET /api/admin/session -> { email }. The minimal authenticated echo the admin
    /// SPA calls to confirm an operator session exists (and the FOUNDATION admin
    /// endpoint the boundary tests exercise). Gated by the "Operator" policy, NEVER
    /// bare [Authorize] - only an allowlisted operator credential (never a purchaser
    /// one, AC-03) reaches here; everyone else gets 401. Returns nothing about the
    /// operator beyond the email on their own credential (AC-07). No admin capability
    /// lives here (grant / revoke / takedown are stories 02/03).
    /// </summary>
    [Authorize(Policy = OperatorSession.PolicyName)]
    [HttpGet("session")]
    public IActionResult Session()
    {
        // The name claim is the operator email the handler recovered from the
        // credential + re-validated against the allowlist. Nothing else is exposed.
        var email = User.Identity?.Name ?? string.Empty;
        return Ok(new OperatorSessionResult(email));
    }

    /// <summary>
    /// Builds the clickable operator magic link and delivers it through the ONE email
    /// seam (accounts-identity/04, AC-02 - the SAME seam the purchaser flow uses, only
    /// the copy / link differ). FAIL-SAFE (AC-08): a provider error is caught, logged
    /// WITHOUT the token / link / email / secret, and swallowed - the caller still
    /// returns the SAME neutral acknowledgement, so a delivery failure never becomes a
    /// 500 and never an existence oracle. The link points at the public web origin
    /// (EmailOptions.LinkBaseUrl), falling back to the request's own origin when unset.
    /// </summary>
    private async Task DeliverMagicLinkAsync(string email, string token)
    {
        try
        {
            var link = BuildMagicLink(token);
            await _email.SendMagicLinkAsync(email, link, MagicLinkPurpose.OperatorLogin);
        }
        catch (Exception ex)
        {
            // AC-08: never surface the failure to the caller. Log the exception only
            // (no token / link / email / secret) and fall through to the neutral 200.
            _logger.LogWarning(ex, "Magic-link email delivery failed for an operator login request; returning the neutral acknowledgement.");
        }
    }

    /// <summary>Builds {LinkBaseUrl-or-request-origin}{MagicLinkPath}?token=... (the token is URL-escaped).</summary>
    private string BuildMagicLink(string token)
    {
        var linkBase = (_emailOptions.LinkBaseUrl ?? string.Empty).Trim();
        if (linkBase.Length == 0)
        {
            linkBase = $"{Request.Scheme}://{Request.Host}";
        }

        return $"{linkBase.TrimEnd('/')}{MagicLinkPath}?token={Uri.EscapeDataString(token)}";
    }
}
