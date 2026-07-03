// ----------------------------------------------------------------------------
//  EntitlementGrant - the lease-shaped record of "this purchaser holds this
//  capability, until this moment" (billing-entitlements/01, issue #70, AC-05).
//
//  WHY A LEASE (validThrough), NOT A BOOLEAN: a subscription grant is time-bound
//  and gets EXTENDED on renewal (story 03's invoice.paid) or allowed to LAPSE on
//  cancel / past-due-grace expiry (ADR 0002 Decisions C/D). A one-time pack is
//  permanent, expressed as a null validThrough. Modelling every grant as a lease
//  means the session-creation read is a single "is it active right now?" check and
//  the whole subscription lifecycle is just moving validThrough - no separate
//  "expired" flag, no delete-on-cancel race.
//
//  SCOPED TO "WHO BOUGHT THIS", NEVER "WHO PLAYED": a grant carries a capability
//  key, a lease end, and a source - never a player / nickname / room reference
//  (that is the anonymity firewall, README section 6). It is stored partitioned by
//  a HASH of the purchaser identity (see IEntitlementGrantStore), never by a raw
//  email or a guessable id.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// Where an <see cref="EntitlementGrant"/> came from (ADR 0002 Decision C). Drives
/// nothing in the session-creation read (that only cares about the lease window),
/// but is recorded so the sysadmin console (#136) and story 05's restore view can
/// tell a subscription from a one-time pack from an operator comp.
/// </summary>
public enum GrantSource
{
    /// <summary>A recurring subscription (the family plan) - renewed by story 03's invoice.paid webhook.</summary>
    Subscription,

    /// <summary>A one-time purchase (an add-on pack) - permanent, so its lease never expires.</summary>
    OneTime,

    /// <summary>An operator-issued comp / support grant (sysadmin console, #136).</summary>
    Operator,
}

/// <summary>
/// A single capability lease held by a purchaser (billing-entitlements/01, AC-05).
/// Immutable value: the session-creation evaluation reads it, story 03's webhook
/// replaces it (upsert by capability key) to extend or lapse the lease. Holds only
/// the capability key, the lease end, and the source - no PII, no player/room
/// reference (AC-03 spirit).
/// </summary>
/// <param name="CapabilityKey">A catalog capability key (e.g. <see cref="EntitlementCatalog.LibraryFull"/> or <see cref="EntitlementCatalog.Pack"/>).</param>
/// <param name="ValidThrough">The lease end. Null = permanent (a one-time pack); a value = active until that instant (a subscription period, extended on renewal).</param>
/// <param name="Source">How the grant was obtained (subscription / one-time / operator).</param>
public sealed record EntitlementGrant(string CapabilityKey, DateTimeOffset? ValidThrough, GrantSource Source)
{
    /// <summary>
    /// True when this grant is active at <paramref name="instant"/>: a null
    /// <see cref="ValidThrough"/> is permanent (always active), otherwise the lease
    /// must not yet have passed. A grant exactly at its <see cref="ValidThrough"/>
    /// reads as EXPIRED (the lease end is exclusive), matching AC-04's "given
    /// validThrough has passed, then that grant's capability reads as locked".
    /// </summary>
    /// <param name="instant">The moment to evaluate against (session-creation time, UTC).</param>
    public bool IsActiveAt(DateTimeOffset instant) => ValidThrough is null || ValidThrough.Value > instant;
}
