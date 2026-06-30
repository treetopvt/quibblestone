// ----------------------------------------------------------------------------
//  IContentSafetyFilter - the single server-side safety contract for QuibbleStone.
//
//  Child safety is a non-negotiable (README section 6, CLAUDE.md section 5):
//  ANY free text a player types (a blank answer or a nickname) must be vetted
//  BEFORE it is stored or shown to anyone. This interface is the one gate every
//  free-text surface routes through. There is exactly ONE implementation
//  (ContentSafetyFilter), registered in DI (Program.cs), so the hub and any
//  future REST controller resolve the SAME instance (child-safety/01 AC-05). No
//  surface ships its own word logic.
//
//  Why async (CheckAsync) even though the baseline impl is synchronous:
//    The slice-1 implementation is a pure, in-process blocklist match. But the
//    backlog (README section 12) parks AI / remote moderation. An async-from-day-1
//    signature means swapping in a remote or AI check later is a drop-in - no
//    caller has to change from sync to async. The CancellationToken lets a future
//    remote call honor request cancellation.
//
//  Authoritative by design (AC-04): the web client may pre-validate for snappy
//  UX, but THIS server-side check is the real gate. A client cannot bypass it.
//
//  Usage (a free-text surface, e.g. a hub method handling a submission):
//      var verdict = await _safety.CheckAsync(candidateText, ct);
//      if (!verdict.IsAllowed)
//      {
//          // Reject with verdict.Message; never store or broadcast the text.
//          // The player can simply try again.
//      }
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Safety;

/// <summary>
/// The single server-side gate for player-submitted free text. Every free-text
/// surface (nicknames, blank answers) calls this before the text is stored or
/// shown. There is one implementation, resolved from DI.
/// </summary>
public interface IContentSafetyFilter
{
    /// <summary>
    /// Vets a candidate piece of free text. Returns a verdict carrying a pass/fail
    /// flag and, on failure, a friendly, non-shaming, kid-readable message the
    /// caller can show so the player can try again (AC-02).
    /// </summary>
    /// <param name="candidate">
    /// The raw player-submitted text (a blank answer or a nickname). May be null,
    /// empty, or whitespace - the filter handles those without throwing.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the (currently synchronous, later possibly remote) check.
    /// </param>
    /// <returns>A <see cref="ContentSafetyResult"/> describing the verdict.</returns>
    ValueTask<ContentSafetyResult> CheckAsync(string? candidate, CancellationToken cancellationToken = default);
}

/// <summary>
/// The verdict from <see cref="IContentSafetyFilter.CheckAsync"/>. Immutable.
///
/// On a pass, <see cref="IsAllowed"/> is true and <see cref="Message"/> is null.
/// On a failure, <see cref="IsAllowed"/> is false and <see cref="Message"/> is a
/// friendly, non-shaming string the caller can show the player (AC-02). The
/// rejected text itself is never echoed back in the message and must never be
/// shown to others.
/// </summary>
/// <param name="IsAllowed">True if the text passed the filter and may proceed.</param>
/// <param name="Message">
/// A friendly retry message when blocked; null when allowed. Plain text - there
/// is no i18n layer in this stack, so keep it brief and kid-readable.
/// </param>
public sealed record ContentSafetyResult(bool IsAllowed, string? Message)
{
    /// <summary>A shared "passed" verdict (no message needed).</summary>
    public static readonly ContentSafetyResult Allowed = new(true, null);

    /// <summary>Builds a "blocked" verdict carrying the friendly retry message.</summary>
    public static ContentSafetyResult Blocked(string message) => new(false, message);
}
