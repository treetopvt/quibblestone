// ----------------------------------------------------------------------------
//  SeatGraceService - the one scheduled timer in QuibbleStone (session-engine/07).
//
//  "Don't Lose the Room": a car dead zone, a phone lock, or a brief network blip
//  is a TRANSIENT drop, not a deliberate departure (README section 1). So a
//  dropped connection no longer evicts its seat on the spot - GameHub.OnDisconnected
//  HOLDS the seat (marks it disconnected, keeps it on the roster) and hands this
//  service a one-shot timer to run the eventual eviction ONLY if the grace window
//  elapses with no reconnect.
//
//  Why a scheduled push, not the lazy on-access sweep RoomRegistry.SweepExpired
//  uses for the 30-minute idle window: a 30-SECOND grace window has OTHER seated
//  players actively waiting on the dropped seat's blanks. If nobody else calls the
//  hub in the interim, a lazy "recompute on next read" would leave them waiting
//  forever - so eviction must be PUSHED. This is the single place in the codebase a
//  timer is justified over lazy-on-access (feature.md Decisions log, 2026-07-03).
//
//  Mechanism (deliberately NOT Task.Delay(...).ContinueWith(...) - ContinueWith's
//  default-scheduler + unobserved-exception semantics are a footgun for a
//  fire-and-forget timer):
//    - await Task.Delay(graceWindow, token) under the seat's CancellationToken
//      (the source lives on the Room; story 08's Rejoin cancels it to keep the seat);
//    - on cancellation, the seat reconnected - do nothing;
//    - on elapse, re-check UNDER the room lock (RoomRegistry.ReleaseGraceSeat ->
//      Room.TryReleaseSeat) that the SAME disconnect episode is still pending, then
//      evict + (if the round was "prompting") abort + re-broadcast the roster,
//      exactly today's leave behavior, just deferred (AC-03);
//    - the whole run is wrapped so a fault is LOGGED, never left unobserved.
//
//  Broadcasting after the hub invocation has ended requires IHubContext<GameHub>
//  (the hub's own Clients are only valid during an invocation), so the epilogue is
//  the SAME shared GameHub.BroadcastPlayerLeftAsync the live LeaveRoom path uses -
//  no forked, mode-specific reconnect logic ("one engine, many thin modes").
//
//  The grace window is a SINGLE named constant (DefaultGraceWindow = 30s) so it is
//  trivially tunable after playtesting, and injectable via the test constructor so
//  a spec can drive a tiny window instead of waiting 30 real seconds.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.SignalR;
using QuibbleStone.Api.Hubs;

namespace QuibbleStone.Api.Rooms;

/// <summary>
/// Schedules and runs the one-shot delayed eviction of a seat held through a
/// disconnect grace window (session-engine/07). A process-wide singleton (like
/// <see cref="RoomRegistry"/>): a fresh hub is built per invocation, so the timer
/// cannot live on the hub. Uses <see cref="IHubContext{GameHub}"/> to broadcast the
/// grace-expiry epilogue after the originating invocation has ended.
/// </summary>
public sealed class SeatGraceService
{
    /// <summary>
    /// The grace window a dropped seat is held before eviction (session-engine/07).
    /// A single named constant, chosen at 30 seconds as a starting default (a short
    /// tunnel / phone-lock blip, not "went inside a store" - feature.md's open
    /// decision, resolved here) so it is cheap to tune after playtesting.
    /// </summary>
    public static readonly TimeSpan DefaultGraceWindow = TimeSpan.FromSeconds(30);

    private readonly IHubContext<GameHub> _hub;
    private readonly RoomRegistry _rooms;
    private readonly TelemetryClient _appInsights;
    private readonly ILogger<SeatGraceService> _logger;
    private readonly TimeSpan _graceWindow;

    /// <summary>The DI constructor: the 30-second <see cref="DefaultGraceWindow"/>.</summary>
    public SeatGraceService(
        IHubContext<GameHub> hub,
        RoomRegistry rooms,
        TelemetryClient appInsights,
        ILogger<SeatGraceService> logger)
        : this(hub, rooms, appInsights, logger, DefaultGraceWindow)
    {
    }

    /// <summary>
    /// The test constructor: an explicit (typically tiny) grace window so a spec can
    /// verify the deferred eviction without waiting the full 30 seconds. The built-in
    /// DI container picks the 4-arg constructor above (TimeSpan is not a registered
    /// service), so this overload is test-only.
    /// </summary>
    public SeatGraceService(
        IHubContext<GameHub> hub,
        RoomRegistry rooms,
        TelemetryClient appInsights,
        ILogger<SeatGraceService> logger,
        TimeSpan graceWindow)
    {
        _hub = hub;
        _rooms = rooms;
        _appInsights = appInsights;
        _logger = logger;
        _graceWindow = graceWindow;
    }

    /// <summary>The grace window in force (30s by default; a test may shorten it).</summary>
    public TimeSpan GraceWindow => _graceWindow;

    /// <summary>
    /// Schedule the one-shot delayed eviction for a held seat. The caller (the hub's
    /// OnDisconnectedAsync) treats this as FIRE-AND-FORGET (it discards the task); the
    /// task is returned so a test can await the deferred eviction deterministically.
    /// </summary>
    /// <param name="handle">The grace handle from <see cref="RoomRegistry.BeginGrace"/>.</param>
    /// <returns>The running eviction task (fire-and-forget for the hub; awaitable for tests).</returns>
    public Task ScheduleEviction(SeatGraceHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return RunAsync(handle);
    }

    private async Task RunAsync(SeatGraceHandle handle)
    {
        try
        {
            try
            {
                await Task.Delay(_graceWindow, handle.Token);
            }
            catch (OperationCanceledException)
            {
                // The seat reconnected within the grace window (story 08's Rejoin
                // cancelled the token) - keep the seat, nothing to evict.
                return;
            }

            // The window elapsed with no reconnect. Release the seat ONLY if this same
            // disconnect episode is still pending (re-checked under the room lock), then
            // re-broadcast to the survivors. A reconnect / newer drop makes this a no-op.
            var stillActive = _rooms.ReleaseGraceSeat(handle.Room, handle.ConnectionId, handle.Episode);
            TryTrack("HubGraceExpired");
            if (stillActive is not null)
            {
                await GameHub.BroadcastPlayerLeftAsync(_hub.Clients, stillActive);
            }
        }
        catch (Exception ex)
        {
            // A fault in a fire-and-forget timer must be logged, never left unobserved -
            // a dropped seat's grace must never crash the host or take down the room.
            _logger.LogWarning(ex, "Grace-expiry eviction faulted (swallowed - the grace timer never gates gameplay).");
        }
    }

    /// <summary>
    /// Fire an anonymous operational event on the App Insights pipeline (mirrors the
    /// hub's HubAbnormalDisconnect / HubGraceStarted), swallowing any fault so
    /// telemetry never interferes with eviction. No room / nickname / connection
    /// payload - just the fact that a grace window expired (no PII, AC-07 posture).
    /// </summary>
    private void TryTrack(string eventName)
    {
        try
        {
            _appInsights.TrackEvent(eventName);
        }
        catch
        {
            // Swallowed: operational telemetry must never break seat eviction.
        }
    }
}
