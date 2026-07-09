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
//  the stable AccountId (see IEntitlementGrantStore), never by a raw email.
//
//  RECOVERY + SUPPORT METADATA (billing-entitlements/08, ADR 0003 Layer 2): a grant
//  additionally carries a GrantId (a fresh GUID stamped per write - identifies THIS
//  write, not the whole lease history), the PlanId (the ProductCatalog product id
//  that produced it), the StripeSubscriptionId (for a subscription-sourced grant),
//  and the Mode (the Stripe mode - Live / Test - that produced it). These make two
//  things possible that the bare lease could not: telling "which purchase produced
//  this row / which live subscription it tracks," and a mode-safe per-account resync
//  from Stripe (the Mode dimension is what stops a Test-mode resync from ever
//  overwriting a Live-derived grant - ADR 0003 "Stripe resync cannot corrupt
//  grants"). None of them feed the session-creation read (that still only reads the
//  lease window) and none of them is PII.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Billing;

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
/// A single capability lease held by a purchaser (billing-entitlements/01, AC-05;
/// grant metadata added by billing-entitlements/08). Immutable value: the
/// session-creation evaluation reads only the lease window, story 03's webhook and
/// story 08's resync replace it (upsert by capability key) to extend / lapse the
/// lease. Holds the capability key, the lease end, the source, and the recovery /
/// support metadata (grant id, plan id, Stripe subscription id, mode) - no PII, no
/// player/room reference (AC-03 spirit).
/// </summary>
/// <param name="CapabilityKey">A catalog capability key (e.g. <see cref="EntitlementCatalog.LibraryFull"/> or <see cref="EntitlementCatalog.Pack"/>).</param>
/// <param name="ValidThrough">The lease end. Null = permanent (a one-time pack); a value = active until that instant (a subscription period, extended on renewal).</param>
/// <param name="Source">How the grant was obtained (subscription / one-time / operator).</param>
/// <param name="PlanId">The <c>ProductCatalog</c> product id that produced this grant (e.g. "family-plan", "pack.spooky"); null for a grant with no known product (a legacy row or a bespoke operator comp). Billing-entitlements/08 AC-01.</param>
/// <param name="StripeSubscriptionId">The Stripe subscription id for a subscription-sourced grant; null for a one-time pack or an operator grant. Billing-entitlements/08 AC-01.</param>
/// <param name="Mode">The Stripe mode (<see cref="StripeMode.Live"/> / <see cref="StripeMode.Test"/>) that produced this grant; null ONLY for a <see cref="GrantSource.Operator"/> comp (no Stripe transaction behind it). The mode-safety dimension the resync guard reads (AC-08). Billing-entitlements/08 AC-01.</param>
public sealed record EntitlementGrant(
    string CapabilityKey,
    DateTimeOffset? ValidThrough,
    GrantSource Source,
    string? PlanId = null,
    string? StripeSubscriptionId = null,
    StripeMode? Mode = null)
{
    /// <summary>
    /// A fresh GUID identifying THIS write (billing-entitlements/08 AC-01) - not the
    /// whole lease history. Minted per construction unless a caller sets it explicitly
    /// (the grant store does so when rehydrating a stored row, so a persisted GrantId
    /// survives a read; a legacy row with no stored id mints a fresh one on read, AC-03).
    /// An <c>init</c> property rather than a positional parameter so every existing
    /// construction site auto-stamps one without threading a GUID through it.
    /// </summary>
    public Guid GrantId { get; init; } = Guid.NewGuid();

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
