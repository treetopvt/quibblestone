// ----------------------------------------------------------------------------
//  AccountsController - the REST sign-in / restore surface for a FAMILY account
//  (accounts-identity/03, issue #69; widened by accounts-identity/07, issue #211).
//  Two endpoints, both adult-facing and both kept entirely OFF the game path:
//
//    POST /api/accounts/signin/request  { email, intent? }  -> issue + "deliver" a link
//    POST /api/accounts/signin/verify   { token, intent? }  -> resolve/create + sign in
//
//  THE TWO INTENTS (accounts-identity/07, ADR 0003 Amendment 1): an account
//  decouples from purchase - it is "an adult who wants things to persist," and a
//  purchaser is an account that ALSO holds paid grants. The optional `intent` on
//  both endpoints selects which entry point the SAME plumbing serves:
//    - "signin" (the DEFAULT): a returning PURCHASER restores an EXISTING account.
//      A valid token for an email with no account resolves to "no-account" - it
//      NEVER creates (the story-03 no-create-on-miss behavior is unchanged). Only
//      the user-facing guidance copy is reworded to also point at the free family
//      account (per AC-04's reframing) - the branch itself still creates nothing.
//    - "signup" (accounts-identity/07): a FREE family account. A valid token for
//      an email with no account CREATES one via IAccountStore.CreateOrGetAsync (the
//      SAME idempotent create-or-get story 02 already built) holding email +
//      created-at and ZERO entitlement grants - reachable WITHOUT a purchase. An
//      email that ALREADY has an account (purchaser or free) resolves to that SAME
//      account (create-or-get idempotency), never a duplicate.
//  Both intents ride the EXACT SAME IMagicLinkTokenService and the SAME neutral,
//  no-enumeration request contract (AC-02); intent only ever (a) selects the email
//  copy and (b) changes the VERIFY-time miss branch (create vs guide) - never the
//  request path's shape or timing.
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

/// <summary>Request body for POST /api/accounts/signin/request: the email to send a link to.</summary>
/// <param name="Email">The account email. May be null/empty - handled as a no-op-shaped neutral response.</param>
/// <param name="Intent">Optional entry-point selector: "signup" for a free family account (accounts-identity/07), anything else (or null) for the default purchaser sign-in. Selects the email copy ONLY on the request path - never the response shape/timing (AC-02).</param>
public sealed record SignInRequestBody(string? Email, string? Intent = null);

/// <summary>Request body for POST /api/accounts/signin/verify: the token from a followed magic link.</summary>
/// <param name="Token">The single-use magic-link token. May be null/empty - resolves to the "link invalid" outcome.</param>
/// <param name="Intent">Optional entry-point selector: "signup" makes a valid-token-but-no-account outcome CREATE a free family account (accounts-identity/07, AC-01); anything else (or null) keeps the story-03 sign-in behavior (no create on miss). Carried across the emailed link via its query string.</param>
public sealed record SignInVerifyBody(string? Token, string? Intent = null);

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

    /// <summary>
    /// The `intent` value that selects the FREE FAMILY ACCOUNT path (accounts-identity/07):
    /// on the request endpoint it picks the "create your account" email copy; on verify it
    /// makes a no-account outcome CREATE (via CreateOrGetAsync) instead of guiding to
    /// purchase. Any other value (or null) is the default story-03 purchaser sign-in.
    /// Compared case-insensitively after a trim. Also the query-string key/value the
    /// emailed link carries so the intent survives the email round-trip.
    /// </summary>
    public const string SignUpIntent = "signup";

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
        // accounts-identity/07: intent is a CLIENT-supplied entry-point selector, not
        // derived from the store - so keying copy on it reveals nothing about account
        // existence (AC-02). The response SHAPE and TIMING are identical for both
        // intents and for a known vs unknown email; only the copy string differs.
        var signUp = IsSignUp(request?.Intent);

        // The one neutral acknowledgement, identical for a known and an unknown
        // email (AC-02/AC-05). It intentionally does not confirm an account exists.
        var neutralMessage = signUp
            ? "We're sending a link to create or open your QuibbleStone family account. Check your inbox."
            : "If that email has a QuibbleStone purchase, a sign-in link is on its way. Check your inbox.";

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
        // The purpose selects ONLY the copy (accounts-identity/07): a family sign-up
        // reads "create your account", a sign-in reads "restore your purchase".
        await DeliverMagicLinkAsync(email, token, signUp);

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
    /// resolves it to an account.
    ///
    /// On a hit (an account already exists for the email): mints the short-lived
    /// credential (AC-02) and returns "signed-in".
    ///
    /// On a valid-token-but-no-account, the behavior forks on `intent`:
    ///   - DEFAULT / "signin" (story 03 behavior): returns "no-account" and NEVER
    ///     creates a row (AC-05 no-create-on-miss). Its guidance copy is reworded to
    ///     also mention the free family account, but the branch creates nothing.
    ///   - "signup" (accounts-identity/07, AC-01): CREATES a free family account via
    ///     IAccountStore.CreateOrGetAsync (the SAME idempotent create-or-get) holding
    ///     email + created-at and ZERO grants, then signs in exactly like a hit. The
    ///     holder proved control of the inbox by following the link, so creating their
    ///     own zero-grant account here is safe and is not an enumeration oracle.
    ///
    /// On an invalid/expired/replayed token: returns "link-invalid" and creates nothing.
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

        var signUp = IsSignUp(request?.Intent);

        // Resolve the verified email to an EXISTING account (READ ONLY - never
        // creates). A hit signs in on either intent.
        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        var created = false;
        if (account is null)
        {
            if (!signUp)
            {
                // DEFAULT / sign-in path (story 03, AC-05 no-create-on-miss): a valid
                // link but no purchase behind this email. Guide the holder rather than
                // leave them ambiguous. The holder controls this inbox (they received
                // the link), so this is not an enumeration oracle.
                return Ok(new SignInVerifyResult(
                    Outcome: "no-account",
                    Message: "We could not find a QuibbleStone purchase for that email yet. Create a free family account or buy the family plan - free play never needs an account.",
                    Email: null,
                    Credential: null));
            }

            // accounts-identity/07 SIGN-UP path (AC-01): create the free family account
            // via the SAME idempotent create-or-get story 02 already built - never a
            // second creation path. It holds email + created-at and ZERO entitlement
            // grants (AC-05): this call grants NOTHING and does not touch the grant
            // store. If a concurrent request already created it, CreateOrGetAsync
            // returns that SAME account (AC-03 no-duplicate).
            account = await _accounts.CreateOrGetAsync(email, cancellationToken);
            created = true;
        }

        // A hit (or a freshly-created free account): mint the short-lived credential (AC-02). This
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

        // Pick the confirmation copy by INTENT, not merely by whether a row was just
        // created (Copilot review): a family sign-up that lands on an ALREADY-existing
        // free account (created == false) must still read as a family account, never
        // the purchaser-only "restore your purchase" wording. Only the default sign-in
        // intent (a returning purchaser) keeps the restore copy.
        var signedInMessage =
            created
                ? "Your free family account is ready. Your keepsakes and any purchases can follow you across your devices."
            : signUp
                ? "You're signed in to your family account. Your keepsakes and any purchases can follow you across your devices."
                : "You are signed in. Your purchase can now be restored on this device.";

        return Ok(new SignInVerifyResult(
            Outcome: "signed-in",
            Message: signedInMessage,
            Email: account.Email,
            Credential: credential));
    }

    /// <summary>
    /// True when <paramref name="intent"/> selects the free family-account sign-up
    /// path (accounts-identity/07) - a case-insensitive, trimmed match against
    /// <see cref="SignUpIntent"/>. Any other value (or null) is the default
    /// purchaser sign-in. A shared helper so the request and verify paths agree.
    /// </summary>
    private static bool IsSignUp(string? intent) =>
        string.Equals(intent?.Trim(), SignUpIntent, StringComparison.OrdinalIgnoreCase);

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
    private async Task DeliverMagicLinkAsync(string email, string token, bool signUp)
    {
        try
        {
            // accounts-identity/07: the followed link must carry the intent so a family
            // sign-up still creates on verify AFTER the email round-trip (the token itself
            // is intent-agnostic - it signs only the email, never widened). The purpose
            // selects the "create your account" vs "restore your purchase" copy.
            var link = BuildMagicLink(token, signUp);
            var purpose = signUp ? MagicLinkPurpose.FamilySignUp : MagicLinkPurpose.PurchaserSignIn;
            // Flow the request-aborted token so a client disconnect or graceful shutdown
            // cancels the outbound send; the catch below still swallows the cancellation
            // into the SAME neutral acknowledgement (AC-08), so behavior is unchanged.
            await _email.SendMagicLinkAsync(email, link, purpose, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // AC-08: never surface the failure to the caller. Log the exception only
            // (no token / link / email / secret) and fall through to the neutral 200.
            _logger.LogWarning(ex, "Magic-link email delivery failed for a purchaser sign-in request; returning the neutral acknowledgement.");
        }
    }

    /// <summary>
    /// Builds {LinkBaseUrl-or-request-origin}{MagicLinkPath}?token=... (the token is
    /// URL-escaped). On the family sign-up path (accounts-identity/07) it also appends
    /// &amp;intent=signup so the followed link keeps its intent across the email round-trip;
    /// the web reads that query value and passes it back to verify.
    /// </summary>
    private string BuildMagicLink(string token, bool signUp)
    {
        var linkBase = (_emailOptions.LinkBaseUrl ?? string.Empty).Trim();
        if (linkBase.Length == 0)
        {
            linkBase = $"{Request.Scheme}://{Request.Host}";
        }

        var intentSuffix = signUp ? $"&intent={SignUpIntent}" : string.Empty;
        return $"{linkBase.TrimEnd('/')}{MagicLinkPath}?token={Uri.EscapeDataString(token)}{intentSuffix}";
    }
}
