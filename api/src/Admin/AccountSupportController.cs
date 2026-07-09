// ----------------------------------------------------------------------------
//  AccountSupportController - the Support job's real payload (sysadmin-console/07,
//  issue #243, ADR 0003 Layer 3): find an ACCOUNT by what the person in front of you
//  can actually give you (a purchaser email or an AccountId), see its account-plane
//  picture in one place, and run the five concrete support verbs ADR 0003's audit
//  named - resend a lost magic link, extend an expiring shared tale, restore an
//  accidentally-deleted keepsake, comp/extend a stuck entitlement, resync a
//  subscription that drifted from Stripe. Every verb writes ONE row to the action log
//  (sysadmin-console/06), log-before-act, exactly like the other five call sites.
//
//  THE CROSS-PLANE FIREWALL IS STRUCTURAL, NOT ASSERTED (AC-08, BINDING - ADR 0003
//  "the support console cannot bridge the planes"):
//    - LOOKUP IS BY EMAIL / AccountId ONLY. A vault claim code and a public-tale slug
//      are NOT valid search inputs and never resolve to an account (the query is parsed
//      as a GUID or an email; anything else - a claim-code or slug shape - simply
//      resolves to the clear not-found state, never to an owner). A claim code is
//      redeemed by the PLAYER'S OWN DEVICE against keepsake-vault's recovery endpoint,
//      never by an operator lookup; a slug is acted on DIRECTLY as content (extend TTL /
//      restore), never resolved back to an owning account or byline.
//    - THE CONSTRUCTOR HOLDS NO BYLINE-CAPABLE REFERENCE. It injects narrow count-only /
//      enum-only / instant-only seams (IVaultAccountSummary, ILinkedDeviceCounter,
//      IVaultTaleRestorer, IPublishedTaleTtlExtender) - NEVER IVaultStore (whose
//      ListAsync returns per-tale bylines), IFamilyDeviceTokenStore (device rows +
//      timestamps), or IPublishedTaleStore (whose GetAsync returns a byline). So this
//      controller structurally CANNOT project a nickname, a tale timestamp, or a
//      per-tale list - AC-02's vault/tale figure is a bare count and nothing else.
//    - NO IMPORT FROM api/src/Rooms OR THE HUBS. Nothing here joins an account to a
//      player nickname, room, or session, nor a piece of content back to its author.
//    - The account-existence lookup is a mild "does an account exist for this email"
//      oracle - ACCEPTABLE only because it sits entirely behind operator authentication
//      (never public, never a player); it answers "does an account exist", never "does
//      this content belong to an account".
//
//  ADMIN BOUNDARY REUSE: every action is [Authorize(Policy = OperatorScopePolicy.Support)]
//  - the SAME Operator credential boundary as AdminEntitlementsController PLUS the Support
//  scope (sysadmin-console/05). A purchaser credential fails to unprotect under the
//  operator purpose and never reaches here; an unauthenticated caller gets 401.
//
//  ORCHESTRATES, NEVER REIMPLEMENTS: this controller composes merged seams - it does not
//  build any of them, and it does NOT fork AdminEntitlementsController's grant plumbing.
//  The comp/extend-entitlement verb (AC-06) reuses story 02's EXACT grant endpoint
//  (POST /api/admin/purchasers/{email}/entitlements) from the web side - there is NO
//  second grant write path here.
//
//  DEPENDENCY-TOLERANT: each panel / verb lights up on its own as its backing seam
//  responds and shows a plain "not available yet" state otherwise (a null count, a
//  Disabled/Unavailable outcome), never an error or a blank panel.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Admin;

// ---- Response DTOs (account-plane + content-plane facts ONLY, AC-08) --------------

/// <summary>
/// A dependency-tolerant section carrying a single COUNT (AC-02): the vault/tale figure and the
/// linked-devices figure. <see cref="Available"/> is false (and <see cref="Count"/> null) when the
/// backing seam is not wired yet - the section renders "not available yet" rather than an error or
/// a fabricated zero. A bare integer only - never a tale, byline, timestamp, or device identifier.
/// </summary>
/// <param name="Available">True when a real count resolved; false = the dependency-tolerant "not available yet" state.</param>
/// <param name="Count">The count when available; null otherwise.</param>
public sealed record SupportCountSection(bool Available, int? Count);

/// <summary>
/// The subscription-state section of an account summary (AC-02), derived ENTIRELY from the
/// subscription-sourced entitlement grants' recovery metadata (billing-entitlements/08: plan id,
/// Stripe subscription id, mode, lease end). PII-free and content-plane-free. When the account
/// holds no subscription grant, <see cref="HasSubscription"/> is false and the detail fields are
/// null - a clear "no subscription on file", never an error.
/// </summary>
/// <param name="HasSubscription">True when the account holds at least one subscription-sourced grant.</param>
/// <param name="Plan">The plan / product id (the grant's PlanId), or null.</param>
/// <param name="Status">"active" when the subscription lease is current, "lapsed" when it has passed; null with no subscription.</param>
/// <param name="ValidThrough">The subscription lease end, or null.</param>
/// <param name="StripeSubscriptionId">The Stripe subscription id, or null.</param>
/// <param name="Mode">The Stripe mode ("test" / "live") that produced the grant, or null.</param>
public sealed record SupportSubscriptionSection(
    bool HasSubscription,
    string? Plan,
    string? Status,
    DateTimeOffset? ValidThrough,
    string? StripeSubscriptionId,
    string? Mode);

/// <summary>
/// The unified account-plane picture the support lookup returns (AC-01/AC-02): the account itself
/// (stable id, created-at, canonical email), its current entitlement grants (the SAME
/// <see cref="PurchaserGrantDto"/> shape story 02 returns), its subscription state, an aggregate
/// vault/tale COUNT, and a linked-devices COUNT. Every section is account-plane / content-plane
/// facts ONLY - NEVER a player nickname, room, session, tale byline, tale timestamp, or a list of
/// individual tales (AC-08). When no account exists, <see cref="AccountExists"/> is false and the
/// remaining fields are their empty defaults (the clear not-found state, not an error).
/// </summary>
/// <param name="AccountExists">True when an account resolves for the query; false = the clear not-found state.</param>
/// <param name="AccountId">The stable AccountId (accounts-identity/05), or null when not found.</param>
/// <param name="Email">The canonical email (the account's when found, else the normalized input).</param>
/// <param name="CreatedUtc">When the account was created, or null when not found.</param>
/// <param name="Grants">The account's current capability leases (empty when no account or none held).</param>
/// <param name="Subscription">The subscription-state section (AC-02).</param>
/// <param name="VaultTales">The aggregate vault/tale COUNT section (AC-02, count-only).</param>
/// <param name="LinkedDevices">The linked-devices COUNT section (AC-02, count-only).</param>
public sealed record SupportAccountSummary(
    bool AccountExists,
    Guid? AccountId,
    string Email,
    DateTimeOffset? CreatedUtc,
    IReadOnlyList<PurchaserGrantDto> Grants,
    SupportSubscriptionSection Subscription,
    SupportCountSection VaultTales,
    SupportCountSection LinkedDevices);

/// <summary>The resend-magic-link result (AC-03): whether the link was issued + a friendly message.</summary>
/// <param name="Ok">True when a fresh link was issued to the account (false = not found / capped).</param>
/// <param name="Message">A friendly, non-leaking operator-facing message.</param>
public sealed record ResendLinkResponse(bool Ok, string Message);

/// <summary>The extend-TTL result (AC-04): slug + new expiry ONLY (never a byline), plus a message.</summary>
/// <param name="Outcome">"extended" / "not-found" / "unavailable".</param>
/// <param name="Slug">The slug acted on, or null when unavailable.</param>
/// <param name="NewExpiryUtc">The new expiry on success; null otherwise.</param>
/// <param name="Message">A friendly operator-facing message.</param>
public sealed record ExtendTaleTtlResponse(string Outcome, string? Slug, DateTimeOffset? NewExpiryUtc, string Message);

/// <summary>The restore-keepsake result (AC-05): the outcome + a friendly message. No tale content.</summary>
/// <param name="Outcome">"restored" / "not-found".</param>
/// <param name="Message">A friendly operator-facing message.</param>
public sealed record RestoreKeepsakeResponse(string Outcome, string Message);

/// <summary>
/// The resync result (AC-07): whether the account was found + billing configured, the mode it ran
/// against, and PII-free counts of what changed / was skipped - mirroring AdminBillingResyncController's
/// shape (billing-entitlements/08). No nickname / room / session ever appears here.
/// </summary>
/// <param name="AccountFound">True when an account exists for the requested id.</param>
/// <param name="BillingConfigured">False when Stripe is not configured - nothing was read or written.</param>
/// <param name="ActiveMode">The Stripe mode the resync ran against ("test" / "live"), or null when not run.</param>
/// <param name="Reconciled">How many capability grants were written / overwritten.</param>
/// <param name="SkippedUnmatchedIdentity">Candidate subscriptions skipped for a non-matching qs_purchaser.</param>
/// <param name="SkippedModeGuard">Existing grants left untouched by the mode / source guard.</param>
/// <param name="SkippedNoMetadata">Matched subscriptions skipped for carrying no capability metadata.</param>
/// <param name="Message">A friendly operator-facing summary.</param>
public sealed record SupportResyncResponse(
    bool AccountFound,
    bool BillingConfigured,
    string? ActiveMode,
    int Reconciled,
    int SkippedUnmatchedIdentity,
    int SkippedModeGuard,
    int SkippedNoMetadata,
    string Message);

// ---- Request bodies ---------------------------------------------------------------

/// <summary>Request body for the resend-magic-link verb (AC-03): the target account email.</summary>
/// <param name="Email">The account email to resend a sign-in link to. May be null/empty - handled as not-found.</param>
public sealed record ResendLinkRequest(string? Email);

/// <summary>Request body for the extend-TTL verb (AC-04): the public tale slug (a DIRECT content input, never an account key).</summary>
/// <param name="Slug">The tale slug to extend. May be null/empty - handled as not-found.</param>
public sealed record ExtendTaleTtlRequest(string? Slug);

/// <summary>
/// Request body for the restore-keepsake verb (AC-05): the DIRECT (vaultId, taleId) content
/// identifiers plus a SINGLE light confirmation. The vault id is a bearer handle carried in the
/// BODY (never a URL path/query - handles are secrets, ADR 0003), and is a direct content input,
/// never an account search key.
/// </summary>
/// <param name="VaultId">The owning vault id (a device-held bearer handle).</param>
/// <param name="TaleId">The tale id to restore within the vault.</param>
/// <param name="Confirm">The single, light confirmation (must be true) - lower friction than the Content tab's takedown restore.</param>
public sealed record RestoreKeepsakeRequest(string? VaultId, string? TaleId, bool Confirm);

/// <summary>Request body for the resync verb (AC-07): the target account's stable id.</summary>
/// <param name="AccountId">The stable AccountId to reconcile (resolved via IAccountStore, never a raw email typed at call time).</param>
public sealed record SupportResyncRequest(Guid? AccountId);

[ApiController]
[Route("api/admin/support")]
// sysadmin-console/05 (#214): the SUPPORT scope (find a person, fix their problem). Pins the same
// Operator credential boundary as the base policy PLUS the Support scope - a no-op for today's
// all-scopes operator; a future support-only operator is a config entry, not a controller rework.
[Authorize(Policy = OperatorScopePolicy.Support)]
public sealed class AccountSupportController : ControllerBase
{
    // The action verbs this controller logs (sysadmin-console/06) - stable free-form strings.
    private const string ActionResendLink = "account.resend-link";
    private const string ActionExtendTtl = "tale.extend-ttl";
    private const string ActionRestoreKeepsake = "vault.restore";
    private const string ActionResync = "subscription.resync";

    private readonly IAccountStore _accounts;
    private readonly IEntitlementGrantStore _grants;
    private readonly IVaultAccountSummary _vaultSummary;
    private readonly ILinkedDeviceCounter _deviceCount;
    private readonly IPublishedTaleTtlExtender _ttlExtender;
    private readonly IVaultTaleRestorer _vaultRestore;
    private readonly IStripeReconciliationService _reconciliation;
    private readonly ISupportMagicLinkResend _resend;
    private readonly SupportResendAccountThrottle _resendThrottle;
    private readonly SupportResyncAccountThrottle _resyncThrottle;
    private readonly IOperatorActionLog _actionLog;

    /// <summary>
    /// Constructs the controller over the merged seams it ORCHESTRATES (it reimplements none):
    /// the account store (email/AccountId lookup, AC-01), the grant store (grants + subscription
    /// metadata read, AC-02), the NARROW count-only / enum-only vault + device + tale seams
    /// (AC-02/AC-04/AC-05 - deliberately NOT the byline-bearing IVaultStore /
    /// IFamilyDeviceTokenStore / IPublishedTaleStore, AC-08), the Stripe reconciliation service
    /// (resync, AC-07), the magic-link resend seam (AC-03 - so this controller never calls
    /// IEmailSender directly), the two per-target-account throttles (AC-03b/AC-07), and the ONE
    /// operator action log every verb appends through (log-before-act).
    /// </summary>
    public AccountSupportController(
        IAccountStore accounts,
        IEntitlementGrantStore grants,
        IVaultAccountSummary vaultSummary,
        ILinkedDeviceCounter deviceCount,
        IPublishedTaleTtlExtender ttlExtender,
        IVaultTaleRestorer vaultRestore,
        IStripeReconciliationService reconciliation,
        ISupportMagicLinkResend resend,
        SupportResendAccountThrottle resendThrottle,
        SupportResyncAccountThrottle resyncThrottle,
        IOperatorActionLog actionLog)
    {
        _accounts = accounts;
        _grants = grants;
        _vaultSummary = vaultSummary;
        _deviceCount = deviceCount;
        _ttlExtender = ttlExtender;
        _vaultRestore = vaultRestore;
        _reconciliation = reconciliation;
        _resend = resend;
        _resendThrottle = resendThrottle;
        _resyncThrottle = resyncThrottle;
        _actionLog = actionLog;
    }

    // ---- AC-01 / AC-02: the account lookup + detail panel -------------------------

    /// <summary>
    /// GET /api/admin/support/accounts/{query} -> the unified account summary (AC-01/AC-02). The
    /// query is a purchaser email OR a stable AccountId (a GUID) - NOTHING else. A vault claim code
    /// or a public-tale slug is neither a well-formed email nor a GUID, so it resolves to the clear
    /// not-found state, never to an account (AC-01/AC-08). Returns AccountExists = false with empty
    /// sections when nothing resolves - NOT a 404 error. Every section is account-plane / content-
    /// plane facts only (AC-08).
    /// </summary>
    [HttpGet("accounts/{query}")]
    public async Task<IActionResult> Lookup(string query, CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(query, cancellationToken);
        if (account is null)
        {
            // The clear not-found state: echo the normalized query (never an error). No count seams
            // are consulted (there is no account to count for).
            return Ok(new SupportAccountSummary(
                AccountExists: false,
                AccountId: null,
                Email: NormalizeEmail(query),
                CreatedUtc: null,
                Grants: [],
                Subscription: new SupportSubscriptionSection(false, null, null, null, null, null),
                VaultTales: new SupportCountSection(false, null),
                LinkedDevices: new SupportCountSection(false, null)));
        }

        var now = DateTimeOffset.UtcNow;

        // Grants (AC-02): read off the stable AccountId and project into the SAME display DTO story
        // 02 returns - a READ of the same store, never a second write path.
        var grants = await _grants.GetGrantsAsync(account.Id, cancellationToken);
        var grantDtos = grants
            .Select(g => new PurchaserGrantDto(
                CapabilityKey: g.CapabilityKey,
                Label: EntitlementLabels.LabelFor(g.CapabilityKey),
                ValidThrough: g.ValidThrough,
                Source: g.Source,
                Active: g.IsActiveAt(now)))
            .OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Subscription (AC-02): derived from the subscription-sourced grants' recovery metadata
        // (billing-entitlements/08). No separate subscription store, no player/content field.
        var subscription = BuildSubscriptionSection(grants, now);

        // Vault/tale count (AC-02, count-only): the narrow projection returns null when keepsake-
        // vault does not yet expose an account-scoped count - the dependency-tolerant "unavailable"
        // state, never a fabricated zero.
        var vaultCount = await _vaultSummary.CountForAccountAsync(account.Id, cancellationToken);

        // Linked-devices count (AC-02, count-only): the narrow counter over accounts-identity/09.
        var deviceCount = await _deviceCount.CountForAccountAsync(account.Id, cancellationToken);

        return Ok(new SupportAccountSummary(
            AccountExists: true,
            AccountId: account.Id,
            Email: account.Email,
            CreatedUtc: account.CreatedUtc,
            Grants: grantDtos,
            Subscription: subscription,
            VaultTales: new SupportCountSection(vaultCount is not null, vaultCount),
            LinkedDevices: new SupportCountSection(deviceCount is not null, deviceCount)));
    }

    // ---- AC-03: resend magic link -------------------------------------------------

    /// <summary>
    /// POST /api/admin/support/resend-link { email } -> reissue a purchaser sign-in link (AC-03).
    /// Bounded on TWO axes: (a) the SAME per-IP [EnableRateLimiting(SignInRateLimit.PolicyName)] the
    /// public request endpoint uses (caller side), and (b) a per-TARGET-account cap independent of
    /// the operator IP (SupportResendAccountThrottle) - closing the email-bomb vector. Resolves the
    /// account (read-only; a link is only ever resent to an existing account's own address), logs
    /// ONE row BEFORE delivering (log-before-act), then issues + sends through the SAME accounts-
    /// identity/04 email seam via ISupportMagicLinkResend (never IEmailSender directly).
    /// </summary>
    [HttpPost("resend-link")]
    [EnableRateLimiting(SignInRateLimit.PolicyName)]
    public async Task<IActionResult> ResendLink([FromBody] ResendLinkRequest? request, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByIdentityAsync(request?.Email ?? string.Empty, cancellationToken);
        if (account is null)
        {
            // No account for that email - nothing is ever sent to an arbitrary address (never an
            // email-bomb amplifier). A clear not-found, never an error. No log row (nothing acted).
            return Ok(new ResendLinkResponse(false, "No account found for that email - nothing to resend."));
        }

        // The per-TARGET-account cap (AC-03b), independent of the operator IP: even a single
        // operator/IP cannot flood ONE inbox. A capped request returns 429 WITHOUT sending or logging.
        if (!_resendThrottle.TryAcquire(account.Id))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ResendLinkResponse(
                false, "This account has had several sign-in links resent recently - please wait a few minutes before trying again."));
        }

        // LOG-BEFORE-ACT (sysadmin-console/06): append the row BEFORE delivery. A bad target throws
        // and aborts before any send - a resend can never happen with no trail. Target is the
        // account email (account-plane fact).
        await _actionLog.AppendAsync(
            User.Identity?.Name ?? string.Empty, ActionResendLink, account.Email, "resend magic link", cancellationToken);

        // Deliver through the shared email seam (fail-safe). The request origin is the link fallback
        // when no public LinkBaseUrl is configured (local dev, where the sender is a no-op).
        await _resend.ResendAsync(account.Email, $"{Request.Scheme}://{Request.Host}", cancellationToken);

        return Ok(new ResendLinkResponse(true, $"A fresh sign-in link is on its way to {account.Email}."));
    }

    // ---- AC-04: extend a public tale's TTL ----------------------------------------

    /// <summary>
    /// POST /api/admin/support/tales/extend-ttl { slug } -> push a public tale's expiry out (AC-04)
    /// through IPublishedTaleStore's EXISTING write path (no parallel store), via the narrow
    /// IPublishedTaleTtlExtender. The slug is a DIRECT content input, NEVER resolved back to an
    /// owning account or byline (AC-08). The response includes ONLY the slug + the new expiry; the
    /// action is logged with the slug as the target.
    /// </summary>
    [HttpPost("tales/extend-ttl")]
    public async Task<IActionResult> ExtendTaleTtl([FromBody] ExtendTaleTtlRequest? request, CancellationToken cancellationToken)
    {
        var slug = (request?.Slug ?? string.Empty).Trim();
        if (slug.Length == 0)
        {
            return BadRequest(new ExtendTaleTtlResponse("not-found", null, null, "A tale slug is required."));
        }

        // LOG-BEFORE-ACT: append the row BEFORE the effectful TTL write. Target is the slug (a
        // content-plane fact, never a byline / owner).
        await _actionLog.AppendAsync(
            User.Identity?.Name ?? string.Empty, ActionExtendTtl, slug, "extend public tale TTL", cancellationToken);

        var result = await _ttlExtender.ExtendTtlAsync(slug, DateTimeOffset.UtcNow, cancellationToken);
        var (outcome, message) = result.Outcome switch
        {
            ExtendTaleTtlOutcome.Extended => ("extended", $"Extended the link for {slug} - it now expires {result.NewExpiryUtc:u}."),
            ExtendTaleTtlOutcome.Unavailable => ("unavailable", "Public tale links are not available in this environment."),
            _ => ("not-found", $"No live public tale resolves for {slug} - nothing to extend."),
        };
        return Ok(new ExtendTaleTtlResponse(outcome, result.Slug, result.NewExpiryUtc, message));
    }

    // ---- AC-05: restore a user's own deleted keepsake -----------------------------

    /// <summary>
    /// POST /api/admin/support/vault/restore { vaultId, taleId, confirm } -> restore a user's OWN
    /// accidentally-deleted keepsake (AC-05) through keepsake-vault/04's self-delete/restore seam
    /// (via the narrow IVaultTaleRestorer). This is a DISTINCT, LOWER-friction verb from the Content
    /// tab's moderation-takedown restore (which requires a stronger confirmation marker) - the two
    /// are never merged. It needs only the SINGLE light confirmation (confirm = true). The vault id +
    /// tale id are DIRECT content inputs carried in the BODY (handles are secrets); the vault id is
    /// NEVER logged - the log target is the tale id.
    /// </summary>
    [HttpPost("vault/restore")]
    public async Task<IActionResult> RestoreKeepsake([FromBody] RestoreKeepsakeRequest? request, CancellationToken cancellationToken)
    {
        var vaultId = (request?.VaultId ?? string.Empty).Trim();
        var taleId = (request?.TaleId ?? string.Empty).Trim();
        if (vaultId.Length == 0 || taleId.Length == 0)
        {
            return BadRequest(new RestoreKeepsakeResponse("not-found", "A vault id and a tale id are required."));
        }

        // The single, light confirmation (AC-05): a courtesy action with no safety implication, so
        // one affirmative flag suffices - deliberately less friction than a moderation-takedown restore.
        if (!request!.Confirm)
        {
            return BadRequest(new RestoreKeepsakeResponse("not-found", "Please confirm the restore."));
        }

        // LOG-BEFORE-ACT: append the row BEFORE the effectful restore. Target is the TALE ID (a
        // content identifier) - NEVER the vault id, which is a bearer secret (ADR 0003 "handles are
        // secrets"; the telemetry scrubber cannot clean a logged handle).
        await _actionLog.AppendAsync(
            User.Identity?.Name ?? string.Empty, ActionRestoreKeepsake, taleId, "restore user keepsake", cancellationToken);

        var outcome = await _vaultRestore.RestoreAsync(vaultId, taleId, cancellationToken);
        return outcome == VaultTaleRestoreOutcome.Restored
            ? Ok(new RestoreKeepsakeResponse("restored", "The keepsake has been restored and will serve normally again."))
            : Ok(new RestoreKeepsakeResponse("not-found", "No restorable keepsake was found for that vault and tale - it may be past its restore window."));
    }

    // ---- AC-07: resync a subscription from Stripe ---------------------------------

    /// <summary>
    /// POST /api/admin/support/resync { accountId } -> reconcile ONE account's subscription grants
    /// from Stripe (AC-07), via billing-entitlements/08's merged IStripeReconciliationService.
    /// Bounded on TWO axes: (a) the caller-side [EnableRateLimiting(StripeResyncRateLimit.PolicyName)]
    /// global budget, and (b) a per-TARGET-account debounce (SupportResyncAccountThrottle) so
    /// repeated clicks / tickets for ONE account cannot hammer the Stripe API. Resolves the AccountId
    /// through the account store FIRST (never a raw string), logs ONE row before the resync, then runs
    /// it. Returns a clear not-found state (not a 404) when no account exists.
    /// </summary>
    [HttpPost("resync")]
    [EnableRateLimiting(StripeResyncRateLimit.PolicyName)]
    public async Task<IActionResult> Resync([FromBody] SupportResyncRequest? request, CancellationToken cancellationToken)
    {
        if (request?.AccountId is not { } accountId || accountId == Guid.Empty)
        {
            return BadRequest(new { message = "A target accountId is required." });
        }

        // Resolve the target through the account store (never a raw string) and confirm it exists
        // before any Stripe read.
        var account = await _accounts.GetByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return Ok(new SupportResyncResponse(
                AccountFound: false, BillingConfigured: false, ActiveMode: null,
                0, 0, 0, 0, "No account found for that id - nothing to resync."));
        }

        // The per-TARGET-account debounce (AC-07): repeated clicks for ONE account are refused within
        // the minimum interval, so they cannot fan out Stripe list traffic. A debounced request
        // returns 429 WITHOUT calling Stripe or logging (nothing acted).
        if (!_resyncThrottle.TryAcquire(account.Id))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new SupportResyncResponse(
                AccountFound: true, BillingConfigured: false, ActiveMode: null, 0, 0, 0, 0,
                "A resync for this account just ran - please wait a moment before trying again."));
        }

        // LOG-BEFORE-ACT: append the row BEFORE the Stripe reconciliation. Target is the account
        // email (account-plane fact).
        await _actionLog.AppendAsync(
            User.Identity?.Name ?? string.Empty, ActionResync, account.Email, "resync subscription from Stripe", cancellationToken);

        var result = await _reconciliation.ResyncAccountAsync(account, cancellationToken);
        var message = !result.BillingConfigured
            ? "Billing is not configured - nothing to resync."
            : $"Resync complete: {result.Reconciled} grant(s) reconciled, "
                + $"{result.SkippedUnmatchedIdentity} unmatched, {result.SkippedModeGuard} mode-guarded, "
                + $"{result.SkippedNoMetadata} without metadata.";

        return Ok(new SupportResyncResponse(
            AccountFound: true,
            BillingConfigured: result.BillingConfigured,
            ActiveMode: result.ActiveMode?.ToWire(),
            Reconciled: result.Reconciled,
            SkippedUnmatchedIdentity: result.SkippedUnmatchedIdentity,
            SkippedModeGuard: result.SkippedModeGuard,
            SkippedNoMetadata: result.SkippedNoMetadata,
            Message: message));
    }

    // ---- helpers ------------------------------------------------------------------

    /// <summary>
    /// Resolves the lookup query to an account (AC-01) by EMAIL or AccountId ONLY - the structural
    /// firewall (AC-08). A GUID-shaped query resolves via GetByIdAsync; anything else is treated as
    /// an email via GetByIdentityAsync. A vault claim code or a public-tale slug is neither a GUID
    /// nor a well-formed email, so it simply misses (returns null) - it is NEVER resolved to an
    /// account, on this surface or any extension of it. Both store reads are READ-ONLY (never create).
    /// </summary>
    private async Task<Account?> ResolveAccountAsync(string query, CancellationToken cancellationToken)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // A GUID query is a stable AccountId - resolve it directly (accounts-identity/05).
        if (Guid.TryParse(trimmed, out var accountId))
        {
            return await _accounts.GetByIdAsync(accountId, cancellationToken);
        }

        // Otherwise treat it as an email. The account store normalizes internally; a claim-code /
        // slug shape simply finds nothing (the email-hash index has no such entry).
        return await _accounts.GetByIdentityAsync(trimmed, cancellationToken);
    }

    /// <summary>
    /// Derives the subscription-state section (AC-02) from the account's subscription-sourced grants'
    /// recovery metadata (billing-entitlements/08). Picks the subscription grant with the LATEST lease
    /// end (the current period), reads its plan / Stripe subscription id / mode, and computes an
    /// active/lapsed status from the lease. No subscription grant -> a clear "no subscription on file".
    /// PII-free and content-plane-free.
    /// </summary>
    private static SupportSubscriptionSection BuildSubscriptionSection(IReadOnlyList<EntitlementGrant> grants, DateTimeOffset now)
    {
        // The most-current subscription grant: order by lease end (a null lease - "no expiry" - sorts
        // last as the most durable). Operator comps / one-time packs are not subscriptions.
        var subscription = grants
            .Where(g => g.Source == GrantSource.Subscription)
            .OrderByDescending(g => g.ValidThrough ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        if (subscription is null)
        {
            return new SupportSubscriptionSection(false, null, null, null, null, null);
        }

        return new SupportSubscriptionSection(
            HasSubscription: true,
            Plan: subscription.PlanId,
            Status: subscription.IsActiveAt(now) ? "active" : "lapsed",
            ValidThrough: subscription.ValidThrough,
            StripeSubscriptionId: subscription.StripeSubscriptionId,
            Mode: subscription.Mode?.ToWire());
    }

    /// <summary>Trims + lowercases an email to the canonical form the account store uses, for the not-found echo.</summary>
    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
