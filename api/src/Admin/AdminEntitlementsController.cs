// ----------------------------------------------------------------------------
//  AdminEntitlementsController - the OPERATOR grant / revoke of a purchaser
//  entitlement by email (sysadmin-console/02, issue #136). The manual, human-
//  operated side door that unsticks a paying customer whose entitlement did not
//  apply, WITHOUT hand-editing Table Storage. A signed-in operator (story 01's
//  boundary) looks a purchaser up by email and grants or revokes a capability.
//
//  ONE STORE, ONE PARTITION (the load-bearing contract): the write lands as the
//  SAME lease-shaped EntitlementGrant billing-entitlements/01 defines, with
//  source = Operator, written through the SAME IEntitlementGrantStore the session-
//  creation gate (StoredValueEntitlementService) reads - never a parallel write
//  path. It resolves the purchaser to an Account FIRST and keys EVERY read and
//  write off account.Email, so a write and the session-creation read funnel through
//  the identical AccountIdentity.KeyFor partition (IEntitlementGrantStore's
//  load-bearing contract). Keying a grant off any other field would silently make
//  it unreadable at session-creation.
//
//  REVOKE IS A LAPSED LEASE, NOT A DELETE (AC-03): the grant store is UPSERT-only.
//  Revoke writes the SAME capability key with a validThrough of "now", so the lease
//  is already past -> IsActiveAt(now) is false -> the NEXT EvaluateForSession reads
//  the capability as locked. An already-open (already-captured) session holds an
//  immutable SessionEntitlements snapshot and is unaffected - only new sessions see
//  the change (README section 3: "not per-request").
//
//  IDEMPOTENT, LOW-CEREMONY (AC-06): grant / revoke UPSERT by capability key, so
//  re-granting the same key REPLACES its lease rather than piling up rows. There is
//  NO audit entity, approval workflow, or history beyond the operator seeing the
//  refreshed grants they just wrote.
//
//  ANONYMITY FIREWALL (AC-04, non-negotiable): this surface operates ENTIRELY on the
//  purchaser plane (email in, grant out). It keys and displays SOLELY by purchaser
//  identity (email) + capability keys. It imports NOTHING from api/src/Rooms or the
//  hubs - there is NO path from a purchaser record to a player nickname, room code,
//  or session, and no "which sessions did this household create" convenience (that
//  is exactly the join ADR 0002 forbids).
//
//  ADMIN BOUNDARY REUSE (AC-05): every action is [Authorize(Policy =
//  OperatorSession.PolicyName)] over the operator scheme - EXACTLY like story 03's
//  ReportedTalesController. A purchaser credential fails to unprotect under the
//  operator purpose and never reaches here; an unauthenticated caller gets 401.
//  Never a bare [Authorize], never a purchaser-facing route.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// One capability lease as the back-office lookup returns it (AC-01): the capability
/// key, its friendly label, the lease end (null = no expiry / one-time pack), the
/// grant source, and whether the lease is active right now. Carries ONLY capability +
/// lease data - never a player / room / session reference (AC-04).
/// </summary>
/// <param name="CapabilityKey">The catalog capability key (e.g. "library.full", "pack.spooky").</param>
/// <param name="Label">A friendly display name (via <see cref="EntitlementLabels"/>).</param>
/// <param name="ValidThrough">The lease end. Null = permanent (a one-time pack); a value = active until that instant (exclusive).</param>
/// <param name="Source">How the grant was obtained (subscription / one-time / operator).</param>
/// <param name="Active">Whether the lease is active at the moment the lookup ran.</param>
public sealed record PurchaserGrantDto(
    string CapabilityKey,
    string Label,
    DateTimeOffset? ValidThrough,
    GrantSource Source,
    bool Active);

/// <summary>
/// The purchaser lookup response (AC-01): whether an account exists for the looked-up
/// email, the canonical (normalized) email, and the purchaser's current grants. When
/// no account exists <see cref="AccountExists"/> is false and <see cref="Grants"/> is
/// empty - a clear "no account found for this email" state, NOT an error. Scoped to
/// email + capability keys ONLY (AC-04).
/// </summary>
/// <param name="AccountExists">True when an account exists for this email; false = the clear not-found state.</param>
/// <param name="Email">The canonical email the lookup resolved (the account's Email when found, else the normalized input).</param>
/// <param name="Grants">The purchaser's current capability leases (empty when no account or none held).</param>
public sealed record PurchaserLookupResult(
    bool AccountExists,
    string Email,
    IReadOnlyList<PurchaserGrantDto> Grants);

/// <summary>
/// The grant request body (AC-02): the capability key to unlock and an operator-set
/// lease end. A null <see cref="ValidThrough"/> means "no expiry" - a permanent, one-
/// time-pack-shaped grant. The source is always <see cref="GrantSource.Operator"/>
/// (set server-side, never trusted from the body).
/// </summary>
/// <param name="CapabilityKey">A catalog capability key from <see cref="EntitlementCatalog"/> (fixed keys or a pack.&lt;id&gt;).</param>
/// <param name="ValidThrough">The operator-set lease end, or null for "no expiry".</param>
public sealed record GrantEntitlementRequest(string? CapabilityKey, DateTimeOffset? ValidThrough);

/// <summary>
/// The result of a grant / revoke action (AC-02 / AC-03): the refreshed purchaser
/// view so the operator sees exactly what they just did (low-ceremony, AC-06), plus a
/// friendly message. There is no separate audit record - this echo IS the feedback.
/// </summary>
/// <param name="Purchaser">The purchaser's refreshed lookup state after the write.</param>
/// <param name="Message">A friendly message describing the outcome.</param>
public sealed record EntitlementActionResult(PurchaserLookupResult Purchaser, string Message);

[ApiController]
[Route("api/admin/purchasers")]
[Authorize(Policy = OperatorSession.PolicyName)]
public sealed class AdminEntitlementsController : ControllerBase
{
    private readonly IAccountStore _accounts;
    private readonly IEntitlementGrantStore _grants;

    /// <summary>
    /// Constructs the controller over accounts-identity/02's account store (the
    /// purchaser lookup, AC-01) and billing-entitlements/01's grant store (the read /
    /// write seam, AC-02/AC-03). It ORCHESTRATES those - it never reimplements them and
    /// never touches any room / player store (AC-04).
    /// </summary>
    public AdminEntitlementsController(IAccountStore accounts, IEntitlementGrantStore grants)
    {
        _accounts = accounts;
        _grants = grants;
    }

    /// <summary>
    /// GET /api/admin/purchasers/{email} -> the purchaser lookup (AC-01). Resolves the
    /// account by email (a READ that never creates - IAccountStore.GetByIdentityAsync)
    /// and lists its current grants. When no account exists, returns AccountExists =
    /// false with an empty grant list (the clear not-found state), NOT a 404 error.
    /// Displays SOLELY email + capability keys / leases (AC-04).
    /// </summary>
    [HttpGet("{email}")]
    public async Task<IActionResult> Lookup(string email, CancellationToken cancellationToken)
    {
        var result = await BuildLookupAsync(email, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/admin/purchasers/{email}/entitlements -> grant a capability (AC-02).
    /// Resolves the account FIRST (create-or-get: an operator comp for a paying customer
    /// whose account never materialized should still land, so we ensure the account
    /// exists), then UPSERTS an EntitlementGrant keyed off account.Email with source =
    /// Operator and the operator-set validThrough (null = no expiry). Written through the
    /// SAME store the session-creation gate reads. Idempotent by construction: re-granting
    /// the same key replaces its lease (AC-06). Rejects a capability key outside the
    /// catalog with 400 (no rival catalog).
    /// </summary>
    [HttpPost("{email}/entitlements")]
    public async Task<IActionResult> Grant(
        string email,
        [FromBody] GrantEntitlementRequest? request,
        CancellationToken cancellationToken)
    {
        // Guard a missing / null JSON body with a 400 (matches every other [FromBody]
        // action here): do not lean on [ApiController]'s auto-validation to be the only
        // thing standing between a null body and the NullReferenceException on the deref
        // below - guard it explicitly, like the rest of the codebase does.
        if (request is null)
        {
            return BadRequest(new { message = "A capability grant is required." });
        }

        var capabilityKey = NormalizeCapabilityKey(request.CapabilityKey);
        if (capabilityKey is null)
        {
            return BadRequest(new { message = "That is not a grantable capability key." });
        }

        // Resolve (create-or-get) the account, then key the grant off its CANONICAL
        // normalized email - the load-bearing contract so this write and the session-
        // creation read land in the SAME partition (IEntitlementGrantStore).
        var account = await _accounts.CreateOrGetAsync(email, cancellationToken);
        var grant = new EntitlementGrant(capabilityKey, request.ValidThrough, GrantSource.Operator);
        await _grants.PutGrantAsync(account.Email, grant, cancellationToken);

        var refreshed = await BuildLookupAsync(account.Email, cancellationToken);
        return Ok(new EntitlementActionResult(
            refreshed,
            $"Granted {EntitlementLabels.LabelFor(capabilityKey)} to {account.Email}."));
    }

    /// <summary>
    /// DELETE /api/admin/purchasers/{email}/entitlements/{key} -> revoke a capability
    /// (AC-03). The grant store is UPSERT-only, so a revoke writes the SAME key with a
    /// validThrough of "now" (source = Operator): the lease is already past, so the NEXT
    /// EvaluateForSession reads the capability as locked, while an already-captured
    /// session snapshot is unaffected (README section 3). When no account exists, returns
    /// the clear not-found state rather than an error (nothing to revoke). Idempotent.
    /// </summary>
    [HttpDelete("{email}/entitlements/{key}")]
    public async Task<IActionResult> Revoke(string email, string key, CancellationToken cancellationToken)
    {
        var capabilityKey = NormalizeCapabilityKey(key);
        if (capabilityKey is null)
        {
            return BadRequest(new { message = "That is not a revocable capability key." });
        }

        // A revoke against an email with no account is a harmless no-op: there is nothing
        // to lapse. Return the not-found state (never create an account just to revoke).
        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            return Ok(new EntitlementActionResult(
                await BuildLookupAsync(email, cancellationToken),
                "No account found for this email - nothing to revoke."));
        }

        // Lapse the lease: a validThrough of "now" makes IsActiveAt(now) false at the next
        // session-creation read (the lease end is exclusive), so the capability reads as
        // locked. Keyed off account.Email so it replaces the SAME row the grant wrote.
        var lapsed = new EntitlementGrant(capabilityKey, DateTimeOffset.UtcNow, GrantSource.Operator);
        await _grants.PutGrantAsync(account.Email, lapsed, cancellationToken);

        var refreshed = await BuildLookupAsync(account.Email, cancellationToken);
        return Ok(new EntitlementActionResult(
            refreshed,
            $"Revoked {EntitlementLabels.LabelFor(capabilityKey)} for {account.Email}."));
    }

    /// <summary>
    /// Builds the purchaser lookup view for <paramref name="email"/>: resolve the account
    /// (read-only, never creates), then read its grants off account.Email and project each
    /// into a display DTO with its friendly label and current-active flag. When no account
    /// exists, returns the clear not-found state over the normalized email. Touches ONLY
    /// the account + grant stores (AC-04).
    /// </summary>
    private async Task<PurchaserLookupResult> BuildLookupAsync(string email, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            // The clear "no account found for this email" state (AC-01): echo the input
            // (normalized to the canonical form) so the operator sees what they searched.
            return new PurchaserLookupResult(false, NormalizeEmail(email), []);
        }

        var now = DateTimeOffset.UtcNow;
        var grants = await _grants.GetGrantsAsync(account.Email, cancellationToken);
        var dtos = grants
            .Select(g => new PurchaserGrantDto(
                CapabilityKey: g.CapabilityKey,
                Label: EntitlementLabels.LabelFor(g.CapabilityKey),
                ValidThrough: g.ValidThrough,
                Source: g.Source,
                Active: g.IsActiveAt(now)))
            .OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PurchaserLookupResult(true, account.Email, dtos);
    }

    /// <summary>
    /// Trims + lowercases an email to the same canonical form the account store uses, so
    /// the not-found echo matches how a grant would key. A whitespace-only input becomes
    /// empty. This is a display convenience only - the stores normalize internally too.
    /// </summary>
    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Validates a capability key against the fixed <see cref="EntitlementCatalog"/> (the
    /// four fixed keys plus a non-empty pack.&lt;id&gt;), returning the trimmed key or null
    /// if it is not a catalog key. This is the guardrail against inventing a rival catalog
    /// (the story's "do NOT invent a rival catalog"): only real catalog keys are grantable
    /// or revocable.
    /// </summary>
    private static string? NormalizeCapabilityKey(string? capabilityKey)
    {
        if (string.IsNullOrWhiteSpace(capabilityKey))
        {
            return null;
        }

        var key = capabilityKey.Trim();
        return key switch
        {
            EntitlementCatalog.LibraryFull => key,
            EntitlementCatalog.PlayRemote => key,
            EntitlementCatalog.PlayLargeGroup => key,
            EntitlementCatalog.AiOnDemand => key,
            // A pack key is valid only when it has a non-empty id after the "pack." prefix.
            _ when key.StartsWith(EntitlementCatalog.PackPrefix, StringComparison.Ordinal)
                && key.Length > EntitlementCatalog.PackPrefix.Length => key,
            _ => null,
        };
    }
}
