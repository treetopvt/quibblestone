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
//  The grace window's code default is a SINGLE named constant (DefaultGraceWindow =
//  180s / 3 minutes). control-plane/03 (#232) migrated it onto the
//  `session.seatGraceWindowSeconds` settings key: the DI path reads the CURRENT value
//  live when a NEW disconnect schedules its eviction (an operator can retune it with no
//  redeploy - AC-03), while a timer already awaiting its window keeps its original one.
//  The test constructor still injects a fixed tiny window so a spec can drive the
//  deferred eviction deterministically instead of waiting for the real thing.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.SignalR;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Settings;

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
    /// The seat grace window in SECONDS as the code default (session-engine/07). Alpha-gate
    /// hardening (pre-friends-and-family audit, B3) raised this from the original 30-second
    /// starting default to 3 minutes: 30 seconds was too eager for a real phone-lock /
    /// elevator / brief tunnel drop (README section 1's "car dead zone" tolerance) and was
    /// aborting rounds that a slightly longer wait would have let recover cleanly on their
    /// own. control-plane/03 (#232) migrated it onto the `session.seatGraceWindowSeconds`
    /// settings key: this constant is now the CODE DEFAULT source (asserted by
    /// KnobMigrationRegressionTests), and the DI path reads the CURRENT effective window
    /// live when a NEW disconnect schedules its eviction, so an operator can retune it with
    /// no redeploy (AC-03).
    /// </summary>
    public const int DefaultGraceWindowSeconds = 180;

    /// <summary>The code-default grace window as a TimeSpan (control-plane/03 code default source).</summary>
    public static readonly TimeSpan DefaultGraceWindow = TimeSpan.FromSeconds(DefaultGraceWindowSeconds);

    private readonly IHubContext<GameHub> _hub;
    private readonly RoomRegistry _rooms;
    private readonly TelemetryClient _appInsights;
    private readonly ILogger<SeatGraceService> _logger;
    // control-plane/03 (#232): the runtime settings service the DI path reads the current
    // grace window from at each new schedule. Null on the test constructor, which pins a
    // fixed window for deterministic specs (see below).
    private readonly IRuntimeSettingsService? _settings;
    // Set ONLY by the test constructor: a fixed window that bypasses the settings read so a
    // spec runs a tiny, deterministic window. Null on the DI path (read live instead).
    private readonly TimeSpan? _fixedGraceWindow;

    /// <summary>
    /// The DI constructor: reads the grace window LIVE from <see cref="IRuntimeSettingsService"/>
    /// (`session.seatGraceWindowSeconds`, code default 180) when each new disconnect schedules
    /// its eviction, so an operator override governs the NEXT new disconnect (AC-03). The
    /// built-in DI container picks this constructor (TimeSpan is not a registered service).
    /// </summary>
    public SeatGraceService(
        IHubContext<GameHub> hub,
        RoomRegistry rooms,
        TelemetryClient appInsights,
        ILogger<SeatGraceService> logger,
        IRuntimeSettingsService settings)
    {
        _hub = hub;
        _rooms = rooms;
        _appInsights = appInsights;
        _logger = logger;
        _settings = settings;
        _fixedGraceWindow = null;
    }

    /// <summary>
    /// The test constructor: an explicit (typically tiny) grace window so a spec can verify
    /// the deferred eviction without waiting the full window. This overload pins the window
    /// (it does NOT read settings), so a spec is fully deterministic. The DI container never
    /// picks it (TimeSpan is not a registered service), so it is test-only.
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
        _settings = null;
        _fixedGraceWindow = graceWindow;
    }

    /// <summary>
    /// The grace window this service would use for a NEW schedule right now: the fixed
    /// test window when constructed with one, else the code default (the DI path resolves
    /// the live value per-schedule in <see cref="ScheduleEviction"/>). For display / tests.
    /// </summary>
    public TimeSpan GraceWindow => _fixedGraceWindow ?? DefaultGraceWindow;

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
            // control-plane/03 (#232, AC-03): resolve the window ONCE, here, at the start of
            // THIS disconnect's timer. A new disconnect that starts after an operator override
            // reads the new window; a timer already awaiting its Task.Delay captured its own
            // window and keeps running unchanged (no retroactive change to an in-flight timer).
            var graceWindow = await ResolveGraceWindowAsync(handle.Token).ConfigureAwait(false);

            try
            {
                await Task.Delay(graceWindow, handle.Token);
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
            var stillActive = _rooms.ReleaseGraceSeat(
                handle.Room, handle.ConnectionId, handle.Episode, out var promotedHostConnectionId);
            TryTrack("HubGraceExpired");
            if (stillActive is not null)
            {
                // room-start-duplicate-members: pass the promoted-host connection (if the
                // evicted seat was the host) so the epilogue nudges it with "HostGranted".
                // B3 (alpha-gate hardening): also pass the EVICTED seat's own connection id
                // so the epilogue can tell whether IT still owed any blanks before aborting
                // a prompting round on its account - see BroadcastPlayerLeftAsync.
                await GameHub.BroadcastPlayerLeftAsync(_hub.Clients, stillActive, handle.ConnectionId, promotedHostConnectionId);
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
    /// Resolves the grace window for a new schedule (control-plane/03). The test path returns
    /// its fixed window (deterministic specs); the DI path reads the current effective
    /// `session.seatGraceWindowSeconds` live, clamped at >= 1s so a drifted / zero value can
    /// never evict a seat the instant it drops. Any read failure degrades to the code default
    /// rather than fault the timer.
    /// </summary>
    private async ValueTask<TimeSpan> ResolveGraceWindowAsync(CancellationToken ct)
    {
        if (_fixedGraceWindow is { } fixedWindow)
        {
            return fixedWindow;
        }

        if (_settings is null)
        {
            // Neither a fixed window nor a settings service (not expected - each constructor
            // sets one): fall back to the code default rather than throw.
            return DefaultGraceWindow;
        }

        try
        {
            var seconds = await _settings
                .GetIntAsync(SettingsCatalog.SessionSeatGraceWindowSeconds, ct)
                .ConfigureAwait(false);
            return TimeSpan.FromSeconds(Math.Max(1, seconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Seat grace: reading the grace window failed; using the code default.");
            return DefaultGraceWindow;
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
