// ----------------------------------------------------------------------------
//  VaultController - the ONE server surface of the anonymous, server-side
//  keepsake vault (keepsake-vault/01, ADR 0003 Decision 2 / Layer 2, issue #196):
//  every completed reveal auto-saves here, keyed by a device-held random vault id,
//  so "where are my saved stories?" has a durable answer beyond the device-local
//  IndexedDB gallery. A thin, dedicated controller kept well away from GameHub.cs
//  and the round lifecycle (the keepsake-gallery precedent) - it never touches the
//  hub, the room registry, or the real-time backbone, and imports NOTHING from
//  api/src/Rooms.
//
//  THE VAULT ID IS A BEARER CREDENTIAL, CARRIED IN A HEADER (AC-01/AC-02, ADR 0003
//  "Handles are secrets"): every vault-id-bearing call reads the id from the
//  X-Vault-Id request HEADER - NEVER a URL path segment or query parameter. There
//  is no {vaultId} route anywhere in this controller. Rationale:
//  PiiScrubbingTelemetryInitializer strips only the query string, so a path
//  segment would leak the credential to App Insights, access logs, Referer, and
//  browser history. The id also never appears in an exception message (the
//  scrubber cannot clean message text). No further auth: a vault id IS the
//  credential (anonymous by construction, exactly like a room join code) - anyone
//  holding it can read/write it, subject to the AC-01 length/format floor
//  (VaultId.IsWellFormed) and the AC-06 per-IP rate limits.
//
//  CHILD SAFETY, SERVER-SIDE RE-VET (AC-04, mirrored from CloudGalleryController /
//  the publish path): on SAVE, EVERY non-empty part (coral player-words AND
//  "literal" template runs) plus the byline is re-run through the authoritative
//  IContentSafetyFilter; if ANY fails, the WHOLE save is rejected (400). The
//  client's word/literal classification is NOT trusted. Length / count caps mirror
//  the sibling surfaces. Empty coral slots are skipped.
//
//  CreatedUtc IS SERVER-STAMPED (AC-02): DateTimeOffset.UtcNow at write time,
//  never accepted from the client - the save request carries no CreatedUtc field
//  (mirrors SaveCloudTaleRequest). This endpoint is anonymous and abusable and the
//  TTL (AC-03) keys off CreatedUtc, so a client-supplied timestamp would be
//  directly spoofable to defeat expiry.
//
//  ANTI-ABUSE: BOTH the write (POST) and the read (GET) endpoints are per-IP rate
//  limited (AC-06, VaultRateLimit) - the read too, because it is an anonymous,
//  bearer-gated partition list a scripted caller could otherwise scrape. The
//  per-vault MaxTalesPerVault cap (AC-07) bounds storage growth independent of the
//  per-IP limiter (which IP rotation alone defeats).
//
//  NO PII (AC-04): only the already-vetted in-session nickname(s) (the byline) and
//  the already-filtered story are ever stored; the vault id is a random handle,
//  never joined to any identity. There is no new free-text entry point - content
//  is re-vetted, not re-authored.
//
//  CLAIM + RECOVERY (keepsake-vault/03, issue #230): four more endpoints turn a
//  vault durable and recoverable. POST /api/vault/claim attaches the SIGNED-IN
//  FAMILY ACCOUNT (the family credential reused exactly, mirroring
//  CloudGalleryController's PurchaserCredentialService pattern - NOT a second auth
//  scheme) so the vault's tales stop expiring (AC-05) and follow the account across
//  devices (AC-01). A human-friendly recovery claim code (ClaimCodeGenerator, a
//  bearer secret carried in the REQUEST BODY on redemption, never a route/query)
//  lets a family recover the SAME vault onto a new device WITHOUT an account
//  (AC-02): POST /api/vault/claim-code/redeem aliases the calling device to the
//  claimed vault; GET /api/vault/claim surfaces the live code + expiry (auto-rotated
//  on open, AC-07); POST /api/vault/claim-code/regenerate is the account-free
//  explicit revoke. Redemption carries THREE anti-brute-force controls (AC-03): the
//  per-IP limiter, the IP-agnostic global ceiling (ClaimRedemptionCeiling), and the
//  per-code failed-attempt burn (in the store). A claimed vault is tied to the
//  FAMILY only, never a kid profile (AC-04, ADR 0003 Decision 1). The claim code and
//  the AccountId are bearer/account-plane secrets - never logged, never in an
//  exception message, and the response never carries the AccountId (AC-06).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Vault;

/// <summary>One part of a save request body: literal template text or a coral player-word.</summary>
/// <param name="IsWord">True for a player-supplied coral word (re-vetted), false for literal template text.</param>
/// <param name="Text">The part's text. May be null - treated as empty.</param>
public sealed record VaultTalePartRequest(bool IsWord, string? Text);

/// <summary>
/// Request body for POST /api/vault/tales (auto-save a completed reveal). The
/// client sends the ALREADY-ASSEMBLED story as ordered parts plus the byline of
/// in-session nicknames; the vault id travels in the X-Vault-Id HEADER, never here.
/// The server re-vets every part + the byline (AC-04), mints a tale id, and
/// server-stamps CreatedUtc (AC-02). Deliberately carries NO CreatedUtc field
/// (mirrors SaveCloudTaleRequest) - a client-supplied timestamp is never accepted.
/// </summary>
/// <param name="Title">The tale title (length-capped).</param>
/// <param name="Parts">The ordered body parts (literal text + coral player-words).</param>
/// <param name="BylineNames">The joined in-session nicknames (e.g. "Sam, Mia &amp; Bo"); may be null/empty.</param>
public sealed record SaveVaultTaleRequest(
    string? Title,
    IReadOnlyList<VaultTalePartRequest>? Parts,
    string? BylineNames);

/// <summary>One tale in the vault-list response (consumed by keepsake-vault/02).</summary>
/// <param name="TaleId">The tale's id.</param>
/// <param name="Title">The tale title.</param>
/// <param name="Parts">The ordered body parts (literal text + coral player-words).</param>
/// <param name="BylineNames">The "carved by" byline nickname(s); may be empty.</param>
/// <param name="CreatedUtc">When the tale was saved (server-stamped; for date sort / display).</param>
public sealed record VaultTaleView(
    string TaleId,
    string Title,
    IReadOnlyList<VaultTalePartRequest> Parts,
    string BylineNames,
    DateTimeOffset CreatedUtc);

/// <summary>Response for GET /api/vault/tales: the vault's non-expired tales.</summary>
public sealed record VaultListResult(IReadOnlyList<VaultTaleView> Tales);

/// <summary>Response for POST /api/vault/tales: the minted id of the newly saved tale.</summary>
public sealed record SavedVaultTaleResult(string TaleId);

/// <summary>Response for POST /api/vault/mint: a fresh server-minted vault id (AC-01 fallback).</summary>
public sealed record MintVaultIdResult(string VaultId);

/// <summary>
/// The live claim-code view returned by POST /api/vault/claim, POST
/// /api/vault/claim-code/regenerate, and GET /api/vault/claim (keepsake-vault/03).
/// Carries ONLY the human-facing recovery code (grouped for display, AC-02) and its
/// expiry (AC-07) - never the AccountId or any PII (AC-06). A device holding the
/// vault id shows this so the family can recover the vault onto a new device.
/// </summary>
/// <param name="ClaimCode">The current active recovery code, grouped for display (e.g. "K5Q-2NX-8CP").</param>
/// <param name="ClaimCodeExpiresUtc">When the current code stops working (AC-07); a fresh one is auto-minted on the next gallery open.</param>
public sealed record VaultClaimCodeView(string ClaimCode, DateTimeOffset ClaimCodeExpiresUtc);

/// <summary>
/// Response for GET /api/vault/claim (keepsake-vault/03): whether the vault is claimed
/// and, if so, its live recovery code + expiry (AC-02/AC-07). An unclaimed vault
/// returns <see cref="Claimed"/> false and a null <see cref="Code"/> - the gallery
/// then shows the "claim this vault" affordance instead of a code. Carries no
/// AccountId / PII (AC-06).
/// </summary>
/// <param name="Claimed">True when the vault has been claimed into a family account (AC-01).</param>
/// <param name="Code">The live recovery code view when claimed; null otherwise.</param>
public sealed record VaultClaimStatusResult(bool Claimed, VaultClaimCodeView? Code);

/// <summary>
/// Request body for POST /api/vault/claim-code/redeem (keepsake-vault/03, AC-02): the
/// human-typed recovery code, carried in the BODY - never a route segment or query
/// string (a claim code is a bearer secret, ADR 0003 "Handles are secrets"). The
/// calling device's own vault id travels in the X-Vault-Id header, never here.
/// </summary>
/// <param name="Code">The recovery code as typed by a human (any grouping / case); normalized server-side.</param>
public sealed record RedeemClaimCodeRequest(string? Code);

/// <summary>
/// Response for POST /api/vault/claim-code/redeem (keepsake-vault/03). Deliberately
/// minimal (AC-06): only whether the calling device was attached to a vault. On
/// success the device re-fetches its tales under its OWN (now-aliased) vault id - it
/// never learns the canonical vault id. A uniform 200 either way so redemption is not
/// an enumeration oracle.
/// </summary>
/// <param name="Redeemed">True when the code was valid and the device is now aliased to the claimed vault (AC-02).</param>
public sealed record RedeemClaimCodeResult(bool Redeemed);

[ApiController]
[Route("api/vault")]
public sealed class VaultController : ControllerBase
{
    /// <summary>
    /// The request header carrying the vault-id bearer credential on every
    /// vault-id-bearing call (AC-02). Never a route segment or query parameter.
    /// </summary>
    public const string VaultIdHeader = "X-Vault-Id";

    // Anti-abuse caps for the save endpoint. Mirror CloudGalleryController's
    // constants so the keepsake surfaces enforce identical, generous-but-bounded
    // limits (a real family tale fits; a payload built to bloat storage does not).
    private const int MaxTitleLength = 200;
    private const int MaxPartTextLength = 500;
    private const int MaxPartsCount = 400;
    private const int MaxBylineLength = 300;
    // Bound the TOTAL retained part text so the serialized PartsJson stays well
    // under Azure Table Storage's ~32K-char string-property limit (the per-part x
    // per-count caps alone could otherwise exceed it). A real filled-in tale is a
    // few hundred chars; 16000 is generous.
    private const int MaxTotalPartsTextLength = 16000;

    private readonly IVaultStore _store;
    private readonly IContentSafetyFilter _safety;
    private readonly PurchaserCredentialService _credential;
    private readonly IAccountStore _accounts;
    private readonly ClaimRedemptionCeiling _redemptionCeiling;

    public VaultController(
        IVaultStore store,
        IContentSafetyFilter safety,
        PurchaserCredentialService credential,
        IAccountStore accounts,
        ClaimRedemptionCeiling redemptionCeiling)
    {
        _store = store;
        _safety = safety;
        _credential = credential;
        _accounts = accounts;
        _redemptionCeiling = redemptionCeiling;
    }

    /// <summary>
    /// POST /api/vault/mint -> { vaultId }. The AC-01 fallback: a device without
    /// crypto.randomUUID calls this (no body) to obtain a fresh, server-minted,
    /// RandomNumberGenerator-backed vault id instead of forging a weak one locally.
    /// Unauthenticated by design (a vault id is anonymous) and cheap / stateless -
    /// it mints and returns, storing nothing.
    /// </summary>
    [HttpPost("mint")]
    public IActionResult Mint()
    {
        return Ok(new MintVaultIdResult(VaultId.Mint()));
    }

    /// <summary>
    /// GET /api/vault/tales -> the vault's non-expired tales (consumed by
    /// keepsake-vault/02). The vault id comes from the X-Vault-Id HEADER (AC-02);
    /// a missing / malformed id is 400 (AC-01). Rate limited per IP (AC-06). Expiry
    /// (AC-03) is applied in the store's ListAsync, so this returns only live tales.
    /// </summary>
    [HttpGet("tales")]
    [EnableRateLimiting(VaultRateLimit.ReadPolicyName)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var vaultId = ReadVaultId();
        if (vaultId is null)
        {
            return BadRequest(new { message = "A valid vault id is required." });
        }

        var tales = await _store.ListAsync(vaultId, cancellationToken);
        var view = tales
            .Select(t => new VaultTaleView(
                t.TaleId,
                t.Title,
                t.Parts.Select(p => new VaultTalePartRequest(p.IsWord, p.Text)).ToList(),
                t.BylineNames,
                t.CreatedUtc))
            .ToList();

        return Ok(new VaultListResult(view));
    }

    /// <summary>
    /// POST /api/vault/tales -> { taleId }. Auto-saves one already-assembled,
    /// already-filtered completed reveal to the vault (AC-02). The vault id comes
    /// from the X-Vault-Id HEADER (AC-02); a missing / malformed id is 400 (AC-01).
    /// Re-vets EVERY non-empty part + the byline server-side (AC-04), enforces
    /// length caps, skips empty coral slots, mints a tale id, and SERVER-STAMPS
    /// CreatedUtc (AC-02). A vault at its cap is rejected (AC-07). Rate limited per
    /// IP (AC-06). The client calls this fire-and-forget, so any non-2xx here simply
    /// fails silently on the reveal screen (AC-02's never-block posture).
    /// </summary>
    [HttpPost("tales")]
    [EnableRateLimiting(VaultRateLimit.SavePolicyName)]
    public async Task<IActionResult> Save([FromBody] SaveVaultTaleRequest? request, CancellationToken cancellationToken)
    {
        var vaultId = ReadVaultId();
        if (vaultId is null)
        {
            return BadRequest(new { message = "A valid vault id is required." });
        }

        if (request is null)
        {
            return BadRequest(new { message = "A tale to save is required." });
        }

        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return BadRequest(new { message = "A tale needs a title to save." });
        }
        if (title.Length > MaxTitleLength)
        {
            return BadRequest(new { message = "That title is too long to save." });
        }

        var requestParts = request.Parts ?? [];
        if (requestParts.Count == 0)
        {
            return BadRequest(new { message = "A tale needs some story to save." });
        }
        if (requestParts.Count > MaxPartsCount)
        {
            return BadRequest(new { message = "That tale is too long to save." });
        }

        var byline = (request.BylineNames ?? string.Empty).Trim();
        if (byline.Length > MaxBylineLength)
        {
            return BadRequest(new { message = "That byline is too long to save." });
        }

        // SERVER-SIDE RE-VET (AC-04): re-run EVERY non-empty part - coral player-
        // words AND "literal" template runs - plus the byline through the
        // authoritative filter; reject the WHOLE save if any fails. The client's
        // IsWord flag is NOT trusted. Empty coral slots are skipped (an unfilled
        // blank renders as a gap). Rejected text is never echoed back.
        var parts = new List<VaultTalePart>(requestParts.Count);
        foreach (var part in requestParts)
        {
            var text = part.Text ?? string.Empty;
            if (text.Length > MaxPartTextLength)
            {
                return BadRequest(new { message = "Part of that tale is too long to save." });
            }

            // Skip empty coral word slots so an empty coral part never reaches storage.
            if (part.IsWord && text.Trim().Length == 0)
            {
                continue;
            }

            // Re-vet any non-empty text regardless of word/literal. Empty literal
            // text (inter-word spacing / punctuation) has nothing to vet and is
            // stored as-is so the story reads correctly.
            if (text.Trim().Length > 0)
            {
                var verdict = await _safety.CheckAsync(text, cancellationToken);
                if (!verdict.IsAllowed)
                {
                    return BadRequest(new { message = "That tale cannot be saved - some content did not pass the family-safe check." });
                }
            }

            parts.Add(new VaultTalePart(IsWord: part.IsWord, Text: text));
        }

        if (parts.Count == 0)
        {
            return BadRequest(new { message = "A tale needs some story to save." });
        }

        // Bound the TOTAL stored text so PartsJson stays under Azure Table's string-
        // property limit (the per-part x per-count caps alone could exceed it).
        if (parts.Sum(p => p.Text.Length) > MaxTotalPartsTextLength)
        {
            return BadRequest(new { message = "That tale is too long to save." });
        }

        if (byline.Length > 0)
        {
            var bylineVerdict = await _safety.CheckAsync(byline, cancellationToken);
            if (!bylineVerdict.IsAllowed)
            {
                return BadRequest(new { message = "That tale cannot be saved - some content did not pass the family-safe check." });
            }
        }

        var tale = new VaultTale(
            VaultId: vaultId,
            TaleId: SlugGenerator.Generate(),
            Title: title,
            Parts: parts,
            BylineNames: byline,
            // AC-02: ALWAYS server-stamped, never from the client body.
            CreatedUtc: DateTimeOffset.UtcNow);

        var outcome = await _store.SaveAsync(tale, cancellationToken);
        if (outcome == VaultSaveOutcome.RejectedCapExceeded)
        {
            // AC-07: the vault is full. 409 Conflict - the client's fire-and-forget
            // call simply fails silently (a family never notices at organic volume).
            return Conflict(new { message = "This vault is full." });
        }

        return Ok(new SavedVaultTaleResult(tale.TaleId));
    }

    /// <summary>
    /// POST /api/vault/claim -> { claimCode, claimCodeExpiresUtc }. Claims the vault
    /// (X-Vault-Id header) into the SIGNED-IN FAMILY ACCOUNT (keepsake-vault/03, AC-01):
    /// requires the family credential (mirrors CloudGalleryController's reuse of
    /// PurchaserCredentialService - NOT a second auth scheme), resolves it to the
    /// stable, non-PII AccountId, and associates the vault with it so its tales become
    /// permanent (no TTL, AC-05) and reachable from any device signed into that
    /// account. Mints and returns a fresh recovery claim code (AC-02). 401 when not
    /// signed in / no account behind the credential; 400 on a missing / malformed
    /// vault id. The claimed vault is tied to the FAMILY only, never a kid profile
    /// (AC-04).
    /// </summary>
    [HttpPost("claim")]
    public async Task<IActionResult> Claim(CancellationToken cancellationToken)
    {
        var vaultId = ReadVaultId();
        if (vaultId is null)
        {
            return BadRequest(new { message = "A valid vault id is required." });
        }

        // Reuse the family credential exactly (AC-01): resolve it to an email, then to
        // the canonical account so the claim keys off the STABLE AccountId GUID, never
        // an email (accounts-identity/05). No valid credential / no account -> 401, so
        // an anonymous player can never claim a vault into an account.
        var email = _credential.ResolvePurchaserEmail(ReadCredential());
        if (email is null)
        {
            return Unauthorized();
        }

        var account = await _accounts.GetByIdentityAsync(email, cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        var claim = await _store.ClaimAsync(vaultId, account.Id, cancellationToken);
        return Ok(ToCodeView(claim));
    }

    /// <summary>
    /// GET /api/vault/claim -> { claimed, code? }. The gallery reads this (X-Vault-Id
    /// header) to surface the live recovery code + its expiry when the vault is claimed
    /// (AC-02/AC-07), or { claimed: false } when it is not (the gallery then shows the
    /// "claim this vault" affordance). Any device holding / aliased to the vault id may
    /// read it - no account required. AUTO-ROTATION (AC-07): an expired / burned code
    /// is refreshed here so the family always sees a working code. Rate limited per IP
    /// (the read policy). Carries no AccountId / PII (AC-06).
    /// </summary>
    [HttpGet("claim")]
    [EnableRateLimiting(VaultRateLimit.ReadPolicyName)]
    public async Task<IActionResult> GetClaim(CancellationToken cancellationToken)
    {
        var vaultId = ReadVaultId();
        if (vaultId is null)
        {
            return BadRequest(new { message = "A valid vault id is required." });
        }

        var claim = await _store.GetClaimAsync(vaultId, cancellationToken);
        return Ok(claim is null
            ? new VaultClaimStatusResult(Claimed: false, Code: null)
            : new VaultClaimStatusResult(Claimed: true, Code: ToCodeView(claim)));
    }

    /// <summary>
    /// POST /api/vault/claim-code/regenerate -> { claimCode, claimCodeExpiresUtc }. The
    /// account-free explicit revoke / regenerate (keepsake-vault/03, AC-07): any device
    /// already holding (or aliased to) the vault id (X-Vault-Id header) can immediately
    /// invalidate the current code and mint a fresh one - NO account required. 404 when
    /// the vault has never been claimed (there is no code to regenerate); 400 on a
    /// missing / malformed vault id. Rate limited per IP (the read policy - a rare,
    /// deliberate action).
    /// </summary>
    [HttpPost("claim-code/regenerate")]
    [EnableRateLimiting(VaultRateLimit.ReadPolicyName)]
    public async Task<IActionResult> RegenerateClaimCode(CancellationToken cancellationToken)
    {
        var vaultId = ReadVaultId();
        if (vaultId is null)
        {
            return BadRequest(new { message = "A valid vault id is required." });
        }

        var claim = await _store.RegenerateClaimCodeAsync(vaultId, cancellationToken);
        if (claim is null)
        {
            return NotFound(new { message = "This vault has not been claimed, so it has no recovery code." });
        }

        return Ok(ToCodeView(claim));
    }

    /// <summary>
    /// POST /api/vault/claim-code/redeem -> { redeemed }. Recovers a claimed vault onto
    /// this NEW device (keepsake-vault/03, AC-02): the recovery code is in the request
    /// BODY (never a route / query - a bearer secret), the calling device's own vault
    /// id is in the X-Vault-Id header. A valid, unexpired code aliases this device to
    /// the claimed vault so a later GET /api/vault/tales under this device's OWN id
    /// returns the same tales - WITHOUT an account and without the device ever learning
    /// the canonical vault id (AC-06). NOT single-use (AC-07). Protected by all THREE
    /// anti-brute-force controls (AC-03): the per-IP limiter ([EnableRateLimiting]),
    /// the IP-agnostic global ceiling (checked first here), and the per-code
    /// failed-attempt burn (in the store). A uniform 200 { redeemed } either way so it
    /// is not an enumeration oracle.
    /// </summary>
    [HttpPost("claim-code/redeem")]
    [EnableRateLimiting(VaultRateLimit.RedeemPolicyName)]
    public async Task<IActionResult> RedeemClaimCode([FromBody] RedeemClaimCodeRequest? request, CancellationToken cancellationToken)
    {
        // Validate the calling vault id FIRST - a malformed request is a 400 that must
        // NOT consume the global-ceiling budget, or an attacker could exhaust AC-03.2's
        // shared budget with cheap malformed requests and block legitimate recovery.
        var callingVaultId = ReadVaultId();
        if (callingVaultId is null)
        {
            return BadRequest(new { message = "A valid vault id is required." });
        }

        // AC-03.2: the IP-agnostic global ceiling - the control that bounds a
        // distributed, IP-rotating brute force the per-IP limiter cannot. Consumed only
        // by a well-formed redemption attempt, and BEFORE any store work so a flood
        // never reaches the reverse-index lookup.
        if (!_redemptionCeiling.TryAcquire())
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Too many recovery attempts right now - please try again in a minute." });
        }

        // Normalize the human-typed code to canonical form (tolerating hyphens /
        // spaces / case). A malformed shape is a failed redemption, not a 400 - a
        // uniform outcome keeps the endpoint from being a code-shape oracle.
        var code = ClaimCodeGenerator.Normalize(request?.Code);
        if (code is null)
        {
            return Ok(new RedeemClaimCodeResult(Redeemed: false));
        }

        var outcome = await _store.RedeemClaimCodeAsync(code, callingVaultId, cancellationToken);
        return Ok(new RedeemClaimCodeResult(Redeemed: outcome == VaultRedeemOutcome.Redeemed));
    }

    // Map a claim to its human-facing code view: the code is GROUPED for display
    // (AC-02) and only the code + expiry cross the wire - never the AccountId (AC-06).
    private static VaultClaimCodeView ToCodeView(VaultClaim claim) =>
        new(ClaimCodeGenerator.Format(claim.ClaimCode), claim.ClaimCodeExpiresUtc);

    // Read + validate the vault id from the X-Vault-Id HEADER (never a route
    // segment, AC-02). Returns the id when it meets the AC-01 length/format floor
    // (VaultId.IsWellFormed), or null when the header is missing / malformed / weak
    // so the caller returns 400. The id is never logged or put in an exception
    // message (it is a bearer secret the telemetry scrubber cannot clean from text).
    private string? ReadVaultId()
    {
        var value = Request.Headers[VaultIdHeader].ToString();
        return VaultId.IsWellFormed(value) ? value : null;
    }

    // The family credential: prefer the Authorization: Bearer value (the cross-origin
    // path the SPA holds from sign-in), fall back to the HttpOnly cookie (same-site
    // deployment). Mirrored from CloudGalleryController (the reused guard, NOT a second
    // auth scheme) - the SAME credential accounts-identity/07's family sign-in mints.
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
