// ----------------------------------------------------------------------------
//  IEntitlementService - the entitlement seam consumed ONCE at session-creation
//  (ai-cost-gate STORY 02, issue #121).
//
//  WHY THIS FILE EXISTS (and what it deliberately is NOT):
//  The AI cost gate's second piece (ADR 0001, ROADMAP "The AI cost gate") is a
//  SINGLE entitlement check, evaluated exactly once when a room/solo session is
//  minted, that decides which AI capabilities that session may use. The real
//  owner of this seam is `billing-entitlements/01` (issue #70) - the accounts +
//  Stripe + capability-catalog chain. That chain is NOT built yet, and this thin
//  slice must not block on it or pull it in.
//
//  So this is option (b) from the story's Technical Notes: a THIN,
//  contract-compatible, DEFAULT-UNLOCKED implementation shaped EXACTLY to #70's
//  public contract (`EvaluateForSession(purchaserIdentity?) -> SessionEntitlements`),
//  which #70 later SUBSUMES without any consumer refactor. The public shape here
//  is #70's shape; when #70 lands it replaces DefaultUnlockedEntitlementService
//  with the real (stored-value) evaluation and consumers (GameHub.CreateRoom)
//  never change. It is NOT a rival catalog or a parallel gate - it CONSUMES/
//  reserves the `ai.*` capability key the jumble will use; #70's real catalog
//  supersedes EntitlementCatalog below.
//
//  ALPHA BEHAVIOUR (ADR 0001 decision C): the AI jumble is FREE for everyone in
//  alpha. This check therefore evaluates DEFAULT-UNLOCKED and does NOT block the
//  jumble - the runtime cost control is the rate-limit/quota (story 03) and the
//  spend circuit-breaker (story 04), never this entitlement. The seam is still
//  built + the `ai.*` key reserved so real charging attaches to the SAME place
//  later without a refactor (retrofitting an entitlement dimension onto an
//  anonymous, per-session AI flow later is painful - CLAUDE.md section 6).
//
//  ANONYMOUS, PER SESSION (README section 6): the check keys off the anonymous
//  room/solo session, NEVER a player identity or PII. In alpha there are no
//  accounts, so the purchaser identity is always null and the default-unlocked
//  set is returned. It meters COMPUTE per session, not identity.
//
//  SINGLE CALL SITE (AC-06): the ONLY session-creation call site that exists in
//  the server today is GameHub.CreateRoom (the room/group session mint). Solo
//  play in this codebase is CLIENT-DRIVEN and has NO server-side session mint
//  (solo only forwards anonymous telemetry beacons via UsageController - there is
//  no solo Room), so there is nowhere on the server to evaluate a solo session at
//  creation TODAY. The seam + default-unlocked service is what ships now; when a
//  solo AI proxy call is introduced in a later feature, that feature evaluates the
//  entitlement at the point the solo AI call is made (the same contract, the same
//  default-unlocked result). We deliberately do NOT invent a fake solo server
//  session here. No entitlement check belongs in any per-call/tap/round/reveal
//  path (AC-01 restated as a code-location guard).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// The reserved AI capability-key catalog (ai-cost-gate/02, AC-02). This is a
/// THIN reservation of the key(s) the AI jumble consumes - NOT a rival catalog.
/// `billing-entitlements/01` (#70) owns the authoritative capability catalog and
/// SUPERSEDES this type when it lands; the key string(s) here are chosen to match
/// what #70 will define so the reservation carries forward unchanged.
/// </summary>
public static class EntitlementCatalog
{
    /// <summary>
    /// The AI on-demand word-bank capability the "Fresh Runes" jumble uses
    /// (ai-on-demand-generation / game-modes/07). Reserved here so real charging
    /// can attach to this exact key later. Namespaced under <c>ai.</c> so every
    /// future AI capability (verdict, voice, illustration) is a sibling key, not a
    /// new gate.
    /// </summary>
    public const string AiOnDemand = "ai.onDemand";

    /// <summary>
    /// All reserved <c>ai.*</c> capability keys - the set the alpha default-unlocked
    /// service grants. #70's real catalog supersedes this list.
    /// </summary>
    public static readonly IReadOnlyList<string> AiCapabilities = new[] { AiOnDemand };
}

/// <summary>
/// The immutable set of capabilities UNLOCKED for one anonymous session, captured
/// once at session-creation and read for the session's lifetime (never
/// re-evaluated - AC-01). This is the value #70's <c>EvaluateForSession</c>
/// returns; consumers only ever ask <see cref="IsUnlocked"/>. Answers unlocked /
/// not - never "how many left" (that is the quota's job, story 03).
/// </summary>
public sealed class SessionEntitlements
{
    private readonly HashSet<string> _unlocked;

    /// <summary>
    /// Builds a session-entitlement set over the given unlocked capability keys.
    /// Keys are compared ordinally (they are fixed catalog constants, not user
    /// text). A capability NOT in the set reads as locked.
    /// </summary>
    /// <param name="unlockedCapabilityKeys">The capability keys unlocked for this session.</param>
    public SessionEntitlements(IEnumerable<string> unlockedCapabilityKeys)
    {
        _unlocked = new HashSet<string>(unlockedCapabilityKeys, StringComparer.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="capabilityKey"/> is unlocked for this session. In
    /// alpha (default-unlocked) the reserved <c>ai.*</c> keys are always unlocked,
    /// so the jumble is reachable by every session (AC-03). Non-blocking by design:
    /// callers READ this; the real runtime gate is quota + breaker (AC-04).
    /// </summary>
    /// <param name="capabilityKey">A catalog capability key, e.g. <see cref="EntitlementCatalog.AiOnDemand"/>.</param>
    public bool IsUnlocked(string capabilityKey) => _unlocked.Contains(capabilityKey);

    /// <summary>The unlocked capability keys (a detached, read-only view).</summary>
    public IReadOnlyCollection<string> UnlockedCapabilities => _unlocked;
}

/// <summary>
/// Evaluates the AI entitlements for one session, exactly once, at
/// session-creation (ai-cost-gate/02). This is the public contract
/// `billing-entitlements/01` (#70) defines and later implements for real;
/// <see cref="DefaultUnlockedEntitlementService"/> is the thin alpha stand-in.
/// </summary>
public interface IEntitlementService
{
    /// <summary>
    /// Evaluates which capabilities this session may use. Called ONCE when a
    /// room/solo session is minted; the result is captured on the session and read
    /// for its lifetime (never re-evaluated per tap/round/AI call - AC-01).
    /// </summary>
    /// <param name="purchaserIdentity">
    /// The optional purchaser identity. Alpha has NO accounts, so this is always
    /// null (anonymous, per session - README section 6, AC-05); it exists only so
    /// #70 can key a real grant off a purchaser later WITHOUT changing this
    /// signature. A null identity returns the default-unlocked set.
    /// </param>
    /// <returns>The session's unlocked capability set.</returns>
    SessionEntitlements EvaluateForSession(string? purchaserIdentity = null);
}

/// <summary>
/// The thin, alpha, DEFAULT-UNLOCKED entitlement service (ai-cost-gate/02, AC-03).
/// Every session - purchaser or not - gets the reserved <c>ai.*</c> capabilities
/// UNLOCKED, so shipping this changes ZERO observed behavior (ADR 0001 decision
/// C): the jumble is free for everyone and its real gating is quota (03) + the
/// spend breaker (04). Stateless -> registered as a singleton in Program.cs.
/// `billing-entitlements/01` (#70) REPLACES this with the real stored-value
/// evaluation against the same <see cref="IEntitlementService"/> contract; turning
/// a capability from unlocked to entitlement-required then becomes a stored-value
/// flip, not new gating code.
/// </summary>
public sealed class DefaultUnlockedEntitlementService : IEntitlementService
{
    /// <inheritdoc />
    public SessionEntitlements EvaluateForSession(string? purchaserIdentity = null)
        // Ignore the (always-null in alpha) purchaser identity: default-unlocked
        // grants every reserved ai.* capability regardless (anonymous, per session).
        => new SessionEntitlements(EntitlementCatalog.AiCapabilities);
}
