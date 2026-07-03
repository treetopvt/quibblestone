// ----------------------------------------------------------------------------
//  Test doubles for the spend circuit-breaker (ai-cost-gate/04, #123).
//
//  Hand-rolled fakes (no mocking framework is wired into the harness), matching the
//  FakeTelemetrySink / SpySafetyFilter pattern the sibling test files use:
//    - FakeMonthlySpendStore  : an in-memory IMonthlySpendStore keyed by UTC month
//                               so a test can seed a total, prove increments round-
//                               trip and survive a "restart", flip a month, or
//                               simulate an UNREADABLE total (null) / a throwing
//                               write (AC-02/AC-03/AC-09).
//    - FixedTimeProvider      : a settable clock so a test can drive the breaker
//                               across a UTC month boundary without waiting (AC-03).
//    - RecordingTelemetryChannel : captures the App Insights items a TelemetryClient
//                               sends so a test can assert EXACTLY ONE attribution
//                               event with the expected anonymous shape (AC-05).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using QuibbleStone.Api.Ai;

namespace QuibbleStone.Api.Tests.Ai;

/// <summary>
/// In-memory <see cref="IMonthlySpendStore"/> keyed by UTC month. Seed a starting
/// total, then read it back (round-trip / survives-restart), increment it, or set
/// <see cref="Unreadable"/> to simulate a down store (read returns null -&gt; the
/// breaker fails safe) or <see cref="ThrowOnWrite"/> to prove a failed write never
/// faults the caller.
/// </summary>
public sealed class FakeMonthlySpendStore : IMonthlySpendStore
{
    private readonly Dictionary<string, decimal> _totals = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>When true, reads return null (the total genuinely cannot be read - AC-09).</summary>
    public bool Unreadable { get; set; }

    /// <summary>When true, writes throw so a test can prove a failed write never faults the call (AC-09).</summary>
    public bool ThrowOnWrite { get; set; }

    /// <summary>Seeds a month's starting total (e.g. to simulate spend already accrued).</summary>
    public FakeMonthlySpendStore Seed(string monthKey, decimal totalUsd)
    {
        lock (_gate)
        {
            _totals[monthKey] = totalUsd;
        }

        return this;
    }

    /// <summary>Reads a month's total directly (for assertions); 0 when absent.</summary>
    public decimal TotalFor(string monthKey)
    {
        lock (_gate)
        {
            return _totals.TryGetValue(monthKey, out var total) ? total : 0m;
        }
    }

    public Task<decimal?> TryReadMonthTotalUsdAsync(string monthKey, CancellationToken cancellationToken = default)
    {
        if (Unreadable)
        {
            return Task.FromResult<decimal?>(null);
        }

        lock (_gate)
        {
            var total = _totals.TryGetValue(monthKey, out var value) ? value : 0m;
            return Task.FromResult<decimal?>(total);
        }
    }

    public Task AddAsync(string monthKey, decimal amountUsd, CancellationToken cancellationToken = default)
    {
        if (ThrowOnWrite)
        {
            throw new InvalidOperationException("Simulated monthly-spend write failure (must never gate gameplay).");
        }

        if (amountUsd <= 0m)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            _totals[monthKey] = (_totals.TryGetValue(monthKey, out var value) ? value : 0m) + amountUsd;
        }

        return Task.CompletedTask;
    }
}

/// <summary>A settable UTC clock so a test can move the breaker across a month boundary (AC-03).</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

/// <summary>
/// Captures every telemetry item a <see cref="TelemetryClient"/> sends so a test can
/// assert the attribution event's shape (AC-05). Build a client over it with
/// <see cref="CreateClient"/>.
/// </summary>
public sealed class RecordingTelemetryChannel : ITelemetryChannel
{
    private readonly List<ITelemetry> _sent = new();
    private readonly object _gate = new();

    public bool? DeveloperMode { get; set; }

    public string? EndpointAddress { get; set; }

    /// <summary>A detached snapshot of the items sent so far.</summary>
    public IReadOnlyList<ITelemetry> Sent
    {
        get
        {
            lock (_gate)
            {
                return _sent.ToArray();
            }
        }
    }

    public void Send(ITelemetry item)
    {
        lock (_gate)
        {
            _sent.Add(item);
        }
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Builds a <see cref="TelemetryClient"/> that sends into this channel. A dummy
    /// instrumentation key keeps the client enabled so Track calls reach the channel;
    /// nothing leaves the process (the channel just records).
    /// </summary>
    public static (TelemetryClient client, RecordingTelemetryChannel channel) CreateClient()
    {
        var channel = new RecordingTelemetryChannel();
        var configuration = new TelemetryConfiguration
        {
            TelemetryChannel = channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
        };
        return (new TelemetryClient(configuration), channel);
    }
}
