// ----------------------------------------------------------------------------
//  VaultTale - the stored record behind ONE tale in the server-side keepsake
//  vault (keepsake-vault/01, ADR 0003 Decision 2 / Layer 2, issue #196).
//
//  The vault is an anonymous, server-side keepsake store keyed by a device-held
//  RANDOM vault id (never an account, never PII): every completed reveal auto-
//  saves into it, so "where are my saved stories?" finally has an answer that
//  survives the device-local IndexedDB gallery's 30-tale cap and its silent
//  eviction. This record is the vault-scoped sibling of CloudTale
//  (keepsake-gallery/05) and PublishedTale (keepsake-gallery/04): the SAME
//  already-assembled, already-filtered content shape (an ordered list of parts -
//  literal template text and coral player-words) plus a byline of in-session
//  nicknames, but keyed to a VAULT ID instead of a purchaser account or a public
//  slug.
//
//  ISOLATION (deliberate, the keepsake-gallery precedent): Vault defines its OWN
//  small VaultTalePart record rather than importing CloudGallery.CloudTalePart or
//  PublishedTales.TalePart - the three features share a content SHAPE, not a type
//  (see docs/features/keepsake-vault/implementation.md's reuse map). It never
//  touches GameHub, the room registry, or the round lifecycle, and it imports
//  NOTHING from api/src/Rooms. The ONE deliberate reuse is the controller minting
//  the RowKey via PublishedTales.SlugGenerator (a shared id minter, not a data-
//  model coupling).
//
//  NO PII, EVER (AC-04, ADR 0003 "no PII on the play plane"): the only identity
//  attached to a vault tale is the in-session nickname(s) already shown on the
//  roster and the reveal - NEVER a real name, an email, an IP, a device
//  fingerprint, a room, or a player. The vault id itself is a random handle
//  (AC-01), never derived from or joined to any identity. Byline nicknames stay
//  PLAY-PLANE content until a family claims the vault (keepsake-vault/03) - only
//  from that point do they become account-plane household data under ADR 0003's
//  carve-out.
//
//  TTL, COMPUTED NOT STORED (AC-03): an unclaimed vault's tales expire on a TTL -
//  default 90 days from the SERVER-STAMPED CreatedUtc - computed as
//  CreatedUtc + TtlDays AT READ TIME. There is deliberately NO stored ExpiresUtc
//  column (that is TableStoragePublishedTaleStore.GetAsync's single-slug shape,
//  NOT this feature's per-vault partition list): IsExpired computes the cutoff
//  from CreatedUtc so the TTL can never be spoofed by a stored expiry and a
//  control-plane TtlDays change applies retroactively to every existing tale.
//
//  A record, not a mutable entity: a saved tale is an immutable keepsake fact
//  ("this crew carved this on this date"). QuibbleStone is a toy, not a system of
//  record (README section 4) - a tale is saved, listed, and expired, never edited.
//
//  SOFT DELETE + RESTORE WINDOW (keepsake-vault/04, issue #231): a player deleting
//  a vault tale no longer removes the row outright - the tale is marked deleted
//  (DeletedUtc stamped) and stops appearing in any listing, but its content stays
//  fully recoverable for a bounded restore window (RestoreWindowDays, default 30
//  from the deletion instant). This is the codebase's "rebuild the immutable
//  record with a flipped marker" pattern (mirrors Player.Connected /
//  Room.MarkDisconnected's `with { ... }`): DeletedUtc is set on a rebuilt record,
//  never editing the content fields. Past the window a soft-deleted tale becomes
//  eligible for real (hard) removal, reclaimed lazily on the next read (the same
//  purge-on-read idiom this record's TTL already uses) - no reaper job. The restore
//  STORE method lives on IVaultStore; the operator-facing console verb that calls
//  it is sysadmin-console/07, not this story. A player's own restore of their own
//  delete carries NO extra friction (it only affects content their own family saw)
//  - the higher-friction, confirmation-gated path is the published-tale takedown
//  restore (IPublishedTaleStore.RestoreFromTakedownAsync), a deliberately DISTINCT
//  operation (AC-07).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Vault;

/// <summary>
/// One ordered element of a vault tale's body: either a literal run of the
/// author-authored template text (<see cref="IsWord"/> false) or a single
/// player-supplied coral word (<see cref="IsWord"/> true); <see cref="IsWord"/>
/// drives only the coral highlight when the tale is rendered. Deliberately a
/// LOCAL record (not CloudGallery.CloudTalePart / PublishedTales.TalePart) so the
/// Vault namespace stays decoupled (the isolation precedent). On save the
/// controller re-vets EVERY non-empty part through the safety filter, since the
/// endpoint is anonymous and the client's word/literal classification is not
/// trusted (AC-04).
/// </summary>
/// <param name="IsWord">True for a player-supplied coral word, false for literal template text.</param>
/// <param name="Text">The part's text (a template run, or one already-vetted player word).</param>
public sealed record VaultTalePart(bool IsWord, string Text);

/// <summary>
/// A single tale in the server-side keepsake vault (keepsake-vault/01). Immutable.
/// Carries only what the gallery renders: the owning vault id (a random device
/// handle, never PII), a minted tale id, the title, the ordered body parts
/// (literal text + coral player-words), the byline of in-session nicknames, and a
/// SERVER-STAMPED created instant. NO PII beyond the byline nickname(s) (AC-04).
/// Expiry is COMPUTED from <see cref="CreatedUtc"/> at read time (AC-03) - there
/// is no stored expiry.
/// </summary>
/// <param name="VaultId">
/// The owning vault's id: an opaque, cryptographically random device handle
/// (AC-01, mirrors Room.NewReconnectToken's posture). It is the partition key and
/// a BEARER CREDENTIAL - never derived from or joined to an email, device
/// fingerprint, IP, or any other identity (AC-04).
/// </param>
/// <param name="TaleId">The minted, unguessable per-tale id (see SlugGenerator) - the row key within the vault partition.</param>
/// <param name="Title">The tale title (already shown on the reveal; length-capped on save).</param>
/// <param name="Parts">The ordered body: literal template text interleaved with coral player-words.</param>
/// <param name="BylineNames">
/// The "carved by [names]" byline - a single string of already-vetted in-session
/// nicknames (never a real name or any PII, AC-04). May be empty.
/// </param>
/// <param name="CreatedUtc">
/// When the tale was saved - ALWAYS server-stamped (DateTimeOffset.UtcNow at write
/// time, AC-02), never accepted from the client: the TTL (AC-03) keys off this on
/// an anonymous, abusable endpoint, so a client-supplied value would be spoofable.
/// </param>
/// <param name="DeletedUtc">
/// When the tale was SOFT-deleted, or null while it is live (keepsake-vault/04,
/// AC-01). A soft-deleted tale is omitted from every listing but stays fully
/// recoverable until <see cref="RestoreWindowDays"/> days past this instant
/// (AC-02), after which it is eligible for hard removal (AC-03). ALWAYS server-
/// stamped on the soft-delete action, never accepted from a client - the restore
/// window keys off it. Null for every tale saved before it was ever deleted.
/// </param>
public sealed record VaultTale(
    string VaultId,
    string TaleId,
    string Title,
    IReadOnlyList<VaultTalePart> Parts,
    string BylineNames,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? DeletedUtc = null)
{
    /// <summary>
    /// The default unclaimed-vault TTL in days (AC-03): a stored tale expires
    /// <see cref="TtlDays"/> days after its <see cref="CreatedUtc"/>. A settings-key
    /// candidate (ADR 0003 control plane) shipped as a code constant until
    /// control-plane/01's catalog exists - this story is not blocked on it.
    /// </summary>
    public const int TtlDays = 90;

    /// <summary>
    /// The soft-delete restore window in days (keepsake-vault/04, AC-02): a
    /// soft-deleted tale stays recoverable for this many days past its
    /// <see cref="DeletedUtc"/>, then becomes eligible for hard removal (AC-03).
    /// The SAME restore-window model the published-tale takedown path uses
    /// (see the published side's own constant). A settings-key candidate (ADR 0003
    /// control-plane "knob migration") shipped as a code constant until
    /// control-plane/01's catalog exists - this story is not blocked on it, and it
    /// mirrors <see cref="PublishedTales.PublishedTalesController.TaleTtl"/>'s
    /// named-constant-recorded-in-the-story precedent rather than a magic number.
    /// </summary>
    public const int RestoreWindowDays = 30;

    /// <summary>
    /// True when this tale is at or past its computed TTL-expiry instant
    /// (<see cref="CreatedUtc"/> + <see cref="TtlDays"/>) and must read as GONE
    /// (AC-03). Pure and computed - NOT read from a stored ExpiresUtc column - so
    /// the TTL cannot be spoofed and a TtlDays change applies to every existing
    /// tale. Unit-tested directly rather than through a store.
    /// </summary>
    /// <param name="now">The current instant (injected so tests are deterministic).</param>
    public bool IsExpired(DateTimeOffset now) => CreatedUtc.AddDays(TtlDays) <= now;

    /// <summary>
    /// True when this tale has been soft-deleted (keepsake-vault/04, AC-01): it is
    /// omitted from every listing but, within the restore window, its content is
    /// still recoverable (AC-02). A live tale has a null <see cref="DeletedUtc"/>.
    /// </summary>
    public bool IsDeleted => DeletedUtc is not null;

    /// <summary>
    /// True when this tale was soft-deleted AND its restore window has fully elapsed
    /// (<see cref="DeletedUtc"/> + <see cref="RestoreWindowDays"/> at or past
    /// <paramref name="now"/>), so it is eligible for real (hard) removal and reads
    /// as genuinely GONE (AC-03). Pure and computed from <see cref="DeletedUtc"/>
    /// (never a stored expiry), so a window change applies to every soft-deleted
    /// tale. False for a live tale (nothing to purge).
    /// </summary>
    /// <param name="now">The current instant (injected so tests are deterministic).</param>
    public bool IsRestoreWindowElapsed(DateTimeOffset now) =>
        DeletedUtc is DateTimeOffset deleted && deleted.AddDays(RestoreWindowDays) <= now;
}
