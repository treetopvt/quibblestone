// ----------------------------------------------------------------------------
//  SeatPreset - a kid SEAT PRESET (accounts-identity/08, issue #228; ADR 0003
//  Decision 1 and its "the kid-profile boundary" section).
//
//  WHAT THIS IS (and, more importantly, the firm edge it must NEVER cross):
//  A seat preset is a join-time CONVENIENCE an adult sets up once, so a kid does
//  not have to re-type their name and re-pick their Guardian every car ride. It is
//  a named (nickname + Guardian variant) shortcut stored under the FAMILY account -
//  nothing more. It is DELIBERATELY NOT a kid identity:
//
//    - "A kid profile is a SEAT PRESET, never an identity" (ADR 0003). Selecting a
//      preset in the join flow is EXACTLY equivalent to typing that nickname and
//      picking that Guardian by hand: it only fills the SAME display-name / variant
//      controls the manual path already uses and submits through the SAME
//      CreateRoom / JoinRoom hub invokes. Nothing preset-related lands on Room or
//      Player; the server cannot tell a preset join from a manual join (AC-03).
//    - No per-preset history, no per-preset gallery (the keepsake vault is
//      FAMILY-level, never per-preset), no per-preset entitlements, no kid login,
//      no kid PII (AC-05). A preset holds ONLY the three fields below - if a future
//      feature wants per-kid anything, that is a new ADR, not a slide here.
//    - The nickname is a nickname, subject to the EXACT SAME length cap + server-
//      side content-safety filter as any manually typed display name (AC-04/AC-07),
//      vetted server-side before it is stored (the preset endpoints on
//      AccountsController) AND again, independently, at join time (the hub's
//      unchanged filter). A preset name is never trusted or pre-approved
//      client-side.
//
//  THE ACCOUNT-PLANE CARVE-OUT (ADR 0003, finding #5): a preset necessarily
//  associates a chosen nickname with a family AccountId (the store partitions by
//  it). That is NOT a play-plane-invariant violation - it is the ADR's explicit
//  account-plane carve-out: adult-owned, adult-consented household data, created
//  only by a signed-in adult managing their own family's presets, never harvested
//  from play and never surfaced to co-players. The play-plane invariant (a preset
//  join is byte-for-byte indistinguishable from a manual join) is upheld structurally
//  by there being NO field on Room / Player to carry a preset reference.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A kid seat preset (accounts-identity/08): a named (nickname + Guardian variant)
/// join-time shortcut stored under a family account. Holds ONLY a stable
/// <see cref="Id"/>, a <see cref="Nickname"/> (free text, same cap + safety filter
/// as any display name), and a Guardian <see cref="Variant"/> - nothing else (AC-01/
/// AC-05). It is a SEAT PRESET, never an identity: it carries no history, gallery,
/// entitlement, login, or PII, and nothing about it ever lands on Room / Player
/// (ADR 0003's kid-profile boundary).
/// </summary>
/// <param name="Id">The stable preset id (a GUID minted once at creation). Scopes edit / delete under the owning account; it is an account-plane row key, never seen by co-players and never on any broadcast.</param>
/// <param name="Nickname">The preset's display name - free text, capped at the same max length as any display name and vetted by the SAME server-side safety filter before it is stored (AC-04/AC-07). Doubles as the preset's label in the manager UI (a preset is "named" by its nickname).</param>
/// <param name="Variant">The chosen Guardian variant, normalized server-side to one of the six known values (defaulting to "teal") exactly as the join path normalizes a manually chosen variant.</param>
public sealed record SeatPreset(Guid Id, string Nickname, string Variant);
