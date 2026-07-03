// ----------------------------------------------------------------------------
//  Account - the lightweight PURCHASER account (accounts-identity/02, issue #68).
//
//  WHAT THIS IS (and, just as important, what it deliberately is NOT):
//  A QuibbleStone account exists for ONE reason - to remember "who bought this"
//  so a purchaser can restore an entitlement on a new device (accounts-identity/
//  03). It is created ONLY at purchase time (AC-02), NEVER as a side effect of
//  playing: players stay anonymous (join code + nickname, no account, no PII -
//  README section 6, CLAUDE.md section 5). The account-creation surface lives in
//  the purchase flow (billing-entitlements), not anywhere near gameplay.
//
//  PII-FREE BEYOND THE ONE IDENTITY (AC-01, AC-03, AC-05):
//    - The account holds ONLY an email address (the magic-link identity, ADR 0002
//      Decision A) plus a created-at timestamp. That is the WHOLE record.
//    - There is NO password, NO OAuth identity, NO name / birthdate / address /
//      phone - the magic link is the entire auth story, so none of that is needed
//      or wanted (minimal data, README section 6).
//    - There is NO reference to any player, nickname, room, or session (AC-03):
//      the account answers "who BOUGHT this", never "who PLAYED". The gameplay
//      side (api/src/Rooms) and the purchaser side (here) never cross-reference,
//      which is exactly why billing-entitlements/01's session gate can read
//      "is there an entitled purchaser?" from IAccountStore WITHOUT touching any
//      room / player data (AC-04).
//    - The email is ADULT data: a purchaser is the buying grown-up, so there is
//      NO age-of-consent flow for the account itself (AC-05).
//
//  A record, not a mutable entity: an account is a tiny immutable fact ("this
//  email bought in at this time"). QuibbleStone is a toy, not a system of record
//  (README section 4), so there is no update/version ceremony here.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A lightweight purchaser account (accounts-identity/02, AC-01). Holds ONLY the
/// email identity (the magic-link subject, ADR 0002 Decision A) and when the
/// account was created - nothing else. No password, no OAuth, no name / birthdate
/// / address / phone, and NO reference to any player / nickname / room / session
/// (AC-03): this record is scoped to "who bought this", never "who played". The
/// email is adult data (a purchaser), so no age-of-consent flow attaches to it
/// (AC-05).
/// </summary>
/// <param name="Email">The purchaser's email - the magic-link identity and the ONLY identifying field on the account (AC-01). Stored normalized (trim + lowercase-invariant) by the store so the same address resolves to one account.</param>
/// <param name="CreatedUtc">When the account was first created (at purchase time, AC-02). A plain timestamp - not PII, not a player reference.</param>
public sealed record Account(string Email, DateTimeOffset CreatedUtc);
