// ----------------------------------------------------------------------------
//  ActionLogController - the OPERATOR read surface for the action log
//  (sysadmin-console/06, issue #233, AC-03). ONE endpoint:
//
//    GET /api/admin/action-log  -> the latest rows, newest-first, capped at 200
//
//  OPS SCOPE (sysadmin-console/05): guarded by [Authorize(Policy =
//  OperatorScopePolicy.Ops)] - the SAME operator credential boundary as
//  StripeModeController plus the Ops scope. Today's single operator holds all
//  scopes, so this is a no-op; a future ops-only operator is a config entry, not a
//  rework. This is a READ-ONLY view; the WRITE seam (IOperatorActionLog.AppendAsync)
//  is called by the money / moderation / settings controllers, never here.
//
//  ANONYMITY (AC-06): a row carries ONLY operator email + action + target + note +
//  timestamp. This controller projects those fields verbatim and imports NOTHING
//  from Rooms / Hubs - there is no path from a log row to a player / room / session.
//  The paired render-side rule (React default text escaping, never
//  dangerouslySetInnerHTML) lives in ActionLogView.tsx (AC-07).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// One action-log row as the console view receives it (sysadmin-console/06, AC-03): who did what to
/// which target, when, with an optional note. Carries ONLY operator-plane facts (AC-06) - never a
/// player / room / session reference.
/// </summary>
/// <param name="OperatorEmail">The operator who performed the action.</param>
/// <param name="Action">The action verb (e.g. "entitlement.grant", "tale.takedown", "stripe-mode.flip", "settings.put").</param>
/// <param name="Target">What the action targeted (a purchaser email, a tale slug, a settings key, or "stripe-mode").</param>
/// <param name="Note">Free-text detail (e.g. a capability key or an old -&gt; new value); may be empty.</param>
/// <param name="TimestampUtc">When the row was appended (UTC).</param>
public sealed record ActionLogRowDto(
    string OperatorEmail,
    string Action,
    string Target,
    string Note,
    DateTimeOffset TimestampUtc);

/// <summary>The action-log view response: the most recent rows, newest first (AC-03).</summary>
/// <param name="Rows">The latest rows in reverse-chronological order, capped at the page size.</param>
public sealed record ActionLogViewResult(IReadOnlyList<ActionLogRowDto> Rows);

[ApiController]
[Route("api/admin/action-log")]
// sysadmin-console/05 (#214): the OPS scope (operations). Same Operator credential boundary PLUS
// the Ops scope - a no-op for today's all-scopes operator; a future ops-only operator is a config
// entry, not a rework.
[Authorize(Policy = OperatorScopePolicy.Ops)]
public sealed class ActionLogController : ControllerBase
{
    /// <summary>A sane page cap (AC-03): the view lists the latest 200 rows, never an unbounded history.</summary>
    private const int PageSize = 200;

    private readonly IOperatorActionLog _actionLog;

    /// <summary>
    /// Constructs the controller over the single action-log seam (the SAME IOperatorActionLog the
    /// money / moderation / settings controllers append through - one seam, AC-02). It only READS;
    /// it never appends and never touches any room / player store.
    /// </summary>
    public ActionLogController(IOperatorActionLog actionLog) => _actionLog = actionLog;

    /// <summary>
    /// GET /api/admin/action-log -> the latest rows, newest-first, capped at <see cref="PageSize"/>
    /// (AC-03). Reads through the seam's newest-first listing (no client-side sort). Returns
    /// operator-plane facts only (AC-06).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var rows = await _actionLog.ListRecentAsync(PageSize, cancellationToken);
        var dtos = rows
            .Select(r => new ActionLogRowDto(r.OperatorEmail, r.Action, r.Target, r.Note, r.TimestampUtc))
            .ToList();
        return Ok(new ActionLogViewResult(dtos));
    }
}
