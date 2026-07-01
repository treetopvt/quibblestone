// ----------------------------------------------------------------------------
//  FamilySafeContentSelector - the SERVER-SIDE family-safe content gate
//  (child-safety/02 follow-up, deferred and now landed for group-play/01).
//
//  This is the server analog of the web's pure `selectTemplates`
//  (web/src/content/familySafe.ts): given the server's tiny template catalog
//  and the current position of the family-safe toggle, it returns the subset a
//  round may draw its template from. It reads ONLY the `FamilySafe` flag already
//  recorded on each catalog entry - it does not interpret any other tag and it
//  does not invent or infer a safety signal of its own.
//
//  Why a server copy exists at all (AC-04): the family-safe toggle lives on the
//  client, but its value is passed to the hub and the SERVER is authoritative
//  about which templates may be offered. A malicious or buggy client cannot
//  smuggle a non-family-safe template into a family-safe round, because the host
//  never sends a template id - the server filters the catalog here and
//  auto-picks from the allowed subset. This mirrors the wire-contract discipline
//  used for PlayerDto/RoomStateDto: the client-side gate and this server gate are
//  kept in behavioral lockstep BY HAND.
//
//  What this is NOT: this is NOT the profanity/safety filter on player free-text
//  submissions (IContentSafetyFilter, child-safety/01). That filter always runs
//  on submitted words regardless of this toggle's position (AC-04) - relaxing
//  this content gate must NEVER relax that filter. This selector has no knowledge
//  of player-submitted text at all; it only ever looks at the hand-curated
//  catalog's FamilySafe flag.
//
//  Pure + stateless by construction: data in, data out. No I/O, no mutable
//  state, no dependency on room/connection state - so it is registered as a
//  singleton in DI (Program.cs) and shared by every transient GameHub instance.
//  Its ONLY consumer today is group-play/01's host template selection.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Content;

namespace QuibbleStone.Api.Safety;

/// <summary>
/// The server-side family-safe selection rule (child-safety/02, AC-04). Given the
/// template catalog and the family-safe toggle position, returns the templates a
/// round may be offered. Mirrors the web's pure <c>selectTemplates</c>
/// (web/src/content/familySafe.ts) - keep the two in behavioral sync by hand.
/// </summary>
public sealed class FamilySafeContentSelector
{
    /// <summary>
    /// Filters <paramref name="catalog"/> by the family-safe toggle position:
    ///
    /// - <paramref name="familySafeOn"/> = true  -> only entries with
    ///   <see cref="TemplateCatalogEntry.FamilySafe"/> == true (AC-04).
    /// - <paramref name="familySafeOn"/> = false -> every entry, unfiltered.
    ///
    /// Never mutates the input; returns a fresh list the caller owns. This is the
    /// exact behavior of the web's selectTemplates gate, expressed server-side and
    /// authoritative (AC-04).
    /// </summary>
    /// <param name="catalog">The server's minimal template catalog (mirrors seedLibrary).</param>
    /// <param name="familySafeOn">The family-safe toggle position sent by the host.</param>
    /// <returns>The allowed subset (a new list; may be empty if nothing qualifies).</returns>
    public IReadOnlyList<TemplateCatalogEntry> SelectAllowed(
        IReadOnlyList<TemplateCatalogEntry> catalog,
        bool familySafeOn)
    {
        if (!familySafeOn)
        {
            return catalog.ToArray();
        }

        return catalog.Where(entry => entry.FamilySafe).ToArray();
    }
}
