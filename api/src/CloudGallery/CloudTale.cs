// ----------------------------------------------------------------------------
//  CloudTale - the stored record behind ONE cloud-synced keepsake tale
//  (keepsake-gallery/05, issue #154).
//
//  This is the purchaser-scoped, cloud-synced sibling of PublishedTale
//  (keepsake-gallery/04): the SAME already-assembled, already-filtered content
//  shape (an ordered list of parts - literal template text and coral player-
//  words) plus a byline of in-session nicknames, but keyed to a PURCHASER account
//  instead of an unguessable public slug. It follows a signed-in purchaser across
//  devices (AC-01).
//
//  NO PII BEYOND THE BYLINE NICKNAME(S) (AC-05, README section 6): the only
//  identity attached to a synced tale is the in-session nickname(s) already shown
//  on the roster and the reveal - NEVER a real name, an email, or any other PII
//  from the purchaser account. The purchaser's identity lives ONLY in the OwnerKey,
//  which since accounts-identity/05 (#195) is the account's STABLE id
//  (account.Id.ToString(), a random GUID) - so the raw email is never on the tale,
//  an operator listing rows sees opaque ids, and an email change never orphans a
//  purchaser's own gallery (the pre-ADR-0003 owner key was a hash of the mutable
//  email, which did orphan it).
//
//  ISOLATION (deliberate): CloudGallery defines its OWN small CloudTalePart record
//  and its own store contract rather than importing PublishedTales.TalePart, mirroring
//  the isolation precedent keepsake-gallery/04 set - the two features share a content
//  SHAPE, not a type. It never touches GameHub or the round lifecycle. The ONE small
//  exception is the controller reusing PublishedTales.SlugGenerator to mint a tale id
//  (a shared id minter, not a data-model coupling) - a deliberate reuse, not a leak of
//  the published-tale model into this surface.
//
//  A record, not a mutable entity: a synced tale is an immutable keepsake fact
//  ("this crew carved this on this date"). QuibbleStone is a toy, not a system of
//  record (README section 4), so there is no update/version ceremony - a tale is
//  saved, listed, and deleted, never edited in place.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.CloudGallery;

/// <summary>
/// One ordered element of a cloud tale's body: either a literal run of the
/// author-authored template text (<see cref="IsWord"/> false) or a single
/// player-supplied coral word (<see cref="IsWord"/> true); <see cref="IsWord"/>
/// drives only the coral highlight when the tale is rendered. Deliberately a
/// LOCAL record (not PublishedTales.TalePart) so CloudGallery stays decoupled from
/// the PublishedTales namespace (the isolation precedent). On save the controller
/// re-vets EVERY non-empty part through the safety filter, since the client's
/// word/literal classification is not trusted (AC-05).
/// </summary>
/// <param name="IsWord">True for a player-supplied coral word, false for literal template text.</param>
/// <param name="Text">The part's text (a template run, or one already-vetted player word).</param>
public sealed record CloudTalePart(bool IsWord, string Text);

/// <summary>
/// A single cloud-synced keepsake tale owned by a purchaser account
/// (keepsake-gallery/05). Immutable. Carries only what the gallery renders: the
/// owner key (the account's stable id, never the raw email - accounts-identity/05),
/// a minted tale id, the title, the ordered body parts (literal text + coral
/// player-words), the byline of in-session nicknames, and a created stamp. NO PII
/// beyond the byline nickname(s) (AC-05).
/// </summary>
/// <param name="OwnerKey">
/// The owner partition key: the account's stable id (account.Id.ToString(), a
/// random GUID - accounts-identity/05). The raw email is NEVER stored on the tale -
/// the tale is keyed to the purchaser only through this opaque, durable id, so it
/// keys exactly like the grant store and survives an email change.
/// </param>
/// <param name="TaleId">The minted, unguessable per-tale id (see SlugGenerator) - the row key within the owner partition.</param>
/// <param name="Title">The tale title (already shown on the reveal; length-capped on save).</param>
/// <param name="Parts">The ordered body: literal template text interleaved with coral player-words.</param>
/// <param name="BylineNames">
/// The "carved by [names]" byline - a single string of already-vetted in-session
/// nicknames (never a real name or any PII, AC-05). May be empty.
/// </param>
/// <param name="CreatedUtc">When the tale was synced to the cloud.</param>
public sealed record CloudTale(
    string OwnerKey,
    string TaleId,
    string Title,
    IReadOnlyList<CloudTalePart> Parts,
    string BylineNames,
    DateTimeOffset CreatedUtc);
