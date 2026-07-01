// ----------------------------------------------------------------------------
//  Test fakes for the anonymous serve-log sink (story-selection/04).
//
//  Hand-rolled fakes (no mocking framework is wired into the harness), matching
//  the SpySafetyFilter / RecordingClients pattern the sibling GameHub test files
//  already use:
//
//    - FakeTelemetrySink     : records every ServeEvent it is handed in an
//                              in-memory list so a test can assert exactly what
//                              was served (AC-01, AC-02, AC-04). Never throws.
//    - ThrowingTelemetrySink : always throws from RecordServeAsync so a test can
//                              prove a broken sink NEVER faults StartRound or the
//                              controller response (AC-03).
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Tests;

/// <summary>Records every serve event in memory; never throws (AC-01/AC-02/AC-04).</summary>
public sealed class FakeTelemetrySink : ITelemetrySink
{
    private readonly List<ServeEvent> _events = [];
    private readonly object _gate = new();

    /// <summary>A detached snapshot of the events recorded so far.</summary>
    public IReadOnlyList<ServeEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public Task RecordServeAsync(ServeEvent serveEvent, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _events.Add(serveEvent);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Always throws from RecordServeAsync so a test can prove gameplay is unaffected (AC-03).</summary>
public sealed class ThrowingTelemetrySink : ITelemetrySink
{
    public Task RecordServeAsync(ServeEvent serveEvent, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated serve-log sink failure (must never gate gameplay).");
}
