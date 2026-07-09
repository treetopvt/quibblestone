// ----------------------------------------------------------------------------
//  ReportedTalesController - the OPERATOR review queue for reported public tales
//  (sysadmin-console/03, issue #137). The back-office surface behind story 01's
//  "Operator" policy that lets a signed-in operator review a tale the crowd
//  auto-hid and either confirm-hidden or restore it. Three endpoints, all under
//  /api/admin and all [Authorize(Policy = "Operator")]:
//
//    GET  /api/admin/reported-tales                  -> the review queue
//    POST /api/admin/reported-tales/{slug}/confirm   -> keep it hidden / delete it
//    POST /api/admin/reported-tales/{slug}/restore   -> resume serving + reset count
//
//  WHAT THIS REUSES (and never forks):
//    - The SAME IPublishedTaleStore the public serve / publish / revoke path uses -
//      story 03 extended it with the moderation operations (report + companion
//      state). There is NO parallel read path and NO second store (the disabled
//      fallback returns an empty queue locally, so the console runs with the feature
//      simply off). This controller only ORCHESTRATES those store calls.
//    - The "Operator" authorization policy + scheme story 01 registered. Every action
//      is [Authorize(Policy = OperatorSession.PolicyName)] - NEVER a bare [Authorize].
//      A purchaser credential fails to unprotect under the operator purpose and never
//      reaches here (AC-03 of story 01); an unauthenticated caller gets 401.
//
//  CHILD-SAFETY / ANONYMITY FIREWALL (AC-06, non-negotiable): a report is a slug + a
//  count. The operator reviews CONTENT (the already-filtered tale body), never a
//  person - there is NO path from a reported tale to a player nickname, room, or
//  session, and this controller imports NOTHING from Rooms / Hubs. It also NEVER
//  re-runs the content-safety filter (AC-04): confirm / restore are a human decision,
//  not a second automated check. keepsake-gallery/04's publish-time re-vet is the
//  authoritative pre-display gate and is untouched.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.PublishedTales;

namespace QuibbleStone.Api.Admin;

/// <summary>One ordered body part of a reported tale as the review queue returns it.</summary>
/// <param name="IsWord">True for a coral player-word, false for literal template text.</param>
/// <param name="Text">The already-filtered part text.</param>
public sealed record ReportedTalePartDto(bool IsWord, string Text);

/// <summary>
/// One entry of the operator review queue (sysadmin-console/03, AC-03): a hidden
/// tale's CONTENT (title, body parts, byline) plus its accumulated report count, so
/// an operator can read what was flagged and decide. Carries ONLY the already-vetted
/// tale content and a count - never a reporter identity, player, room, or session
/// (AC-06).
/// </summary>
/// <param name="Slug">The hidden tale's slug (the moderation key the actions target).</param>
/// <param name="Title">The tale title.</param>
/// <param name="Parts">The ordered body: literal text interleaved with coral player-words.</param>
/// <param name="BylineNames">The in-session nickname byline (may be empty).</param>
/// <param name="ReportCount">How many reports pushed it past the auto-hide threshold.</param>
public sealed record ReportedTaleDto(
    string Slug,
    string Title,
    IReadOnlyList<ReportedTalePartDto> Parts,
    string BylineNames,
    int ReportCount);

/// <summary>The review-queue response: the currently-hidden tales awaiting an operator decision.</summary>
/// <param name="Tales">The hidden tales, most-reported first.</param>
public sealed record ReportedTalesQueueResult(IReadOnlyList<ReportedTaleDto> Tales);

/// <summary>The result of a confirm / restore action: whether a hidden tale was acted on.</summary>
/// <param name="Slug">The slug the action targeted.</param>
/// <param name="Applied">True when a hidden tale was found and acted on; false (idempotent no-op) otherwise.</param>
/// <param name="Message">A friendly message describing the outcome.</param>
public sealed record ReportedTaleActionResult(string Slug, bool Applied, string Message);

[ApiController]
[Route("api/admin/reported-tales")]
// sysadmin-console/05 (#214): the CONTENT scope (moderation). Same Operator credential
// boundary PLUS the Content scope - a no-op for today's all-scopes operator (AC-05); a
// future content-only moderator is a config entry, not a rework (AC-06).
[Authorize(Policy = OperatorScopePolicy.Content)]
public sealed class ReportedTalesController : ControllerBase
{
    private readonly IPublishedTaleStore _store;

    public ReportedTalesController(IPublishedTaleStore store) => _store = store;

    /// <summary>
    /// GET /api/admin/reported-tales -> the review queue (AC-03). Lists every
    /// currently-hidden tale with its content + report count, most-reported first, so
    /// an operator can review it. Reads the SAME store the public path uses (never a
    /// parallel path). Returns CONTENT only - no reporter identity or player data (AC-06).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Queue(CancellationToken cancellationToken)
    {
        var hidden = await _store.ListHiddenAsync(cancellationToken);
        var dtos = hidden
            .Select(v => new ReportedTaleDto(
                Slug: v.Tale.Slug,
                Title: v.Tale.Title,
                Parts: v.Tale.Parts.Select(p => new ReportedTalePartDto(p.IsWord, p.Text)).ToList(),
                BylineNames: v.Tale.BylineNames,
                ReportCount: v.ReportCount))
            .ToList();

        return Ok(new ReportedTalesQueueResult(dtos));
    }

    /// <summary>
    /// POST /api/admin/reported-tales/{slug}/confirm -> confirm the tale stays hidden
    /// (AC-03). After this the slug NEVER serves again (the store hard-deletes it) and
    /// it drops off the queue. Idempotent: a slug that is unknown / not hidden returns
    /// Applied=false rather than an error.
    /// </summary>
    [HttpPost("{slug}/confirm")]
    public async Task<IActionResult> Confirm(string slug, CancellationToken cancellationToken)
    {
        var applied = await _store.ConfirmHiddenAsync(slug, cancellationToken);
        return Ok(new ReportedTaleActionResult(
            slug,
            applied,
            applied
                ? "The tale is confirmed hidden and will not serve again."
                : "There is no hidden tale under that slug."));
    }

    /// <summary>
    /// POST /api/admin/reported-tales/{slug}/restore -> restore the tale (AC-03). It
    /// resumes serving normally at its slug AND its report count is reset to zero, so
    /// the same reports do not immediately re-hide it. Idempotent: a slug that is
    /// unknown / not hidden returns Applied=false rather than an error.
    /// </summary>
    [HttpPost("{slug}/restore")]
    public async Task<IActionResult> Restore(string slug, CancellationToken cancellationToken)
    {
        var applied = await _store.RestoreAsync(slug, cancellationToken);
        return Ok(new ReportedTaleActionResult(
            slug,
            applied,
            applied
                ? "The tale is restored and serving normally again."
                : "There is no hidden tale under that slug."));
    }
}
