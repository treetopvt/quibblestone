// ----------------------------------------------------------------------------
//  AccountsController - the REST sign-in / restore surface for a returning
//  PURCHASER (accounts-identity/03, issue #69). Two endpoints, both purchaser-
//  facing and both kept entirely OFF the game path:
//
//    POST /api/accounts/signin/request  { email }  -> issue + "deliver" a link
//    POST /api/accounts/signin/verify   { token }  -> resolve the account + sign in
//
//  WHAT THIS BUILDS ON (and never reimplements):
//    - accounts-identity/02's IMagicLinkTokenService issues + verifies the one-
//      time, HMAC-signed magic-link token (ADR 0002 Decision A). We inject the
//      SAME registered service - there is no second token implementation here.
//    - accounts-identity/02's IAccountStore holds the lightweight purchaser
//      account (email + created-at ONLY). We call GetByIdentityAsync, the READ-
//      ONLY lookup that NEVER creates a row - so a sign-in for an email that
//      never purchased misses cleanly (AC-01 no-duplicate, AC-05 no-create).
//
//  THE PURCHASER CREDENTIAL (AC-02) - built on the framework, no new dependency:
//    On a successful verify we mint a SHORT-LIVED, purchaser-scoped credential
//    with ASP.NET Core Data Protection (ITimeLimitedDataProtector) under a
//    dedicated purpose string (PurchaserSessionPurpose). It protects a tiny
//    payload (the purchaser email + issued-at) and expires after
//    CredentialLifetime. This is the "signed in as purchaser X" token that
//    billing-entitlements/05's future restore view consumes to look up what this
//    purchaser owns, with NO device-specific state. It is returned as a bearer
//    value in the response body (the SPA and the API are different origins in
//    dev, which makes a cross-site cookie awkward; a bearer the SPA holds and
//    later presents to the restore endpoint is the simplest seam and is
//    explicitly allowed). It is ALSO mirrored into an HttpOnly cookie for a
//    same-site production deployment. Either way it is hand-rolled-crypto-free.
//
//  AUTH-BOUNDARY INVARIANT (AC-03/AC-04, NON-NEGOTIABLE): this credential is
//  NEVER required by, nor even checked in, GameHub or any player-facing endpoint.
//  Nothing about a room / round / player depends on sign-in state. Free play
//  (single-player or joining a group by code) never touches this controller.
//  The purchaser side lives entirely here + its own web surface; it imports
//  nothing from api/src/Rooms and the hub imports nothing from here.
//
//  NO ACCOUNT ENUMERATION (AC-05): the request endpoint returns the SAME neutral
//  response whether or not an account exists - it does NOT branch on the store
//  (it never even reads it), does NOT create an account, and the response
//  shape/timing is identical for a known and an unknown email. Issue() signs the
//  email without consulting the store, so even the Development-only token echo
//  leaks nothing about account existence. The verify endpoint only ever reaches a
//  "signed in" outcome for a holder of a valid single-use token (i.e. someone who
//  received the emailed link, so controls that inbox); a valid-token-but-no-
//  account holder is guided to purchase (AC-05 "guided, not left ambiguous").
//
//  SECRETS (AC-06): the token signing key comes from config / Key Vault
//  (accounts-identity/02), and this credential's protection key is framework-
//  managed by Data Protection - NEVER a committed literal, NEVER a VITE_* var. The
//  token and the credential are NEVER logged. (The Data Protection key ring today
//  is the framework default; a durable, Key Vault-backed shared key ring is a
//  billing-entitlements deployment follow-up - see the Program.cs registration.)
//
//  DAY ONE (AC-06): with zero accounts anywhere, every verify simply resolves to
//  the friendly "no account - purchase to get started" outcome without erroring;
//  nothing here assumes an account exists.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Controllers;

/// <summary>Request body for POST /api/accounts/signin/request: the email to send a sign-in link to.</summary>
/// <param name="Email">The purchaser's email. May be null/empty - handled as a no-op-shaped neutral response.</param>
public sealed record SignInRequestBody(string? Email);

/// <summary>Request body for POST /api/accounts/signin/verify: the token from a followed magic link.</summary>
/// <param name="Token">The single-use magic-link token. May be null/empty - resolves to the "link invalid" outcome.</param>
public sealed record SignInVerifyBody(string? Token);

/// <summary>
/// Response for the request-a-link endpoint. Deliberately NEUTRAL (AC-05): the
/// SAME shape and message regardless of whether an account exists. <see
/// cref="DevToken"/> / <see cref="DevVerifyPath"/> are populated ONLY in the
/// Development environment (so the flow is walkable locally with no email
/// provider) and are null everywhere else - and even in dev they reveal nothing
/// about account existence, since a token is issued for any email.
/// </summary>
public sealed record SignInRequestResult(string Message, string? DevToken, string? DevVerifyPath);

/// <summary>
/// Response for the verify endpoint. <see cref="Outcome"/> is one of
/// "signed-in", "no-account", or "link-invalid". <see cref="Credential"/> - the
/// short-lived purchaser bearer credential (AC-02) - is present ONLY on the
/// "signed-in" outcome; <see cref="Email"/> (for the "signed in as X" UI) is too.
/// </summary>
public sealed record SignInVerifyResult(string Outcome, string Message, string? Email, string? Credential);

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController : ControllerBase
{
    /// <summary>
    /// The Data Protection purpose string that scopes this credential (AC-02). Now
    /// owned by <see cref="PurchaserCredentialService"/> (the ONE minter+resolver, reused
    /// by billing-entitlements/05's restore endpoint); forwarded here so existing
    /// references keep working and the value cannot drift between the two.
    /// </summary>
    public const string PurchaserSessionPurpose = PurchaserCredentialService.Purpose;

    /// <summary>How long a purchaser sign-in credential stays valid (owned by <see cref="PurchaserCredentialService"/>).</summary>
    public static readonly TimeSpan CredentialLifetime = PurchaserCredentialService.Lifetime;

    /// <summary>The HttpOnly cookie name mirroring the credential (owned by <see cref="PurchaserCredentialService"/>).</summary>
    public const string CredentialCookieName = PurchaserCredentialService.CookieName;

    /// <summary>
    /// Max accepted email length on the OPEN request endpoint (Copilot review). The
    /// RFC 5321 ceiling is 254; anything longer is not a real address and would only
    /// bloat the signed token (and, in dev, the echoed response). An over-length
    /// email returns the SAME neutral shape (no enumeration tell) with no token
    /// issued, failing fast before any HMAC work.
    /// </summary>
    public const int MaxEmailLength = 254;

    /// <summary>
    /// Max accepted magic-link token length on the OPEN verify endpoint (Copilot
    /// review). A legitimate token is well under this (v1 payload over a &lt;=254
    /// char email + signature); the generous cap simply lets an oversized payload
    /// fail fast to the neutral "link-invalid" outcome rather than doing avoidable
    /// HMAC CPU/allocation on attacker-controlled bulk input.
    /// </summary>
    public const int MaxTokenLength = 1024;

    /// <summary>
    /// The web route the emailed magic link lands on for a purchaser (the Account
    /// page, web/src/App.tsx). The link is {LinkBaseUrl}{path}?token=... and the
    /// (future) deep-link handler on that page verifies the token. Kept as a const so
    /// the delivered link and the web route stay in one place.
    /// </summary>
    public const string MagicLinkPath = "/account";

    private readonly IMagicLinkTokenService _tokens;
    private readonly IAccountStore _accounts;
    private readonly PurchaserCredentialService _credential;
    private readonly IEmailSender _email;
    private readonly EmailOptions _emailOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(
        IMagicLinkTokenService tokens,
        IAccountStore accounts,
        PurchaserCredentialService credential,
        IEmailSender email,
        EmailOptions emailOptions,
        IWebHostEnvironment environment,
        ILogger<AccountsController> logger)
    {
        _tokens = tokens;
        _accounts = accounts;
        _credential = credential;
        _email = email;
        _emailOptions = emailOptions;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/accounts/signin/request -> a NEUTRAL "if that email has an
    /// account, a link is on its way" acknowledgement. Issues a fresh single-use
    /// token for the entered email (accounts-identity/02's issuer) and "delivers"
    /// it. There is no email provider wired yet, so in the Development environment
    /// ONLY the token (and a follow path) are echoed back so the flow is walkable
    /// locally; in any other environment the response carries no token.
    ///
    /// AC-05 (no enumeration): this NEVER reads or writes the account store, so
    /// there is no existence branch and no timing tell, and it never creates an
    /// account. The token is issued for any well-formed email regardless.
    /// </summary>
    [HttpPost("signin/request")]
    [EnableRateLimiting(SignInRateLimit.PolicyName)]
    public async Task<IActionResult> RequestLink([FromBody] SignInRequestBody? request)
    {
        // The one neutral acknowledgement, identical for a known and an unknown
        // email (AC-05). It intentionally does not confirm an account exists.
        const string neutralMessage =
            "If that email has a QuibbleStone purchase, a sign-in link is on its way. Check your inbox.";

        var email = (request?.Email ?? string.Empty).Trim();
        if (email.Length == 0 || email.Length > MaxEmailLength)
        {
            // Nothing to sign (empty), OR an over-length input that is not a real
            // address (Copilot review) - either way return the SAME neutral shape
            // rather than an error, so it is indistinguishable from any other
            // submit (no oracle, and a friendly UX) and no oversized token is ever
            // minted. No token is issued on this path.
            return Ok(new SignInRequestResult(neutralMessage, DevToken: null, DevVerifyPath: null));
        }

        // Issue a single-use token bound to the email. Issue() signs the email
        // WITHOUT consulting the account store, so this reveals nothing about
        // whether an account exists (AC-05). The token is never logged (AC-06).
        var token = _tokens.Issue(email);

        // accounts-identity/04: deliver the link through the ONE email seam (AC-02),
        // right after issuing the token. With no provider configured this is the
        // NoOpEmailSender (a no-op, AC-03); with a provider it emails the link. The
        // send happens for ANY well-formed email regardless of account existence
        // (the store is never read here), so it is not an enumeration oracle (AC-04).
        await DeliverMagicLinkAsync(email, token);

        // Development ONLY: echo the token + a follow path so the sign-in flow is
        // exercisable locally with no email provider. In any non-dev environment
        // the token is delivered by email and NEVER returned here.
        if (_environment.IsDevelopment())
        {
            return Ok(new SignInRequestResult(
                neutralMessage,
                DevToken: token,
                DevVerifyPath: "/api/accounts/signin/verify"));
        }

        return Ok(new SignInRequestResult(neutralMessage, DevToken: null, DevVerifyPath: null));
    }

    /// <summary>
    /// POST /api/accounts/signin/verify -> { outcome, message, email?, credential? }.
    /// Verifies the followed magic-link token (recovering the email subject) and
    /// resolves it to an EXISTING account via the READ-ONLY GetByIdentityAsync -
    /// it never creates a row (AC-01 no-duplicate, AC-05 no-create-on-miss).
    ///
    /// On a hit: mints the short-lived purchaser credential (AC-02) and returns
    /// "signed-in". On a valid-token-but-no-account: returns "no-account" guiding
    /// the user to purchase (AC-05). On an invalid/expired/replayed token: returns
    /// "link-invalid". No account is ever created on any path.
    /// </summary>
    [HttpPost("signin/verify")]
    public async Task<IActionResult> Verify([FromBody] SignInVerifyBody? request, CancellationToken cancellationToken)
    {
        var submittedToken = request?.Token ?? string.Empty;

        // Fail fast on an over-length token (Copilot review): a legitimate token is
        // well under MaxTokenLength, so anything larger is junk / attacker bulk
        // input - reject it to the SAME neutral "link-invalid" outcome before doing
        // any HMAC CPU/allocation work. TryVerify would reject it anyway; this just
        // avoids the avoidable work on an open, un-rate-limited endpoint.
        // An invalid, tampered, expired, or already-used token also verifies false
        // (the service never throws). The holder is told the link did not work
        // and to request a fresh one - no account is touched.
        var verification = submittedToken.Length > MaxTokenLength
            ? TokenVerification.Failure
            : await _tokens.TryVerifyAsync(submittedToken, cancellationToken);
        var email = verification.Subject;
        if (!verification.Succeeded || email.Length == 0)
        {
            return Ok(new SignInVerifyResult(
                Outcome: "link-invalid",
                Message: "That sign-in link did not work - it may have expired or already been used. Request a fresh link and try again.",
                Email: null,
                Credential: null));
        }

        // READ ONLY (AC-01/AC-05): resolve the verified email to an EXISTING
        // account. A miss returns null and NEVER creates a row - a valid token for
        // an email that never purchased must not mint an account.
        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            // Valid link, but no purchase behind this email. Guide the holder to
            // purchase rather than leave them in an ambiguous state (AC-05). The
            // holder controls this inbox (they received the link), so this is not
            // an enumeration oracle.
            return Ok(new SignInVerifyResult(
                Outcome: "no-account",
                Message: "We could not find a QuibbleStone purchase for that email yet. Buy the family plan to unlock it - free play never needs an account.",
                Email: null,
                Credential: null));
        }

        // A hit: mint the short-lived, purchaser-scoped credential (AC-02). This
        // is the "signed in as purchaser X" token billing-entitlements/05's
        // restore view will consume, with no device-specific state. Built on the
        // framework's time-limited data protector - no new dependency, no hand-
        // rolled crypto. The protected payload carries only the purchaser email
        // and an issued-at stamp (no PII beyond the one identity, no room/player).
        var credential = ProtectCredential(account.Email);

        // Mirror the credential into an HttpOnly cookie for a same-site production
        // deployment (the API and SPA share an origin behind the front door there).
        // Secure only outside dev (a Secure cookie is dropped over plain-http local
        // dev); SameSite=Lax as this is a top-level, purchaser-only navigation and
        // is NEVER sent to the hub. This cookie is advisory - the bearer value in
        // the body is the primary, cross-origin-friendly credential.
        Response.Cookies.Append(CredentialCookieName, credential, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            MaxAge = CredentialLifetime,
            Path = "/",
        });

        return Ok(new SignInVerifyResult(
            Outcome: "signed-in",
            Message: "You are signed in. Your purchase can now be restored on this device.",
            Email: account.Email,
            Credential: credential));
    }

    /// <summary>
    /// Protects the purchaser-session payload (email + issued-at) with a time-
    /// limited data protector scoped to <see cref="PurchaserSessionPurpose"/>,
    /// expiring after <see cref="CredentialLifetime"/>. The signing/encryption key
    /// is framework-managed by Data Protection - never a committed literal (AC-06).
    /// (Today the key ring is the framework default; a durable, Key Vault-backed
    /// shared key ring is a billing-entitlements deployment follow-up.) Callers
    /// (billing-entitlements/05) unprotect with a protector created for the SAME
    /// purpose to recover the purchaser email.
    /// </summary>
    private string ProtectCredential(string purchaserEmail) => _credential.Protect(purchaserEmail);

    /// <summary>
    /// Builds the clickable magic link and delivers it through the ONE email seam
    /// (accounts-identity/04, AC-02). FAIL-SAFE (AC-08): a provider error is caught,
    /// logged WITHOUT the token / link / email / secret, and swallowed - the caller
    /// still returns the SAME neutral acknowledgement, so a delivery failure never
    /// becomes a 500 and never an existence oracle. The link points at the public web
    /// origin (EmailOptions.LinkBaseUrl); when that is unset it falls back to the
    /// request's own origin (a local dev walkthrough uses the dev-token echo anyway).
    /// </summary>
    private async Task DeliverMagicLinkAsync(string email, string token)
    {
        try
        {
            var link = BuildMagicLink(token);
            // Flow the request-aborted token so a client disconnect or graceful shutdown
            // cancels the outbound send; the catch below still swallows the cancellation
            // into the SAME neutral acknowledgement (AC-08), so behavior is unchanged.
            await _email.SendMagicLinkAsync(email, link, MagicLinkPurpose.PurchaserSignIn, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // AC-08: never surface the failure to the caller. Log the exception only
            // (no token / link / email / secret) and fall through to the neutral 200.
            _logger.LogWarning(ex, "Magic-link email delivery failed for a purchaser sign-in request; returning the neutral acknowledgement.");
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
