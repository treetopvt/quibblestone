// ----------------------------------------------------------------------------
//  ConnectionEntitlementStore - the per-connection bridge from OnConnectedAsync's
//  one-time purchaser-credential resolution to a later CreateRoom on the SAME
//  connection (accounts-identity/06, ADR 0002 Decision F, issue #210).
//
//  WHY IT EXISTS (cold-builder-critical): SignalR builds a FRESH GameHub instance
//  per invocation (Program.cs's own RoomRegistry comment: "every transient GameHub
//  instance ... shares the SAME set of active rooms" - the same reasoning
//  RoomRegistry is a singleton for). So a hub INSTANCE field cannot carry a value
//  from OnConnectedAsync to a later CreateRoom - they run on different GameHub
//  instances. This process-wide singleton, keyed by Context.ConnectionId, is the
//  ONLY place the connect-time resolution can live so CreateRoom can read it back.
//
//  THE LOAD-BEARING INVARIANT, MADE STRUCTURAL (ADR 0003 "Security posture":
//  "Identity is discarded at the boundary, structurally", and ADR 0002's
//  load-bearing invariant): what this store holds is DELIBERATELY not an identity.
//  GameHub.OnConnectedAsync resolves the purchaser email, IMMEDIATELY evaluates the
//  session's capabilities from it, and stores ONLY the resulting SessionEntitlements
//  (plus a reserved AdultUnlocked bool, always false in this story - story 09
//  populates it) - never the email / AccountId / device-token id. The value type
//  below (ResolvedConnectionIdentity) has NO identity-shaped field at all, exactly
//  as Room.Entitlements enforces the same discipline on Room (accounts-identity/01
//  AC-02): no identity string is EVER a value keyed by ConnectionId alongside the
//  roster (AC-04 / AC-08). The identity string lives only in a local variable for
//  the duration of the single EvaluateForSession call that consumes it.
//
//  LIFECYCLE: written once in OnConnectedAsync (only for a connection that supplies
//  a purchaser access token - an anonymous connection stores nothing and CreateRoom
//  falls back to the default-unlocked baseline, AC-05), read once in CreateRoom, and
//  removed in OnDisconnectedAsync when the physical connection ends. A deliberate
//  LeaveRoom does NOT clear it: the shared SignalR connection stays open for the next
//  game, so its resolved capabilities remain valid until the connection itself drops.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The resolved, capability-ONLY outcome of a connection's connect-time purchaser
/// resolution. Carries EXACTLY the two capture-once values a room reads at
/// CreateRoom - the session's <see cref="SessionEntitlements"/> and a reserved
/// <paramref name="AdultUnlocked"/> boolean - and, deliberately, NOTHING that could
/// identify the purchaser (no email, AccountId, or device-token id). This shape is
/// the structural half of ADR 0002's load-bearing invariant (accounts-identity/06
/// AC-04 / AC-08): an identity string can never be stored keyed by a ConnectionId
/// because this type has no field to put one in.
/// </summary>
/// <param name="Capabilities">The session's unlocked capability set, resolved once in OnConnectedAsync.</param>
/// <param name="AdultUnlocked">
/// Reserved for accounts-identity/09's adult-signal logic; ALWAYS false in
/// accounts-identity/06 (this story only reserves the slot). Kept ORTHOGONAL to
/// <paramref name="Capabilities"/> - it is never folded into the capability set -
/// exactly as Room's two capture-once booleans stay separate (implementation.md).
/// </param>
public readonly record struct ResolvedConnectionIdentity(
    SessionEntitlements Capabilities,
    bool AdultUnlocked);

/// <summary>
/// The per-connection resolved-CAPABILITY lookup GameHub.CreateRoom reads (never an
/// identity lookup - identity never survives past the OnConnectedAsync call that
/// resolves it). A process-wide singleton (see the file header for why it CANNOT be
/// a hub instance field). accounts-identity/09 extends the stored value's
/// AdultUnlocked computation; it never forks a second store.
/// </summary>
public interface IConnectionEntitlementStore
{
    /// <summary>
    /// Records the connect-time resolved capabilities for a connection (called once
    /// from OnConnectedAsync, only when the connection supplied a purchaser token).
    /// </summary>
    /// <param name="connectionId">The SignalR connection id (Context.ConnectionId).</param>
    /// <param name="resolved">The capability-only resolution (no identity, by construction).</param>
    void Set(string connectionId, ResolvedConnectionIdentity resolved);

    /// <summary>
    /// Reads a connection's resolved capabilities, or null when none were stored (an
    /// anonymous connection, or one that never went through the resolve path) - the
    /// miss CreateRoom treats as the default-unlocked baseline (AC-05).
    /// </summary>
    /// <param name="connectionId">The SignalR connection id (Context.ConnectionId).</param>
    /// <returns>The resolved capabilities, or null on a miss.</returns>
    ResolvedConnectionIdentity? TryGet(string connectionId);

    /// <summary>Removes a connection's entry when the physical connection ends (OnDisconnectedAsync).</summary>
    /// <param name="connectionId">The SignalR connection id (Context.ConnectionId).</param>
    void Remove(string connectionId);
}

/// <summary>
/// In-memory <see cref="IConnectionEntitlementStore"/> over a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by connection id. Thread-safe
/// (SignalR invocations for different connections run concurrently) and process-wide,
/// mirroring <c>RoomRegistry</c>'s singleton, no-database posture (a toy, not a system
/// of record - CLAUDE.md section 10). Keys are compared ordinally (SignalR connection
/// ids are opaque server tokens, not user text).
/// </summary>
public sealed class ConnectionEntitlementStore : IConnectionEntitlementStore
{
    private readonly ConcurrentDictionary<string, ResolvedConnectionIdentity> _byConnection =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Set(string connectionId, ResolvedConnectionIdentity resolved) =>
        _byConnection[connectionId] = resolved;

    /// <inheritdoc />
    public ResolvedConnectionIdentity? TryGet(string connectionId) =>
        _byConnection.TryGetValue(connectionId, out var resolved) ? resolved : null;

    /// <inheritdoc />
    public void Remove(string connectionId) =>
        _byConnection.TryRemove(connectionId, out _);
}
