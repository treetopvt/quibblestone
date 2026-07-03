// ----------------------------------------------------------------------------
//  TaleModeration - the small companion state behind the post-publish report ->
//  auto-hide -> operator-review path for public keepsake tales
//  (sysadmin-console/03, issue #137).
//
//  WHY THIS IS A SEPARATE RECORD, NOT A FIELD ON PublishedTale (load-bearing):
//  PublishedTale is an IMMUTABLE record serialized WHOLE into one Table row - if a
//  mutable report counter lived on it, every single report would rewrite the entire
//  tale. Instead the moderation signal is a TINY companion state keyed by the SAME
//  slug (a report count + a Hidden flag), stored and mutated on its own, never
//  touching the tale body. It is also a DISTINCT state from the tale's lazy-expiry
//  IsExpired (a host who revoked / let their tale expire is GONE / 404; a tale the
//  crowd reported past the threshold is HIDDEN / under review) - the two must never
//  be collapsed (AC-02, the load-bearing distinction: a legitimate host must not
//  read as a moderated bad actor).
//
//  ANONYMITY (AC-06, non-negotiable): a report is a slug + a count and NOTHING else.
//  There is NO reporter identity, NO PII, and NO path from here to any player
//  nickname, room, or session - moderation operates purely on published CONTENT.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.PublishedTales;

/// <summary>
/// The moderation companion state for one published tale, keyed by its slug
/// (sysadmin-console/03). <see cref="ReportCount"/> is how many times the public
/// tale page has been reported; <see cref="IsHidden"/> is true once that count
/// reached the auto-hide threshold (or an operator has not yet restored it). This
/// is a DISTINCT signal from PublishedTale.IsExpired (AC-02) and carries NO reporter
/// identity or PII (AC-06). An unreported slug has no stored state and resolves to
/// the default (count 0, not hidden).
/// </summary>
/// <param name="Slug">The published tale's slug this moderation state is keyed to.</param>
/// <param name="ReportCount">How many reports have accumulated against the slug.</param>
/// <param name="IsHidden">True when the tale is auto-hidden and serving the "under review" page.</param>
public sealed record TaleModerationState(string Slug, int ReportCount, bool IsHidden)
{
    /// <summary>The neutral, unreported default for a slug that has never been reported.</summary>
    /// <param name="slug">The slug the default is for.</param>
    public static TaleModerationState None(string slug) => new(slug, ReportCount: 0, IsHidden: false);
}

/// <summary>
/// One row of the operator review queue (sysadmin-console/03, AC-03): a currently
/// hidden tale's stored CONTENT plus its accumulated report count, so an operator can
/// read what was flagged and decide confirm-hidden or restore. Carries only the
/// already-filtered tale content and a count - never a reporter identity, a player
/// nickname, a room, or a session (AC-06).
/// </summary>
/// <param name="Tale">The hidden tale's stored (already-filtered) content.</param>
/// <param name="ReportCount">How many reports pushed it past the auto-hide threshold.</param>
public sealed record ReportedTaleView(PublishedTale Tale, int ReportCount);
