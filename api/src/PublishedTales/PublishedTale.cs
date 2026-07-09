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
//  MODERATION TAKEDOWN IS A SOFT-DELETE (keepsake-vault/04, issue #231): the
//  operator "confirm-hidden" action (sysadmin-console/03) no longer HARD-deletes
//  the tale body - it stamps DeletedUtc so the slug stops serving (reads as GONE,
//  exactly the old post-confirm behavior) but the content stays recoverable within
//  a restore window (TakedownRestoreWindowDays), so a wrongly-hidden tale or an
//  operator mistake is reversible. Past the window the row is reclaimed lazily on
//  read, the same idiom as ExpiresUtc. This is the SAME restore-window model the
//  vault's own soft-delete uses (VaultTale.RestoreWindowDays). Restoring a takedown
//  is a DISTINCT, higher-friction operation from the existing moderation "un-hide"
//  (which never body-deleted anything) - see IPublishedTaleStore.
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
/// <param name="DeletedUtc">
/// When the tale was taken down by a moderation confirm-hidden action, or null while
/// it is serving normally (keepsake-vault/04, AC-04). A taken-down tale reads as GONE
/// to the public (a 404, like a revoked / expired tale) but its content stays
/// recoverable until <see cref="TakedownRestoreWindowDays"/> days past this instant
/// (AC-02), after which it is eligible for hard removal (AC-03). Null for a tale that
/// was never taken down.
/// </param>
public sealed record PublishedTale(
    string Slug,
    string Title,
    IReadOnlyList<TalePart> Parts,
    string BylineNames,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? DeletedUtc = null)
{
    /// <summary>
    /// The moderation-takedown restore window in days (keepsake-vault/04, AC-02): a
    /// soft-deleted (taken-down) tale stays recoverable for this many days past its
    /// <see cref="DeletedUtc"/>, then becomes eligible for hard removal (AC-03). The
    /// SAME restore-window model the vault's own soft-delete uses
    /// (<see cref="Vault.VaultTale.RestoreWindowDays"/>). A settings-key candidate
    /// (ADR 0003 control-plane "knob migration") shipped as a named code constant
    /// until control-plane/01's catalog exists, mirroring
    /// <see cref="PublishedTalesController.TaleTtl"/>'s precedent - not a magic number.
    /// </summary>
    public const int TakedownRestoreWindowDays = 30;

    /// <summary>
    /// True when this tale is at or past its expiry instant and must read as GONE
    /// (AC-05). Pure, so it is unit-tested directly rather than through the store.
    /// </summary>
    /// <param name="now">The current instant (injected so tests are deterministic).</param>
    public bool IsExpired(DateTimeOffset now) => ExpiresUtc <= now;

    /// <summary>
    /// True when this tale has been taken down by a moderation confirm-hidden action
    /// (keepsake-vault/04, AC-04): it stops serving to the public but, within the
    /// restore window, its content is still recoverable (AC-02). A normally-serving
    /// tale has a null <see cref="DeletedUtc"/>.
    /// </summary>
    public bool IsTakenDown => DeletedUtc is not null;

    /// <summary>
    /// True when this tale was taken down AND its restore window has fully elapsed
    /// (<see cref="DeletedUtc"/> + <see cref="TakedownRestoreWindowDays"/> at or past
    /// <paramref name="now"/>), so it is eligible for real (hard) removal and reads
    /// as genuinely GONE (AC-03). Pure and computed from <see cref="DeletedUtc"/>.
    /// False for a serving tale (nothing to purge).
    /// </summary>
    /// <param name="now">The current instant (injected so tests are deterministic).</param>
    public bool IsRestoreWindowElapsed(DateTimeOffset now) =>
        DeletedUtc is DateTimeOffset deleted && deleted.AddDays(TakedownRestoreWindowDays) <= now;
}
