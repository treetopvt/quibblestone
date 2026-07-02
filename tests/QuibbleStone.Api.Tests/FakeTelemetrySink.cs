// ----------------------------------------------------------------------------
//  Test fakes for the anonymous serve-log + feedback-vote sink
//  (story-selection/04, story-selection/05).
//
//  Hand-rolled fakes (no mocking framework is wired into the harness), matching
//  the SpySafetyFilter / RecordingClients pattern the sibling GameHub test files
//  already use:
//
//    - FakeTelemetrySink     : records every ServeEvent AND FeedbackEvent it is
//                              handed in in-memory lists so a test can assert
//                              exactly what was served / voted on (AC-01, AC-02,
//                              AC-04). Never throws.
//    - ThrowingTelemetrySink : always throws from both record methods so a test
//                              can prove a broken sink NEVER faults StartRound or
//                              the controller response (AC-03/AC-05).
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Tests;

/// <summary>Records every serve/feedback event in memory; never throws (AC-01/AC-02/AC-04).</summary>
public sealed class FakeTelemetrySink : ITelemetrySink
{
    private readonly List<ServeEvent> _events = [];
    private readonly List<FeedbackEvent> _feedbackEvents = [];
    private readonly object _gate = new();

    /// <summary>A detached snapshot of the serve events recorded so far.</summary>
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

    /// <summary>A detached snapshot of the feedback (thumbs) events recorded so far (story-selection/05).</summary>
    public IReadOnlyList<FeedbackEvent> FeedbackEvents
    {
        get
        {
            lock (_gate)
            {
                return _feedbackEvents.ToArray();
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

    public Task RecordFeedbackAsync(FeedbackEvent feedbackEvent, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _feedbackEvents.Add(feedbackEvent);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Always throws from both record methods so a test can prove gameplay is unaffected (AC-03/AC-05).</summary>
public sealed class ThrowingTelemetrySink : ITelemetrySink
{
    public Task RecordServeAsync(ServeEvent serveEvent, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated serve-log sink failure (must never gate gameplay).");

    public Task RecordFeedbackAsync(FeedbackEvent feedbackEvent, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated feedback-vote sink failure (must never gate gameplay).");
}
