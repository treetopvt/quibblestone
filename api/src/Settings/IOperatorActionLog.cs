// ----------------------------------------------------------------------------
//  IOperatorActionLog - the append-only operator action-log seam. FIRST declared by
//  control-plane/01 (issue #197) as a narrow, dependency-tolerant WRITE contract;
//  LIT UP by sysadmin-console/06 (issue #233), which backs it with a durable
//  TableStorageOperatorActionLog and adds the reverse-chronological READ + retention
//  PRUNE the console view (AC-03) and the retention floor (AC-04) need.
//
//  ONE SEAM, NOT SEVERAL (06 AC-02): every money / moderation / settings call site
//  appends through THIS single interface - AdminEntitlementsController (grant / revoke),
//  ReportedTalesController (confirm / restore), StripeModeController (mode flip), and
//  SettingsController (settings put / delete, control-plane/01). No controller
//  reimplements its own logging and there is no second log store anywhere. The action
//  name is a FREE-FORM string (not a closed enum), so a future caller (story 07's
//  support verbs) starts appending its own rows with ZERO change here.
//
//  LOG-BEFORE-ACT (06 AC-01a): the money / moderation call sites call AppendAsync
//  BEFORE their effectful write, so an action can never commit with no trail. If the
//  append throws (store unavailable, or an invalid target - AC-07), the request aborts
//  BEFORE the effect runs. control-plane/01's settings call site keeps its own ordering
//  (that is its footprint, not this story's).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The operator action log: append one row per effectful money / moderation / settings action
/// (control-plane/01 + sysadmin-console/06; ADR 0003 Amendment 2), list the most recent rows
/// newest-first for the console view (06 AC-03), and prune rows past the age-based retention
/// floor (06 AC-04). A row carries ONLY operator-plane facts (operator email, action, target,
/// note, timestamp) - never a player / room / session reference (06 AC-06). Backed by a durable
/// Table Storage store in a deployed environment; by a working in-memory store otherwise.
/// </summary>
public interface IOperatorActionLog
{
    /// <summary>
    /// Appends one action-log row: WHO (<paramref name="operatorEmail"/>), WHAT
    /// (<paramref name="action"/>, e.g. <c>entitlement.grant</c> / <c>settings.put</c>), the
    /// TARGET (<paramref name="target"/>, e.g. a purchaser email, a tale slug, a settings key, or
    /// the literal <c>"stripe-mode"</c>), and a free-text <paramref name="note"/> (e.g.
    /// <c>"3 -&gt; 7"</c>). The implementation stamps its own UTC timestamp. Validates the target
    /// first (06 AC-07): an empty / over-long target, or an email-shaped target that does not
    /// parse as a well-formed address, throws <see cref="InvalidOperatorActionTargetException"/>
    /// so a log-before-act call site aborts before its effect runs.
    /// </summary>
    Task AppendAsync(string operatorEmail, string action, string target, string note, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent action-log rows in REVERSE-CHRONOLOGICAL order (newest first),
    /// capped at <paramref name="maxItems"/> (06 AC-03). The console's Operations view is the only
    /// caller. Reads only operator-plane rows (AC-06); never scans-and-sorts a whole partition in
    /// the caller (the durable store keys rows so a bounded ascending read is already newest-first).
    /// </summary>
    /// <param name="maxItems">The page cap (e.g. 200) - the view never pulls an unbounded history.</param>
    Task<IReadOnlyList<OperatorActionLogEntry>> ListRecentAsync(int maxItems, CancellationToken ct = default);

    /// <summary>
    /// Prunes rows OLDER than the effective retention horizon (06 AC-04) and returns how many were
    /// removed. The horizon is <see cref="OperatorActionLogPolicy.ClampRetentionDays"/> applied to
    /// <paramref name="configuredRetentionDays"/> - a null / below-floor value is clamped UP to
    /// <see cref="OperatorActionLogPolicy.MinRetentionDays"/>, so no runtime setting can evict a row
    /// still within the floor, regardless of volume or a hostile lower value. There is no scheduled
    /// pruner in this story; this is the on-demand / future-scheduler entry point (and the AC-04 test seam).
    /// </summary>
    /// <param name="configuredRetentionDays">A future control-plane/03 retention override, or null when none is configured.</param>
    Task<int> PruneAsync(int? configuredRetentionDays, CancellationToken ct = default);
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
/// The working in-memory <see cref="IOperatorActionLog"/> used when no storage connection string
/// is configured (local dev / CI / a fresh clone). NOT a no-op: it records rows in memory so every
/// AC is exercisable end to end with ZERO Azure setup - same append validation (AC-07), same
/// newest-first listing (AC-03), and same retention-floor pruning semantics (AC-04) as the durable
/// TableStorageOperatorActionLog; only durability across a restart differs. Thread-safe. The DI
/// registration swaps to the durable store with no call-site change once a connection string exists.
/// </summary>
public sealed class InMemoryOperatorActionLog : IOperatorActionLog
{
    // Guards the ordered list against concurrent append / prune / read (a plain List is not
    // thread-safe, and pruning mutates mid-list, which a ConcurrentQueue cannot express).
    private readonly object _gate = new();
    private readonly List<OperatorActionLogEntry> _entries = new();
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Constructs the store over the wall clock (the DI default).</summary>
    public InMemoryOperatorActionLog() : this(() => DateTimeOffset.UtcNow) { }

    /// <summary>
    /// Constructs the store over an injectable clock. Production uses the wall-clock default above;
    /// a test injects a controllable clock to seed rows at chosen ages and assert the retention
    /// floor (AC-04) without waiting real months.
    /// </summary>
    public InMemoryOperatorActionLog(Func<DateTimeOffset> clock) => _clock = clock;

    /// <summary>The rows appended so far (a detached snapshot), newest last. For inspection / tests.</summary>
    public IReadOnlyList<OperatorActionLogEntry> Entries
    {
        get { lock (_gate) { return _entries.ToArray(); } }
    }

    /// <inheritdoc />
    public Task AppendAsync(string operatorEmail, string action, string target, string note, CancellationToken ct = default)
    {
        // Validate BEFORE recording (AC-07): a malformed / markup-bearing email-shaped target is
        // refused at write time. Because the money / moderation call sites append BEFORE their
        // effect (log-before-act), this throw aborts the action rather than persisting a bad row.
        if (!OperatorActionLogPolicy.IsValidTarget(target))
        {
            throw new InvalidOperatorActionTargetException($"'{target}' is not a valid action-log target.");
        }

        lock (_gate)
        {
            _entries.Add(new OperatorActionLogEntry(operatorEmail, action, target, note, _clock()));
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OperatorActionLogEntry>> ListRecentAsync(int maxItems, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<OperatorActionLogEntry> recent = _entries
                .OrderByDescending(e => e.TimestampUtc) // newest first (AC-03)
                .Take(Math.Max(0, maxItems))
                .ToArray();
            return Task.FromResult(recent);
        }
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(int? configuredRetentionDays, CancellationToken ct = default)
    {
        // Clamp UP to the floor (AC-04): a null / below-floor value can never shorten retention.
        var cutoff = _clock() - TimeSpan.FromDays(OperatorActionLogPolicy.ClampRetentionDays(configuredRetentionDays));
        lock (_gate)
        {
            var removed = _entries.RemoveAll(e => e.TimestampUtc < cutoff);
            return Task.FromResult(removed);
        }
    }
}
