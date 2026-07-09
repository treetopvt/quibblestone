// ----------------------------------------------------------------------------
//  CloudGalleryController - the ONE server surface of the cloud-synced keepsake
//  gallery (keepsake-gallery/05, issue #154): a signed-in PURCHASER's private,
//  cloud-synced gallery that follows them across devices. A thin, dedicated
//  controller kept well away from GameHub.cs and the round lifecycle (the same
//  isolation precedent keepsake-gallery/04 set) - it never touches the hub, the
//  room registry, or the real-time backbone, and it imports NOTHING from
//  api/src/Rooms.
//
//  THE AUTH BOUNDARY (mirrored EXACTLY from EntitlementsController): the caller
//  proves who they are with the SAME short-lived purchaser credential
//  accounts-identity/03 mints on sign-in, resolved here via the shared
//  PurchaserCredentialService (the reused guard, NOT a second auth check). The
//  credential arrives as an Authorization: Bearer value (the SPA path) or the
//  HttpOnly cookie (same-site). No valid credential -> 401 on EVERY endpoint, so an
//  anonymous player never gets, sees, or implies a cloud gallery (AC-02). The
//  child-facing game / reveal flow NEVER touches a purchaser credential (AC-01, the
//  auth-boundary invariant) - syncing is an explicit purchaser-surface action here.
//
//  DEFAULT-UNLOCKED ENTITLEMENT, EVALUATED ONCE (AC-04): whether cloud sync is
//  available is decided EXACTLY ONCE, when the signed-in gallery surface is entered
//  (the GET), by reading the billing-entitlements/01 seam
//  (IEntitlementService.EvaluateForSession) for the dedicated capability key
//  gallery.cloudSync. It ships DEFAULT-UNLOCKED (added to the default-unlocked
//  baseline alongside the ai.* keys), so the effective gate is "is this a signed-in
//  purchaser" (the 401 above) and the entitlement check is always true today - but
//  it is read against the catalog key so real gating can later flip it to a stored
//  grant with NO consumer refactor. The check is NOT re-run on save / delete: the
//  entitlement is a session-level decision, not a per-item one (AC-04).
//
//  NO PII (AC-05): only the already-vetted in-session nickname(s) (the byline) and
//  the already-filtered story are ever stored. The purchaser identity lives ONLY in
//  the owner key, which since accounts-identity/05 (#195) is the account's STABLE id
//  (account.Id.ToString(), a random GUID) - never a raw email / real name on a tale a
//  child might see, and no longer a hash of the (mutable) email (so an email change
//  never orphans a purchaser's own gallery). There is no new free-text entry point:
//  content is re-vetted, not re-authored.
//
//  CHILD SAFETY, SERVER-SIDE RE-VET (AC-05, mirrored from PublishedTalesController):
//  on SAVE, EVERY non-empty part (coral player-words AND "literal" template runs)
//  plus the byline is re-run through the authoritative IContentSafetyFilter; if ANY
//  fails, the WHOLE save is rejected (400). The client's word/literal classification
//  is NOT trusted. Length / count caps mirror the publish caps. Empty coral slots
//  are skipped.
//
//  ANTI-ABUSE: the SAVE (write) endpoint is rate limited per client IP
//  ([EnableRateLimiting], CloudGalleryRateLimit - registered in Program.cs) so a
//  compromised / scripted credential cannot flood the store. Reads and deletes are
//  not limited.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.CloudGallery;

/// <summary>One part of a save request body: literal template text or a coral player-word.</summary>
/// <param name="IsWord">True for a player-supplied coral word (re-vetted), false for literal template text.</param>
/// <param name="Text">The part's text. May be null - treated as empty.</param>
public sealed record CloudTalePartRequest(bool IsWord, string? Text);

/// <summary>
/// Request body for POST /api/account/gallery (sync a tale to the cloud). The
/// client sends the ALREADY-ASSEMBLED story as ordered parts plus the byline of
/// in-session nicknames; the server re-vets every part + the byline, mints a tale
/// id, keys it off the signed-in purchaser, and stores it. No PII is accepted or
/// stored beyond the byline nickname(s) (AC-05).
/// </summary>
/// <param name="Title">The tale title (length-capped).</param>
/// <param name="Parts">The ordered body parts (literal text + coral player-words).</param>
/// <param name="BylineNames">The joined in-session nicknames (e.g. "Sam, Mia &amp; Bo"); may be null/empty.</param>
public sealed record SaveCloudTaleRequest(
    string? Title,
    IReadOnlyList<CloudTalePartRequest>? Parts,
    string? BylineNames);

/// <summary>One tale in the gallery-list response: the client renders / searches / sorts over these.</summary>
/// <param name="TaleId">The tale's id (used to delete it).</param>
/// <param name="Title">The tale title.</param>
/// <param name="Parts">The ordered body parts (literal text + coral player-words).</param>
/// <param name="BylineNames">The "carved by" byline nickname(s); may be empty.</param>
/// <param name="CreatedUtc">When the tale was synced (for date sort / display).</param>
public sealed record CloudTaleView(
    string TaleId,
    string Title,
    IReadOnlyList<CloudTalePartRequest> Parts,
    string BylineNames,
    DateTimeOffset CreatedUtc);

/// <summary>Response for GET /api/account/gallery: the signed-in purchaser's cloud tales.</summary>
public sealed record CloudGalleryResult(IReadOnlyList<CloudTaleView> Tales);

/// <summary>Response for POST /api/account/gallery: the minted id of the newly synced tale.</summary>
public sealed record SavedCloudTaleResult(string TaleId);

[ApiController]
[Route("api/account/gallery")]
public sealed class CloudGalleryController : ControllerBase
{
    // Anti-abuse caps for the save endpoint. Mirror PublishedTalesController's
    // constants so the two keepsake surfaces enforce identical, generous-but-bounded
    // limits (a real family tale fits; a payload built to bloat storage does not).
    private const int MaxTitleLength = 200;
    private const int MaxPartTextLength = 500;
    private const int MaxPartsCount = 400;
    private const int MaxBylineLength = 300;
    // Bound the TOTAL retained part text so the serialized PartsJson stays well under
    // Azure Table Storage's ~32K-char string-property limit. The per-part x per-count
    // caps alone (400 x 500) could otherwise serialize past it and throw a 500 on save
    // (Copilot review). A real filled-in tale is a few hundred chars; 16000 is generous.
    private const int MaxTotalPartsTextLength = 16000;

    private readonly PurchaserCredentialService _credential;
    private readonly IAccountStore _accounts;
    private readonly IEntitlementService _entitlements;
    private readonly ICloudGalleryStore _store;
    private readonly IContentSafetyFilter _safety;

    public CloudGalleryController(
        PurchaserCredentialService credential,
        IAccountStore accounts,
        IEntitlementService entitlements,
        ICloudGalleryStore store,
        IContentSafetyFilter safety)
    {
        _credential = credential;
        _accounts = accounts;
        _entitlements = entitlements;
        _store = store;
        _safety = safety;
    }

    /// <summary>
    /// GET /api/account/gallery -> the signed-in purchaser's cloud tales. 401 when
    /// not signed in (AC-02). This is the SINGLE entitlement evaluation point (AC-04):
    /// EvaluateForSession is read ONCE here for gallery.cloudSync; a locked capability
    /// 403s (default-unlocked makes this always pass today, but the seam is preserved).
    /// Search / filter / sort run client-side over the returned bounded set (AC-03).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        if (email is null)
        {
            // Not signed in / expired / tampered - no cloud gallery for an anonymous
            // visitor (AC-02).
            return Unauthorized();
        }

        // Resolve the canonical account (the SAME identity billing-01's gate reads).
        // No account -> nothing to show; a friendly empty gallery, never an error.
        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            return Ok(new CloudGalleryResult([]));
        }

        // AC-04: gallery.cloudSync must be unlocked for this account. Default-unlocked
        // makes it true today; the check preserves the seam so real gating flips a
        // stored grant with no consumer refactor. Applied here AND on every write op
        // (see CloudSyncLockedAsync) so a future gating flip is enforced uniformly,
        // never bypassable by POSTing/DELETEing directly (Copilot review). It is a
        // single cheap capability read per op, never a per-item loop.
        if (await CloudSyncLockedAsync(account, cancellationToken) is { } locked)
        {
            return locked;
        }

        var ownerKey = account.Id.ToString();
        var tales = await _store.ListByOwnerAsync(ownerKey, cancellationToken);
        var view = tales
            .Select(t => new CloudTaleView(
                t.TaleId,
                t.Title,
                t.Parts.Select(p => new CloudTalePartRequest(p.IsWord, p.Text)).ToList(),
                t.BylineNames,
                t.CreatedUtc))
            .ToList();

        return Ok(new CloudGalleryResult(view));
    }

    /// <summary>
    /// POST /api/account/gallery -> { taleId }. Syncs an already-assembled,
    /// already-filtered tale to the signed-in purchaser's cloud gallery (AC-01).
    /// 401 when not signed in. Re-vets EVERY non-empty part + the byline server-side
    /// (AC-05), enforces length caps, skips empty coral slots, mints a tale id, keys
    /// it off the purchaser account, and stores it. Enforces the gallery.cloudSync
    /// entitlement too (AC-04) so a future gating flip cannot be bypassed by POSTing
    /// directly. Rate limited per IP.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(CloudGalleryRateLimit.PolicyName)]
    public async Task<IActionResult> Save([FromBody] SaveCloudTaleRequest? request, CancellationToken cancellationToken)
    {
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        if (email is null)
        {
            return Unauthorized();
        }

        // Resolve the canonical account so the tale keys off account.Id exactly like
        // the gallery read does (the load-bearing key alignment - a save keyed off any
        // other value would silently list back empty). No account -> a valid credential
        // with no purchase behind it may not create cloud state.
        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        // Entitlement (AC-04): enforced on the write path too, so a future gating flip
        // cannot be bypassed by calling POST directly (Copilot review).
        if (await CloudSyncLockedAsync(account, cancellationToken) is { } locked)
        {
            return locked;
        }

        if (request is null)
        {
            return BadRequest(new { message = "A tale to sync is required." });
        }

        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return BadRequest(new { message = "A tale needs a title to sync." });
        }
        if (title.Length > MaxTitleLength)
        {
            return BadRequest(new { message = "That title is too long to sync." });
        }

        var requestParts = request.Parts ?? [];
        if (requestParts.Count == 0)
        {
            return BadRequest(new { message = "A tale needs some story to sync." });
        }
        if (requestParts.Count > MaxPartsCount)
        {
            return BadRequest(new { message = "That tale is too long to sync." });
        }

        var byline = (request.BylineNames ?? string.Empty).Trim();
        if (byline.Length > MaxBylineLength)
        {
            return BadRequest(new { message = "That byline is too long to sync." });
        }

        // SERVER-SIDE RE-VET (AC-05): re-run EVERY non-empty part - coral player-words
        // AND "literal" template runs - plus the byline through the authoritative
        // filter; reject the WHOLE save if any fails. The client's IsWord flag is NOT
        // trusted (same rationale as the publish path). Empty coral slots are skipped
        // (an unfilled blank renders as a gap). Rejected text is never echoed back.
        var parts = new List<CloudTalePart>(requestParts.Count);
        foreach (var part in requestParts)
        {
            var text = part.Text ?? string.Empty;
            if (text.Length > MaxPartTextLength)
            {
                return BadRequest(new { message = "Part of that tale is too long to sync." });
            }

            // Skip empty coral word slots so an empty coral part never reaches storage.
            if (part.IsWord && text.Trim().Length == 0)
            {
                continue;
            }

            // Re-vet any non-empty text regardless of word/literal. Empty literal text
            // (inter-word spacing / punctuation) has nothing to vet and is stored as-is
            // so the story reads correctly.
            if (text.Trim().Length > 0)
            {
                var verdict = await _safety.CheckAsync(text, cancellationToken);
                if (!verdict.IsAllowed)
                {
                    return BadRequest(new { message = "That tale cannot be synced - some content did not pass the family-safe check." });
                }
            }

            parts.Add(new CloudTalePart(IsWord: part.IsWord, Text: text));
        }

        if (parts.Count == 0)
        {
            return BadRequest(new { message = "A tale needs some story to sync." });
        }

        // Bound the TOTAL stored text so PartsJson stays under Azure Table's string-
        // property limit (the per-part x per-count caps alone could exceed it - 500).
        if (parts.Sum(p => p.Text.Length) > MaxTotalPartsTextLength)
        {
            return BadRequest(new { message = "That tale is too long to sync." });
        }

        if (byline.Length > 0)
        {
            var bylineVerdict = await _safety.CheckAsync(byline, cancellationToken);
            if (!bylineVerdict.IsAllowed)
            {
                return BadRequest(new { message = "That tale cannot be synced - some content did not pass the family-safe check." });
            }
        }

        var tale = new CloudTale(
            OwnerKey: account.Id.ToString(),
            TaleId: SlugGenerator.Generate(),
            Title: title,
            Parts: parts,
            BylineNames: byline,
            CreatedUtc: DateTimeOffset.UtcNow);

        await _store.SaveAsync(tale, cancellationToken);
        return Ok(new SavedCloudTaleResult(tale.TaleId));
    }

    /// <summary>
    /// DELETE /api/account/gallery/{taleId} -> 204. Removes one tale from the
    /// signed-in purchaser's cloud gallery (AC-06). 401 when not signed in.
    /// Idempotent and scoped to the owner: deleting an unknown / already-gone tale
    /// (or one that is not this owner's) still succeeds and never touches another
    /// owner's tale.
    /// </summary>
    [HttpDelete("{taleId}")]
    public async Task<IActionResult> Delete(string taleId, CancellationToken cancellationToken)
    {
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        if (email is null)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            // No cloud state exists for a credential with no account - nothing to
            // delete, but keep the response shape uniform (204, idempotent).
            return NoContent();
        }

        // Entitlement (AC-04): enforced on the write path too (Copilot review).
        if (await CloudSyncLockedAsync(account, cancellationToken) is { } locked)
        {
            return locked;
        }

        var ownerKey = account.Id.ToString();
        await _store.DeleteAsync(ownerKey, taleId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// DELETE /api/account/gallery -> 204. Revokes cloud sync: removes EVERY tale in
    /// the signed-in purchaser's cloud gallery within a bounded window (AC-06). 401
    /// when not signed in. Idempotent (revoking an empty gallery succeeds) and scoped
    /// to the owner - it never touches another purchaser's tales.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> RevokeAll(CancellationToken cancellationToken)
    {
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        if (email is null)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            return NoContent();
        }

        // Entitlement (AC-04): enforced on the write path too (Copilot review).
        if (await CloudSyncLockedAsync(account, cancellationToken) is { } locked)
        {
            return locked;
        }

        var ownerKey = account.Id.ToString();
        await _store.DeleteAllForOwnerAsync(ownerKey, cancellationToken);
        return NoContent();
    }

    // The credential: prefer the Authorization: Bearer value (the cross-origin path
    // the SPA holds from sign-in), fall back to the HttpOnly cookie (same-site
    // deployment). Mirrored EXACTLY from EntitlementsController (the reused guard).
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

    // The gallery.cloudSync entitlement gate (AC-04), applied by EVERY authenticated
    // gallery operation (read AND write) so a future gating flip is enforced uniformly
    // and cannot be bypassed by calling a write endpoint directly (Copilot review). A
    // single cheap capability read per op - never a per-item loop. Returns a 403 result
    // when the capability is locked, or null when it is unlocked (default-unlocked
    // today, so this is null in the current alpha).
    private async Task<IActionResult?> CloudSyncLockedAsync(Account account, CancellationToken cancellationToken)
    {
        var session = await _entitlements.EvaluateForSession(account.Email, cancellationToken);
        return session.IsUnlocked(EntitlementCatalog.GalleryCloudSync)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Cloud gallery is not available on this account." });
    }
}
