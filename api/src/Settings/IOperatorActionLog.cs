// ----------------------------------------------------------------------------
//  IOperatorActionLog - the append-only operator action-log WRITE seam (declared here
//  by control-plane/01, issue #197; the STORE is sysadmin-console/06's to build).
//
//  WHY THIS LIVES HERE, NOW (ADR 0003 Amendment 2 + Security posture): every settings
//  PUT / DELETE must append exactly one action-log row on success (AC-09) - "every
//  settings change is logged NOW, not deferred." That is THIS story's requirement, and
//  it is one of the log's day-one writers alongside the money / moderation call sites.
//  But the log's Table Storage store, retention cap, and console view are
//  sysadmin-console/06's job, which lands in a LATER wave.
//
//  DEPENDENCY-TOLERANT BY DESIGN: so this story does not hard-block on 06, it declares
//  its own NARROW copy of the seam (one AppendAsync method - the exact shape 06 defines)
//  and registers a WORKING in-memory implementation in Program.cs. SettingsController
//  always has something to call. When 06 merges its TableStorageOperatorActionLog, the
//  DI registration swaps to the real store with ZERO change to the controller's call
//  sites. If 06 lands first, this story simply references its concrete interface instead.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Settings;

/// <summary>
/// Appends one row to the operator action log (control-plane/01 AC-09; ADR 0003 Amendment 2).
/// One row per completed, effectful operator action (a grant / revoke, a takedown, a Stripe
/// flip, a settings change). Only successful actions are logged - a rejected PUT (bounds or
/// confirmation) writes no row. The narrow seam sysadmin-console/06 backs with a durable store;
/// until then a working in-memory implementation stands in (dependency-tolerant).
/// </summary>
public interface IOperatorActionLog
{
    /// <summary>
    /// Appends one action-log row: WHO (<paramref name="operatorEmail"/>), WHAT
    /// (<paramref name="action"/>, e.g. <c>settings.put</c> / <c>settings.delete</c>), the TARGET
    /// (<paramref name="target"/>, e.g. the settings key), and a free-text
    /// <paramref name="note"/> (e.g. <c>"3 -&gt; 7"</c> or <c>"reverted to default"</c>). The
    /// implementation stamps its own timestamp. Called AFTER a successful write, never on a
    /// rejected one (AC-09).
    /// </summary>
    Task AppendAsync(string operatorEmail, string action, string target, string note, CancellationToken ct = default);
}

/// <summary>
/// One captured action-log row (control-plane/01's interim seam). Carries only operator-plane
/// facts - operator email, action, target, note, timestamp - never a player / room / session
/// reference. Superseded by sysadmin-console/06's stored row shape when its durable store lands.
/// </summary>
/// <param name="OperatorEmail">The operator who performed the action.</param>
/// <param name="Action">The action verb (e.g. <c>settings.put</c>).</param>
/// <param name="Target">What the action targeted (e.g. the settings key).</param>
/// <param name="Note">Free-text detail (e.g. the old -&gt; new value).</param>
/// <param name="TimestampUtc">When the row was appended (UTC).</param>
public sealed record OperatorActionLogEntry(
    string OperatorEmail,
    string Action,
    string Target,
    string Note,
    DateTimeOffset TimestampUtc);

/// <summary>
/// The working in-memory <see cref="IOperatorActionLog"/> that stands in until
/// sysadmin-console/06's durable Table Storage store lands (control-plane/01). NOT a no-op: it
/// records rows in memory so AC-09 is exercisable end to end with zero dependency on 06. Thread-
/// safe. Not durable across a restart - that is 06's store's job. The DI registration swaps to
/// the real store with no call-site change once 06 merges.
/// </summary>
public sealed class InMemoryOperatorActionLog : IOperatorActionLog
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<OperatorActionLogEntry> _entries = new();

    /// <summary>The rows appended so far (a detached snapshot), newest last. For inspection / tests.</summary>
    public IReadOnlyList<OperatorActionLogEntry> Entries => _entries.ToArray();

    /// <inheritdoc />
    public Task AppendAsync(string operatorEmail, string action, string target, string note, CancellationToken ct = default)
    {
        _entries.Enqueue(new OperatorActionLogEntry(operatorEmail, action, target, note, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }
}
