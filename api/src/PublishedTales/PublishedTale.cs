// ----------------------------------------------------------------------------
//  PublishedTale - the stored record behind a public shareable tale link
//  (keepsake-gallery/04, issue #66).
//
//  This is the ONLY thing the feature ever stores server-side, and it is
//  deliberately minimal (AC-05): the ALREADY-ASSEMBLED, ALREADY-FILTERED story
//  (as an ordered list of parts - literal template text and coral player-words)
//  plus a small amount of anonymous metadata (a byline of in-session nicknames,
//  a created stamp, an expiry stamp). It NEVER holds raw per-player submissions,
//  NEVER an IP / device id / session id, NEVER a real name - only the exact
//  content a family-safe reveal already showed and the in-session nicknames
//  already displayed on the roster (AC-03, README section 6).
//
//  Why "parts" and not one flat string: the public page paints the coral
//  player-words distinctly from the author-authored template prose, exactly like
//  the Reveal tablet does. Storing the interleaving (each part tagged word/text)
//  lets the read-side render that distinction without re-running any engine or
//  re-deriving attribution. The parts list is small (a short story), so it
//  serializes to a single JSON string property well under Table Storage's 32KB
//  string ceiling (see TableStoragePublishedTaleStore).
//
//  Expiry (AC-05): a published tale is an ephemeral keepsake, not a system of
//  record (README section 4). ExpiresUtc caps its life; the read side treats a
//  tale at or past that instant as GONE (lazy expiry-on-read via IsExpired),
//  mirroring the room registry's lazy idle sweep. No background reaper is needed
//  at toy scale.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.PublishedTales;

/// <summary>
/// One ordered element of a published tale's body: either a literal run of the
/// author-authored template text (<see cref="IsWord"/> false) or a single
/// player-supplied coral word (<see cref="IsWord"/> true); <see cref="IsWord"/>
/// drives only the coral highlight on the page. On publish the endpoint re-vets
/// EVERY non-empty part - words AND "literal" parts - through the safety filter,
/// since the endpoint is public and the client's word/literal classification is
/// not trusted (AC-03, security review CR-001). Author template text is
/// false-positive-resistant and passes harmlessly.
/// </summary>
/// <param name="IsWord">True for a player-supplied coral word, false for literal template text.</param>
/// <param name="Text">The part's text (a template run, or one already-vetted player word).</param>
public sealed record TalePart(bool IsWord, string Text);

/// <summary>
/// The stored, already-filtered public tale (keepsake-gallery/04). Immutable.
/// Carries only what the public read-only page renders: the title, the ordered
/// body parts (literal text + coral player-words), the byline of in-session
/// nicknames, and the created / expiry stamps. NO PII, ever (AC-03/AC-05).
/// </summary>
/// <param name="Slug">The unguessable, non-sequential public slug (see SlugGenerator).</param>
/// <param name="Title">The tale title (already shown on the reveal; length-capped on publish).</param>
/// <param name="Parts">The ordered body: literal template text interleaved with coral player-words.</param>
/// <param name="BylineNames">
/// The "carved by [names]" byline - a single string of already-vetted in-session
/// nicknames (never a real name or any PII). May be empty when a round had no
/// resolvable crew (the page then simply omits the byline).
/// </param>
/// <param name="CreatedUtc">When the tale was published.</param>
/// <param name="ExpiresUtc">When the tale stops resolving (lazy expiry-on-read, AC-05).</param>
public sealed record PublishedTale(
    string Slug,
    string Title,
    IReadOnlyList<TalePart> Parts,
    string BylineNames,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc)
{
    /// <summary>
    /// True when this tale is at or past its expiry instant and must read as GONE
    /// (AC-05). Pure, so it is unit-tested directly rather than through the store.
    /// </summary>
    /// <param name="now">The current instant (injected so tests are deterministic).</param>
    public bool IsExpired(DateTimeOffset now) => ExpiresUtc <= now;
}
