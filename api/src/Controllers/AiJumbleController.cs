// ----------------------------------------------------------------------------
//  AiJumbleController - the REST seam onto the AI word-bank jumble
//  (ai-on-demand-generation/05). The FIRST real consumer of the AI cost gate:
//  the web "Fresh runes" button (game-modes/07) POSTs here to PREFER AI-generated
//  words, and falls back to its free deterministic reshuffle whenever this
//  reports fellBack.
//
//  WHY REST (mirrors ModerationController + the throwaway probe): the jumble is a
//  per-player, request/response action during word collection - not a room
//  broadcast - and BOTH play modes reach it the same way. Solo play has no
//  SignalR round-trip at all (it is a single browser tab), so a hub method could
//  not serve it; a REST endpoint serves solo AND group uniformly. It also carries
//  the AI per-IP rate-limit policy (ai-cost-gate/03, AC-08) - the same guard the
//  probe first exercised - so a client cannot spam AI calls from one IP.
//
//  THE ANONYMOUS QUOTA KEY (README section 6, no PII):
//    - GROUP play: the client holds only the join code, never the server-side
//      Room.InstanceId. So the server resolves the live room from the code and
//      keys the gate's quota + attribution on its InstanceId - the exact
//      anonymous per-session key the gate is designed for (ai-cost-gate/03/04).
//    - SOLO play: there is no room. The client supplies its own anonymous,
//      device-local session id (the same id it already uses for telemetry), and
//      the gate keys on that. Never a nickname / account / PII either way.
//  If neither yields a usable key, the request falls back cleanly (no AI call).
//
//  NO AI LOGIC HERE: this controller only shapes the request/response and picks
//  the anonymous key; the generation + parsing is JumbleWordGenerator's, and the
//  quota / breaker / attribution / moderation are the gate's. With no AI
//  configured the gate's no-op transport makes every call fall back, so the
//  endpoint is always safe to call and simply degrades to the free reshuffle.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QuibbleStone.Api.Ai.Jumble;
using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Controllers;

/// <summary>
/// Request body for POST /api/ai/jumble. Carries the blank's category, the
/// family-safe toggle, the already-shown words to avoid, and the anonymous
/// session handle (a group join code and/or a solo device session id).
/// </summary>
/// <param name="Category">The blank's category label (e.g. "noun"). A controlled value, not player free text.</param>
/// <param name="FamilySafe">The round's family-safe toggle - tightens generation + moderation.</param>
/// <param name="Avoid">Words already shown for this blank (already vetted) - the model is asked to skip them. May be null.</param>
/// <param name="RoomCode">The group join code (resolved to the anonymous Room.InstanceId server-side). Null/empty for solo.</param>
/// <param name="SessionId">The solo client's anonymous, device-local session id (used only when no live room resolves). Null/empty for group.</param>
public sealed record AiJumbleRequest(
    string? Category,
    bool FamilySafe,
    IReadOnlyList<string>? Avoid,
    string? RoomCode,
    string? SessionId);

[ApiController]
[Route("api/ai/jumble")]
[EnableRateLimiting(Program.AiPerIpRateLimitPolicy)]
public sealed class AiJumbleController : ControllerBase
{
    // The longest a category label may sensibly be, and the pattern it must match
    // (letters + hyphens only). A defensive check on a value that flows into the
    // prompt - the client sends a fixed BlankCategory, so anything else is bad
    // input and degrades to the fallback rather than reaching the model.
    private const int MaxCategoryLength = 24;

    // Bounds on the client-controlled avoid-list, enforced at the boundary BEFORE
    // the generator iterates it: a very large payload could otherwise burn CPU /
    // memory on dedupe before the per-IP rate limit bites. Generous for a real
    // word bank (the prompt itself only names the first ~20), tiny for an abuser.
    private const int MaxAvoidItems = 100;
    private const int MaxAvoidItemLength = 40;

    private readonly JumbleWordGenerator _generator;
    private readonly RoomRegistry _rooms;
    private readonly ILogger<AiJumbleController> _logger;

    public AiJumbleController(
        JumbleWordGenerator generator,
        RoomRegistry rooms,
        ILogger<AiJumbleController> logger)
    {
        _generator = generator;
        _rooms = rooms;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/ai/jumble -> { words, remainingQuota, fellBack }. Generates a
    /// fresh, moderated set of AI word-bank words for the blank's category via the
    /// gate, or a fell-back result the client degrades to its free reshuffle for.
    /// POST (not GET) so a browser prefetch/refresh can never silently trigger a
    /// gated (potentially paid) call.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Jumble([FromBody] AiJumbleRequest? request, CancellationToken cancellationToken)
    {
        // A null body (a JSON `null` or a binder edge case) degrades cleanly - the
        // client just uses its deterministic reshuffle. Guarding it here lets the
        // rest of the method read `request.` without null-forgiving noise.
        if (request is null)
        {
            return Ok(FellBack());
        }

        // A missing/malformed category is bad input: degrade cleanly rather than
        // 400 into gameplay.
        var category = request.Category?.Trim();
        if (string.IsNullOrEmpty(category) || !IsValidCategory(category))
        {
            return Ok(FellBack());
        }

        // Pick the anonymous quota key: a live room's InstanceId (group) takes
        // precedence over the solo device session id. Neither is PII.
        var instanceId = ResolveInstanceId(request);
        if (string.IsNullOrEmpty(instanceId))
        {
            // No usable anonymous key -> no gated call (the gate would only deny on
            // an empty key anyway); fall back so the client reshuffles for free.
            _logger.LogDebug("AI jumble: no usable anonymous session key; falling back without an AI call.");
            return Ok(FellBack());
        }

        var result = await _generator.GenerateAsync(
            category,
            CapAvoid(request.Avoid),
            request.FamilySafe,
            instanceId,
            cancellationToken);

        return Ok(new
        {
            words = result.Words,
            remainingQuota = result.RemainingQuota,
            fellBack = result.FellBack,
        });
    }

    /// <summary>
    /// Resolves the anonymous quota key: a live room's InstanceId when the join
    /// code names one (group play), else the client's solo device session id.
    /// Returns null when neither is usable.
    /// </summary>
    private string? ResolveInstanceId(AiJumbleRequest? request)
    {
        var roomCode = request?.RoomCode?.Trim();
        if (!string.IsNullOrEmpty(roomCode))
        {
            var room = _rooms.TryGet(roomCode);
            if (room is not null)
            {
                return room.InstanceId;
            }
        }

        var sessionId = request?.SessionId?.Trim();
        return string.IsNullOrEmpty(sessionId) ? null : sessionId;
    }

    /// <summary>
    /// Bounds the client-controlled avoid-list before it reaches the generator:
    /// drops null/blank entries, truncates each to <see cref="MaxAvoidItemLength"/>,
    /// and keeps at most <see cref="MaxAvoidItems"/> - so an oversized payload
    /// cannot force unbounded dedupe work (a cheap DoS surface) ahead of the
    /// per-IP rate limit. These are already-shown words, so trimming is harmless.
    /// </summary>
    private static IReadOnlyList<string> CapAvoid(IReadOnlyList<string>? avoid)
    {
        if (avoid is null || avoid.Count == 0)
        {
            return Array.Empty<string>();
        }

        var capped = new List<string>(Math.Min(avoid.Count, MaxAvoidItems));
        foreach (var word in avoid)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }
            var trimmed = word.Trim();
            capped.Add(trimmed.Length > MaxAvoidItemLength ? trimmed[..MaxAvoidItemLength] : trimmed);
            if (capped.Count >= MaxAvoidItems)
            {
                break;
            }
        }
        return capped;
    }

    /// <summary>A category is valid if it is short and letters/hyphens only (a controlled BlankCategory label).</summary>
    private static bool IsValidCategory(string category)
    {
        if (category.Length > MaxCategoryLength)
        {
            return false;
        }
        foreach (var ch in category)
        {
            if (!char.IsLetter(ch) && ch != '-')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The graceful fell-back payload (no words, no quota spent) the client reshuffles for.</summary>
    private static object FellBack() => new { words = Array.Empty<string>(), remainingQuota = 0, fellBack = true };
}
