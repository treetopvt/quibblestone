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
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    public VaultController(IVaultStore store, IContentSafetyFilter safety)
    {
        _store = store;
        _safety = safety;
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
}
