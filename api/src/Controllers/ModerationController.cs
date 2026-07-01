// ----------------------------------------------------------------------------
//  ModerationController - the REST seam onto the authoritative safety filter
//  (game-modes/02, README section 6 / CLAUDE.md section 5).
//
//  child-safety/01 already owns the one server-side gate every free-text
//  surface must route through: IContentSafetyFilter, registered once in DI
//  (Program.cs) so every caller resolves the SAME instance. GameHub already
//  reaches it over SignalR (see GameHub's nickname-join check); this
//  controller is the REST equivalent that anticipated header comment on
//  IContentSafetyFilter calls out - "the hub and any future REST controller
//  resolve the same instance". It exists because single-player play has no
//  SignalR round-trip to lean on for checking a typed word before recording
//  it, so the web client (web/src/safety/checkWord.ts) calls this endpoint
//  directly.
//
//  This controller has NO safety logic of its own - it only shapes the
//  request/response around the injected IContentSafetyFilter, exactly like
//  HealthController demonstrates the bare controller pattern for the walking
//  skeleton. Async all the way, nullable respected, no secrets, no PII.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Controllers;

/// <summary>Request body for POST /moderation/check: the candidate free text to vet.</summary>
/// <param name="Text">The raw player-submitted text. May be null or empty - the filter handles that.</param>
public sealed record ModerationCheckRequest(string? Text);

[ApiController]
[Route("moderation")]
public sealed class ModerationController : ControllerBase
{
    private readonly IContentSafetyFilter _safety;

    public ModerationController(IContentSafetyFilter safety)
    {
        _safety = safety;
    }

    /// <summary>
    /// POST /moderation/check -> { allowed, message }. Vets a single piece of
    /// candidate free text (a blank answer) against the authoritative safety
    /// filter before it is recorded or shown to anyone. This is the same gate
    /// GameHub uses for nicknames - just reached over REST instead of a hub
    /// method, for surfaces (single-player) with no SignalR round-trip.
    /// </summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] ModerationCheckRequest request, CancellationToken cancellationToken)
    {
        var verdict = await _safety.CheckAsync(request.Text, cancellationToken);
        return Ok(new { allowed = verdict.IsAllowed, message = verdict.Message });
    }
}
