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
using QuibbleStone.Api.Safety;

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

/// <summary>
/// Request body for creating / updating a kid seat preset (accounts-identity/08).
/// Both fields are client-supplied and NEVER trusted: the nickname is trimmed,
/// length-capped, and safety-filtered server-side before storage (AC-04/AC-07), and
/// the variant is normalized to one of the six known Guardian values.
/// </summary>
/// <param name="Nickname">The preset's display name (free text; same cap + safety filter as any display name).</param>
/// <param name="Variant">The chosen Guardian variant; normalized server-side (null/empty/unknown -> "teal").</param>
public sealed record SeatPresetBody(string? Nickname, string? Variant);

/// <summary>
/// One seat preset as returned to the owning family (accounts-identity/08). A pure
/// { id, nickname, variant } tuple - no history, gallery, entitlement, or PII (AC-05).
/// </summary>
/// <param name="Id">The stable preset id (used by the manager UI to edit / delete).</param>
/// <param name="Nickname">The stored (already vetted) nickname - doubles as the preset's label.</param>
/// <param name="Variant">The stored Guardian variant.</param>
public sealed record SeatPresetView(string Id, string Nickname, string Variant);

/// <summary>Response for GET /api/accounts/presets: the signed-in family's saved presets.</summary>
public sealed record SeatPresetsResult(IReadOnlyList<SeatPresetView> Presets);

/// <summary>A friendly, kid-readable validation message when a preset nickname is rejected (AC-04/AC-07).</summary>
public sealed record SeatPresetError(string Message);

/// <summary>
/// Response for POST /api/accounts/devices/link (accounts-identity/09, AC-01): the
/// short, human-enterable link code the parent hands to the kid's device, and when it
/// expires. Displayed on the Account page for the family to type into the device's
/// redeem screen within the short window.
/// </summary>
/// <param name="Code">The CSPRNG-minted link code (AC-01).</param>
/// <param name="ExpiresUtc">When the code stops being redeemable (minutes out).</param>
public sealed record LinkDeviceResult(string Code, DateTimeOffset ExpiresUtc);

/// <summary>Request body for POST /api/accounts/devices/redeem: the link code typed on the kid's device.</summary>
/// <param name="Code">The link code from the parent's Account page. May be null/empty - resolves to the neutral "did not work" outcome.</param>
public sealed record RedeemDeviceBody(string? Code);

/// <summary>
/// Response for POST /api/accounts/devices/redeem (accounts-identity/09, AC-02). On
/// success the RAW family-device token to persist on the device ONCE (only its hash is
/// kept server-side, AC-05) plus the device's non-identifying label; on failure neither,
/// with a friendly message. A freshly redeemed device is family-safe by default (AC-07) -
/// nothing in this response unlocks teen-plus.
/// </summary>
/// <param name="Ok">True when the code redeemed and a device token was minted.</param>
/// <param name="Message">A friendly, non-enumerating message for either outcome.</param>
/// <param name="Token">The raw device token to persist (success only), returned exactly once.</param>
/// <param name="Label">The device's short, random label (success only, AC-04).</param>
public sealed record RedeemDeviceResult(bool Ok, string Message, string? Token, string? Label);

/// <summary>Request body for POST /api/accounts/devices/refresh: the current device token to rotate.</summary>
/// <param name="Token">The device's current raw token. May be null/empty - resolves to a neutral failure.</param>
public sealed record RefreshDeviceBody(string? Token);

/// <summary>
/// Response for POST /api/accounts/devices/refresh (accounts-identity/09, security
/// posture): on success the REPLACEMENT raw token to persist (the old value is
/// invalidated server-side); on failure none, so the client keeps using / re-links.
/// </summary>
/// <param name="Ok">True when the token was rotated.</param>
/// <param name="Token">The new raw token to persist (success only).</param>
public sealed record RefreshDeviceResult(bool Ok, string? Token);

/// <summary>Request body for POST /api/accounts/devices/{deviceTokenId}/adult-confirm: the new toggle position.</summary>
/// <param name="Confirmed">True to opt this device into the teen-plus tier (AC-07), false to return it to family-safe.</param>
public sealed record AdultConfirmBody(bool Confirmed);

/// <summary>
/// Response for GET /api/accounts/adult-signal (accounts-identity/10, AC-05): EXACTLY
/// one boolean field and nothing else - no account id, email, device-token id, or
/// capability list. The value is resolved ENTIRELY server-side from whatever credential
/// the request presents (header or cookie); there is no request field a client can set
/// to assert it - the boolean is resolved, never accepted as input.
/// </summary>
/// <param name="AdultUnlocked">True only on a positive, freshly resolved adult signal (a purchaser credential or an adult-confirmed device); false otherwise, including every failure (AC-04 fail-safe).</param>
public sealed record AdultSignalResult(bool AdultUnlocked);

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
    private readonly FamilyDeviceLinkService _deviceLinks;
    private readonly IFamilyDeviceTokenStore _deviceTokens;
    private readonly IAdultSignalResolver _adultSignal;
    private readonly FamilyDeviceRedeemGlobalThrottle _globalThrottle;
    private readonly ILogger<AccountsController> _logger;
    // accounts-identity/08: the kid-seat-preset store (account-plane, keyed by the
    // resolved family AccountId) and the SAME server-side safety filter every free-text
    // surface uses - a preset nickname is vetted through it before being stored (AC-04).
    private readonly ISeatPresetStore _presets;
    private readonly IContentSafetyFilter _safety;

    public AccountsController(
        IMagicLinkTokenService tokens,
        IAccountStore accounts,
        PurchaserCredentialService credential,
        IEmailSender email,
        EmailOptions emailOptions,
        IWebHostEnvironment environment,
        FamilyDeviceLinkService deviceLinks,
        IFamilyDeviceTokenStore deviceTokens,
        IAdultSignalResolver adultSignal,
        FamilyDeviceRedeemGlobalThrottle globalThrottle,
        ILogger<AccountsController> logger,
        ISeatPresetStore presets,
        IContentSafetyFilter safety)
    {
        _tokens = tokens;
        _accounts = accounts;
        _credential = credential;
        _email = email;
        _emailOptions = emailOptions;
        _environment = environment;
        // accounts-identity/09 (#229): the family-device-link crypto/orchestration service
        // (mint link code, redeem, refresh) and the linked-device store (list, revoke, the
        // adult-confirm toggle - all plain row updates behind the account holder's own auth).
        _deviceLinks = deviceLinks;
        _deviceTokens = deviceTokens;
        // accounts-identity/10 (#247): the SHARED adult-signal resolver (AC-06). The SAME
        // service GameHub.OnConnectedAsync uses - solo play's GET /api/accounts/adult-signal
        // routes through it rather than forking a second copy of the decision.
        _adultSignal = adultSignal;
        // accounts-identity/09: the GLOBAL redeem/refresh ceiling (per-IP is defeated by IP
        // rotation, ADR 0003) - checked before any store work on those two endpoints.
        _globalThrottle = globalThrottle;
        _logger = logger;
        _presets = presets;
        _safety = safety;
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

    // ========================================================================
    //  accounts-identity/09 (#229): the family-device link surface.
    //
    //  Two UNauthenticated, rate-limited endpoints a kid's device (never signed in)
    //  calls - redeem + refresh - and four endpoints behind the account holder's OWN
    //  purchaser credential (the SAME reused guard the entitlement/preset surfaces use,
    //  never a second auth check): link (mint a code), list, revoke, adult-confirm.
    //
    //  The adult-confirm toggle (AC-07) is the ONLY way to opt a device into teen-plus,
    //  and it is edited HERE, through the authenticated account holder, never from the
    //  unauthenticated device. A freshly redeemed device is always family-safe (AC-02).
    // ========================================================================

    /// <summary>
    /// POST /api/accounts/devices/link (AC-01): the signed-in account holder mints a
    /// short, human-enterable link code tied to their AccountId. The parent reads it off
    /// the Account page and types it into the kid's device within the short window. The
    /// code is CSPRNG-minted from a distinct, higher-entropy alphabet than a room code.
    /// Requires the caller's own valid purchaser credential (401 otherwise).
    /// </summary>
    [HttpPost("devices/link")]
    public async Task<IActionResult> LinkDevice(CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        var (code, expiresUtc) = _deviceLinks.MintLinkCode(account.Id);
        return Ok(new LinkDeviceResult(code, expiresUtc));
    }

    /// <summary>
    /// POST /api/accounts/devices/redeem { code } (AC-02): a kid's device (NOT signed in)
    /// redeems a link code for a long-lived family-device token. The code travels in the
    /// BODY, never the URL (handles are secrets - a path segment leaks to logs / Referer /
    /// history). On success the RAW token is returned ONCE (only its hash is stored, AC-05)
    /// and the device defaults to the SAFE state (IsAdultConfirmedDevice = false, AC-07).
    /// Rate-limited per-IP AND globally (an IP-rotating attacker still hits the ceiling);
    /// the per-code attempt burn lives in the code store. A missing / expired / used /
    /// burned code returns a friendly, non-enumerating failure.
    /// </summary>
    [HttpPost("devices/redeem")]
    [EnableRateLimiting(FamilyDeviceRedeemRateLimit.PerIpPolicyName)]
    public async Task<IActionResult> RedeemDevice([FromBody] RedeemDeviceBody? request, CancellationToken cancellationToken)
    {
        // The GLOBAL ceiling (ADR 0003: per-IP is defeated by IP rotation). Checked FIRST,
        // before any store work, so an IP-rotating flood cannot mint / enumerate past the
        // aggregate budget. 429 when the process-wide window is spent.
        if (!_globalThrottle.TryAcquire())
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var code = (request?.Code ?? string.Empty).Trim();
        var outcome = code.Length == 0
            ? DeviceRedeemOutcome.Miss
            : await _deviceLinks.RedeemAsync(code, cancellationToken);

        if (!outcome.Success)
        {
            // Neutral failure (no oracle): the same shape whether the code was unknown,
            // expired, already used, or burned. Never says which.
            return Ok(new RedeemDeviceResult(
                Ok: false,
                Message: "That link code did not work - it may have expired or already been used. Ask for a fresh code and try again.",
                Token: null,
                Label: null));
        }

        return Ok(new RedeemDeviceResult(
            Ok: true,
            Message: "This device is linked to your family. Every game you start here now carries the family's unlocks.",
            Token: outcome.RawToken,
            Label: outcome.Label));
    }

    /// <summary>
    /// POST /api/accounts/devices/refresh { token } (security posture: rolling TTL +
    /// silent re-issue): the device calls this once per app launch to rotate its token
    /// (verify by hash, mint a replacement on the SAME row, invalidate the old value,
    /// slide the TTL). The token travels in the BODY, never the URL. Bounds how long a
    /// copied/stolen token stays valid. A dead / revoked / unknown token fails cleanly so
    /// the client re-links. Rate-limited exactly like redeem.
    /// </summary>
    [HttpPost("devices/refresh")]
    [EnableRateLimiting(FamilyDeviceRedeemRateLimit.PerIpPolicyName)]
    public async Task<IActionResult> RefreshDevice([FromBody] RefreshDeviceBody? request, CancellationToken cancellationToken)
    {
        // The GLOBAL ceiling, checked first (same rationale as redeem).
        if (!_globalThrottle.TryAcquire())
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var token = (request?.Token ?? string.Empty).Trim();
        var rotated = token.Length == 0
            ? null
            : await _deviceLinks.RefreshAsync(token, cancellationToken);

        return rotated is null
            ? Ok(new RefreshDeviceResult(Ok: false, Token: null))
            : Ok(new RefreshDeviceResult(Ok: true, Token: rotated));
    }

    /// <summary>
    /// GET /api/accounts/devices (AC-04): the signed-in account holder lists their linked
    /// devices with enough NON-PII context to make revocation actionable - the random
    /// label + a relative last-seen + the adult-confirm toggle position - and NOTHING
    /// device-identifying (no IP / user agent / raw token). Requires the caller's own
    /// credential (401 otherwise).
    /// </summary>
    [HttpGet("devices")]
    public async Task<IActionResult> ListDevices(CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        var devices = await _deviceTokens.ListByAccountAsync(account.Id, cancellationToken);
        // Project to the NON-PII summary - deliberately dropping the token hash so a list
        // read can never leak credential material (AC-04/AC-05).
        var summaries = devices
            .Select(d => new LinkedDeviceSummary(
                d.DeviceTokenId,
                d.Label,
                d.CreatedUtc,
                d.LastUsedUtc,
                d.IsAdultConfirmedDevice,
                d.Revoked))
            .ToList();
        return Ok(summaries);
    }

    /// <summary>
    /// POST /api/accounts/devices/{deviceTokenId}/revoke (AC-04): the account holder revokes
    /// one device; its token stops resolving immediately (a room created from it afterwards
    /// falls back to the default-unlocked, family-safe baseline, not an error). Idempotent -
    /// revoking an already-revoked or unknown device is a clean success. Requires the
    /// caller's own credential; the device is read within the caller's OWN account partition,
    /// so one account can never revoke another's device.
    /// </summary>
    [HttpPost("devices/{deviceTokenId:guid}/revoke")]
    public async Task<IActionResult> RevokeDevice(Guid deviceTokenId, CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        var device = await _deviceTokens.GetAsync(account.Id, deviceTokenId, cancellationToken);
        if (device is null)
        {
            // Unknown (or another account's) device - idempotent no-op success, no oracle.
            return NoContent();
        }

        if (!device.Revoked)
        {
            await _deviceTokens.UpdateAsync(device with { Revoked = true }, cancellationToken);
        }
        return NoContent();
    }

    /// <summary>
    /// POST /api/accounts/devices/{deviceTokenId}/adult-confirm { confirmed } (AC-07): the
    /// account holder flips a device's adult-unlock signal - the ONLY way to opt a device
    /// into the teen-plus tier, performed HERE through the authenticated adult, never from
    /// the device. A plain property update on the SAME row (not a new record). Setting it
    /// true takes effect at the NEXT room the device creates (entitlements are session-
    /// captured); setting it false returns the device to family-safe. Requires the caller's
    /// own credential; the device is read within the caller's own account partition.
    /// </summary>
    [HttpPost("devices/{deviceTokenId:guid}/adult-confirm")]
    public async Task<IActionResult> SetAdultConfirmed(Guid deviceTokenId, [FromBody] AdultConfirmBody? request, CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        var device = await _deviceTokens.GetAsync(account.Id, deviceTokenId, cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        var confirmed = request?.Confirmed ?? false;
        if (device.IsAdultConfirmedDevice != confirmed)
        {
            await _deviceTokens.UpdateAsync(device with { IsAdultConfirmedDevice = confirmed }, cancellationToken);
        }
        return NoContent();
    }

    /// <summary>
    /// GET /api/accounts/adult-signal -> { adultUnlocked } (accounts-identity/10, #247):
    /// the READ-ONLY, ANONYMOUS-ACCESSIBLE endpoint solo play calls once on mount to learn
    /// whether THIS device carries an adult-unlock signal. It resolves the SAME signal
    /// GameHub.OnConnectedAsync resolves for group play, through the SAME shared resolver
    /// (AC-06) - a purchaser credential (adult-by-construction) or a family-device token
    /// whose row is adult-confirmed -> true; anything else (no credential, an unconfirmed
    /// device, a bad/expired token, any error) -> false (AC-01/AC-03/AC-04 fail-safe).
    ///
    /// The credential is read via <see cref="ReadCredential"/> - the Authorization: Bearer
    /// header, falling back to the HttpOnly cookie - EXACTLY like the other account
    /// endpoints, NEVER from a query string or path segment: a bearer credential in a URL
    /// leaks to access logs / App Insights / the Referer header (ADR 0003's "handles are
    /// secrets" rule). There is no request body and no request field a client can set to
    /// assert the bool - it is resolved server-side, never accepted as input (AC-05).
    ///
    /// HONEST SCOPE (AC-07): this is an identity-aware CLIENT NUDGE, not a structural
    /// "can never." The teen-plus templates stay bundled in the web build and cached
    /// offline regardless of this signal, so a determined, technically capable kid can
    /// still reach them by overriding the client-held boolean or reading the cached
    /// bundle directly - the same bundled-content caveat group play's Room-based gate
    /// does NOT have to make (its gate is server-side). Closing that residual gap is the
    /// Option-B content-supply escalation the story tracks, deliberately out of scope
    /// here; this endpoint exists so solo's toggle cannot, by itself, unlock teen-plus on
    /// a device with no adult signal - not to make teen-plus structurally unreachable.
    /// </summary>
    [HttpGet("adult-signal")]
    public async Task<IActionResult> GetAdultSignal(CancellationToken cancellationToken)
    {
        var adultUnlocked = await _adultSignal.ResolveAdultSignalAsync(ReadCredential(), cancellationToken);
        return Ok(new AdultSignalResult(adultUnlocked));
    }

    /// <summary>
    /// Resolves the caller's OWN family account from their purchaser credential (the reused
    /// guard, accounts-identity/03 / billing-entitlements/05 - never a second auth check),
    /// or null when no valid credential is presented. Used by the authenticated device
    /// endpoints (link / list / revoke / adult-confirm) so only the account holder can act
    /// on their own account's devices.
    /// </summary>
    private async Task<Account?> ResolveAccountAsync(CancellationToken cancellationToken)
    {
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        if (string.IsNullOrEmpty(email))
        {
            return null;
        }
        return await _accounts.GetByIdentityAsync(email, cancellationToken);
    }

    // The credential: prefer the Authorization: Bearer value (the cross-origin path the
    // SPA holds from sign-in), fall back to the HttpOnly cookie (same-site deployment).
    // Mirrors EntitlementsController.ReadCredential exactly (the one shared shape).
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

    // ----------------------------------------------------------------------------
    //  Kid seat presets (accounts-identity/08, issue #228).
    //
    //  A small account-plane REST surface an adult manages from the Account page:
    //  list / create / update / delete named (nickname + Guardian variant) presets
    //  stored under their family account. Every endpoint is authorized by resolving
    //  the SAME purchaser credential accounts-identity/03 mints (via the shared
    //  PurchaserCredentialService) to the family AccountId - no new auth mechanism,
    //  and no valid credential -> 401. Presets are keyed by that AccountId, so an
    //  adult only ever reaches their own family's presets.
    //
    //  THE HARD BOUNDARY (AC-03): these endpoints are the ACCOUNT plane only. A preset
    //  is a join-time convenience; selecting one in the web client just fills the SAME
    //  display-name / variant controls and submits through the SAME CreateRoom /
    //  JoinRoom hub invokes. NOTHING here touches Room / Player, and there is no
    //  "preset join" path - the server cannot tell a preset join from a manual one.
    //
    //  SAFETY, SERVER-SIDE, EVERY TIME (AC-04/AC-07): a preset nickname is trimmed,
    //  length-capped (SeatPresetRules.MaxNicknameLength, the display-name cap), and run
    //  through the SAME IContentSafetyFilter as any manually typed name BEFORE it is
    //  stored - never trusted or pre-approved client-side. It is filtered AGAIN,
    //  independently, at join time by the unchanged hub filter.
    // ----------------------------------------------------------------------------

    /// <summary>
    /// GET /api/accounts/presets - the signed-in family's saved seat presets (AC-02).
    /// 401 when not signed in (no valid credential), so no preset state leaks to an
    /// unauthenticated visitor. A signed-in family with none (or an account that no
    /// longer resolves) gets an empty list, never an error.
    /// </summary>
    [HttpGet("presets")]
    public async Task<IActionResult> ListPresets(CancellationToken cancellationToken)
    {
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        if (email is null)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            // Signed in but the account no longer resolves (e.g. a deleted account):
            // a friendly empty list, not an error - there are simply no presets.
            return Ok(new SeatPresetsResult([]));
        }

        var presets = await _presets.ListAsync(account.Id, cancellationToken);
        return Ok(new SeatPresetsResult(presets.Select(ToView).ToList()));
    }

    /// <summary>
    /// POST /api/accounts/presets { nickname, variant } - create a preset under the
    /// signed-in family account (AC-01). 401 when not signed in. The nickname is vetted
    /// through the SAME length cap + content-safety filter as any display name before
    /// storage (AC-04/AC-07); a rejected name returns 400 with a friendly message and
    /// stores nothing. The variant is normalized to a known Guardian value.
    /// </summary>
    [HttpPost("presets")]
    public async Task<IActionResult> CreatePreset([FromBody] SeatPresetBody? request, CancellationToken cancellationToken)
    {
        var account = await ResolveSignedInAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        var validation = await ValidateNicknameAsync(request?.Nickname, cancellationToken);
        if (validation.Error is not null)
        {
            return BadRequest(new SeatPresetError(validation.Error));
        }

        var variant = SeatPresetRules.NormalizeVariant(request?.Variant);
        var created = await _presets.CreateAsync(account.Id, validation.Nickname, variant, cancellationToken);
        return Ok(ToView(created));
    }

    /// <summary>
    /// PUT /api/accounts/presets/{id} { nickname, variant } - update a preset under the
    /// signed-in family account (AC-01). 401 when not signed in; 404 when no preset with
    /// that id exists under this family (a stale / cross-account id - never a create).
    /// The nickname is re-vetted through the SAME filter (AC-04/AC-07); a rejected name
    /// returns 400 and changes nothing.
    /// </summary>
    [HttpPut("presets/{id}")]
    public async Task<IActionResult> UpdatePreset(string id, [FromBody] SeatPresetBody? request, CancellationToken cancellationToken)
    {
        var account = await ResolveSignedInAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        if (!Guid.TryParse(id, out var presetId))
        {
            // A malformed id can never match a stored preset - treat it as a clean miss.
            return NotFound();
        }

        var validation = await ValidateNicknameAsync(request?.Nickname, cancellationToken);
        if (validation.Error is not null)
        {
            return BadRequest(new SeatPresetError(validation.Error));
        }

        var variant = SeatPresetRules.NormalizeVariant(request?.Variant);
        var updated = await _presets.UpdateAsync(account.Id, presetId, validation.Nickname, variant, cancellationToken);
        return updated is null ? NotFound() : Ok(ToView(updated));
    }

    /// <summary>
    /// DELETE /api/accounts/presets/{id} - remove a preset under the signed-in family
    /// account. 401 when not signed in; 204 when a preset was removed; 404 when none
    /// existed under this family (already gone / cross-account). Idempotent.
    /// </summary>
    [HttpDelete("presets/{id}")]
    public async Task<IActionResult> DeletePreset(string id, CancellationToken cancellationToken)
    {
        var account = await ResolveSignedInAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        if (!Guid.TryParse(id, out var presetId))
        {
            return NotFound();
        }

        var deleted = await _presets.DeleteAsync(account.Id, presetId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>The outcome of vetting a candidate preset nickname: the trimmed nickname on success, or a friendly Error message.</summary>
    private readonly record struct NicknameValidation(string Nickname, string? Error);

    /// <summary>
    /// Vet a candidate preset nickname through the SAME rules a manually typed display
    /// name obeys (accounts-identity/08, AC-04/AC-07): trim, reject empty, reject over
    /// the display-name cap, and run the SHARED server-side content-safety filter. On a
    /// pass the trimmed nickname is returned with a null Error; on any failure the
    /// friendly, kid-readable message is returned and the nickname is not stored.
    /// </summary>
    private async Task<NicknameValidation> ValidateNicknameAsync(string? candidate, CancellationToken cancellationToken)
    {
        var nickname = (candidate ?? string.Empty).Trim();
        if (nickname.Length == 0)
        {
            return new NicknameValidation(nickname, "Give this seat preset a name.");
        }
        if (nickname.Length > SeatPresetRules.MaxNicknameLength)
        {
            return new NicknameValidation(nickname, $"That name is a bit long - keep it to {SeatPresetRules.MaxNicknameLength} characters.");
        }

        // The SAME gate every free-text surface routes through (child safety, README
        // section 6). A preset name is never trusted client-side; the server vets it
        // here before storage, and again at join time via the unchanged hub filter.
        var verdict = await _safety.CheckAsync(nickname, cancellationToken);
        return verdict.IsAllowed
            ? new NicknameValidation(nickname, null)
            : new NicknameValidation(nickname, verdict.Message);
    }

    /// <summary>
    /// Resolve the signed-in family account for a preset write (create / update /
    /// delete), or null when the request carries no valid credential OR the credential
    /// resolves to no account - either way the caller returns 401. A preset cannot be
    /// owned without an account.
    /// </summary>
    private async Task<Account?> ResolveSignedInAccountAsync(CancellationToken cancellationToken)
    {
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        return email is null ? null : await _accounts.GetByIdentityAsync(email, cancellationToken);
    }

    /// <summary>Maps a stored preset to its wire view (a pure { id, nickname, variant } tuple, AC-05).</summary>
    private static SeatPresetView ToView(SeatPreset preset) =>
        new(preset.Id.ToString(), preset.Nickname, preset.Variant);
}
