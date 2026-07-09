// ----------------------------------------------------------------------------
//  Account - the lightweight FAMILY / purchaser account (accounts-identity/02,
//  issue #68; re-shaped by accounts-identity/05, issue #195, ADR 0003 Layer 0).
//
//  WHAT THIS IS (and, just as important, what it deliberately is NOT):
//  A QuibbleStone account exists to remember "who this adult is" so their
//  entitlements and keepsakes persist and can be restored on a new device
//  (accounts-identity/03). Post-ADR-0003 it is created either at purchase OR at a
//  free family sign-up (accounts-identity/07) - NEVER as a side effect of playing:
//  players stay anonymous (join code + nickname, no account, no PII - README
//  section 6, CLAUDE.md section 5). The account-creation surfaces live on the
//  adult / purchase side, not anywhere near gameplay.
//
//  THE STABLE IDENTITY IS THE AccountId, NOT THE EMAIL (accounts-identity/05):
//    - `Id` is a durable, randomly-generated GUID minted ONCE at account creation
//      and NEVER changed for the life of the account (AC-01). It is the primary
//      key EVERYTHING else keys off - entitlement grants (api/src/Entitlements),
//      cloud-gallery tales (api/src/CloudGallery), and every ADR 0003 Layer-1/2
//      feature (keepsake-vault, control-plane). It carries NO PII by itself - a
//      GUID is not derived from the email or any identifying value (AC-07).
//    - `Email` is a MUTABLE login attribute (AC-02), not the account's identity:
//      an email change updates this value in place and orphans NOTHING, because
//      grants / gallery are keyed by `Id`, not by a hash of the email. (This story
//      does not build the change-email endpoint - it only makes the record shape
//      support one without a future re-key.) The email is still how a magic-link
//      sign-in FINDS the account: the store keeps a slim email-hash index row
//      (AccountIdentity.KeyFor) that resolves an email to its `Id` first, then
//      reads the account by that `Id`.
//
//  PII-FREE BEYOND THE ONE IDENTITY (AC-01, AC-03, AC-07):
//    - The account holds ONLY the stable id, an email address (the magic-link
//      identity, ADR 0002 Decision A), and a created-at timestamp. That is the
//      WHOLE record - the AccountId adds a non-PII GUID, not a new identifying
//      field (AC-07).
//    - There is NO password, NO OAuth identity, NO name / birthdate / address /
//      phone - the magic link is the entire auth story, so none of that is needed
//      or wanted (minimal data, README section 6).
//    - There is NO reference to any player, nickname, room, or session (AC-03):
//      the account answers "who is this adult", never "who PLAYED". The gameplay
//      side (api/src/Rooms) and the account side (here) never cross-reference,
//      which is exactly why billing-entitlements/01's session gate can read
//      "is there an entitled account?" from IAccountStore WITHOUT touching any
//      room / player data.
//    - The email is ADULT data: an account holder is a grown-up, so there is NO
//      age-of-consent flow for the account itself.
//
//  A record, not a mutable entity in the identity sense: the account's IDENTITY
//  (its `Id`) is a fixed fact, while its `Email` is an updatable login attribute.
//  QuibbleStone is a toy, not a system of record (README section 4), so there is
//  no update/version ceremony beyond replacing the email value in place.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A lightweight family / purchaser account (accounts-identity/02, re-shaped by
/// accounts-identity/05). Holds a stable <see cref="Id"/> (the durable identity),
/// the email login attribute (the magic-link subject, ADR 0002 Decision A), and
/// when the account was created - nothing else. No password, no OAuth, no name /
/// birthdate / address / phone, and NO reference to any player / nickname / room /
/// session (AC-03): this record is scoped to "who this adult is", never "who
/// played". The email is adult data, so no age-of-consent flow attaches to it.
/// </summary>
/// <param name="Id">The stable account id (accounts-identity/05, AC-01): a randomly generated GUID minted once at creation and NEVER changed - the durable key grants, gallery, and every downstream feature partition by. Carries no PII by itself (AC-07); it is NOT derived from the email.</param>
/// <param name="Email">The account's email - the magic-link login attribute. MUTABLE (AC-02): it can be updated in place without disturbing anything else, because nothing keys off it (the store keeps a hash index from email to <see cref="Id"/>). Stored normalized (trim + lowercase-invariant) so the same address resolves to one account.</param>
/// <param name="CreatedUtc">When the account was first created (at purchase or free sign-up). A plain timestamp - not PII, not a player reference.</param>
public sealed record Account(Guid Id, string Email, DateTimeOffset CreatedUtc);
