// ----------------------------------------------------------------------------
//  HubPlayer - one simulated player: a thin wrapper over a single SignalR
//  HubConnection that drives the real GameHub the way the web client does.
//
//  It owns ONE connection (mirroring the app's "one shared connection" rule) and:
//    - registers the server -> client broadcast handlers the game flow needs
//      (YourBlanks, RevealReady, plus the count-only RoundStarted / CollectProgress
//      / RosterChanged / RoundAborted / BackToLobby),
//    - exposes the invocable hub methods the full-round scenario calls
//      (CreateRoom / JoinRoom / StartRound / SubmitWord / Rejoin / LeaveRoom), each
//      timed + error-bucketed through Metrics,
//    - arms a fresh completion source per round so the scenario can await THIS
//      player's own YourBlanks (its assigned blank indices) and the room's
//      RevealReady (round complete) without polling.
//
//  WHY A TCS, NOT WORK-IN-THE-HANDLER: the broadcast handlers only complete a
//  TaskCompletionSource (never block, never call back into the hub), so the
//  SignalR receive loop is never stalled. The scenario code does the actual
//  SubmitWord calls AFTER awaiting the blanks - no reentrancy into a handler.
//
//  RECONNECT: the connection is built WithAutomaticReconnect so an unexpected
//  transport drop under load is handled + observed (Reconnecting/Reconnected feed
//  the metrics). Deliberate churn is separate: ChurnReconnectAsync does a clean
//  Stop + Start (a fresh server connection id) then spends the seat's reconnect
//  token via Rejoin - exactly the session-engine/07+08 path a car dead-zone hits.
//
//  CASING: the JSON protocol is configured PropertyNameCaseInsensitive so the
//  PascalCase DTOs bind to the camelCase wire (see Dtos.cs).
//
//  Prose style: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection; // AddJsonProtocol

namespace QuibbleStone.LoadTest;

public sealed class HubPlayer : IAsyncDisposable
{
    private readonly HubConnection _conn;
    private readonly Metrics _metrics;
    private readonly SemaphoreSlim _connectGate;

    // Per-round signals, swapped fresh by ArmRound before each StartRound. Volatile
    // so the broadcast-handler thread always sees the current round's source.
    private volatile TaskCompletionSource<IReadOnlyList<int>> _yourBlanks = NewBlanksSource();
    private volatile TaskCompletionSource<bool> _reveal = NewRevealSource();

    /// <summary>This seat's own reconnect token (from its CreateRoom/JoinRoom envelope); spent by churn's Rejoin. Null before create/join.</summary>
    public string? ReconnectToken { get; private set; }

    public HubPlayer(string hubUrl, Metrics metrics, SemaphoreSlim connectGate)
    {
        _metrics = metrics;
        _connectGate = connectGate;

        _conn = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .AddJsonProtocol(options => options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true)
            .Build();

        RegisterHandlers();
    }

    private void RegisterHandlers()
    {
        // Per-connection: THIS player's assigned blank indices (blind - indices only).
        _conn.On<YourBlanksDto>("YourBlanks", dto =>
            _yourBlanks.TrySetResult(dto.BlankIndices ?? Array.Empty<int>()));

        // Room-wide: the round completed (last blank submitted) - the completion signal.
        _conn.On<RevealReadyDto>("RevealReady", _ =>
        {
            _metrics.BroadcastRevealReady();
            _reveal.TrySetResult(true);
        });

        // Room-wide: a player left mid-collection so the round can no longer complete.
        // Resolve the reveal waiter as "not completed" so a scenario never hangs on it.
        _conn.On<RoundAbortedDto>("RoundAborted", _ =>
        {
            _metrics.BroadcastRoundAborted();
            _reveal.TrySetResult(false);
        });

        // Count-only broadcasts (verify the server -> client fan-out is actually arriving).
        _conn.On<RoundStartedDto>("RoundStarted", _ => _metrics.BroadcastRoundStarted());
        _conn.On<CollectProgressDto>("CollectProgress", _ => _metrics.BroadcastCollectProgress());
        _conn.On<RoomStateDto>("RosterChanged", _ => _metrics.BroadcastRosterChanged());
        _conn.On("BackToLobby", () => { /* no-op: the harness drives replay via StartRound */ });

        // Auto-reconnect lifecycle (unexpected drops only, NOT deliberate churn).
        _conn.Reconnecting += _ => { _metrics.Reconnecting(); return Task.CompletedTask; };
        _conn.Reconnected += _ => { _metrics.Reconnected(); return Task.CompletedTask; };
    }

    /// <summary>Open the connection, gated by the ramp semaphore so only N clients connect at once.</summary>
    public async Task<bool> ConnectAsync()
    {
        _metrics.ConnectAttempted();
        await _connectGate.WaitAsync();
        try
        {
            await _conn.StartAsync();
            _metrics.ConnectSucceeded();
            return true;
        }
        catch (Exception ex)
        {
            _metrics.ConnectFailed();
            _metrics.RecordError("Connect", ex);
            return false;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task<CreateRoomResultDto?> CreateRoomAsync(string displayName, string variant)
    {
        var result = await _metrics.TimeInvokeAsync(
            "CreateRoom", () => _conn.InvokeAsync<CreateRoomResultDto>("CreateRoom", displayName, variant));
        if (result is { Ok: true })
        {
            ReconnectToken = result.ReconnectToken;
        }
        return result;
    }

    public async Task<JoinResultDto?> JoinRoomAsync(string code, string displayName, string variant)
    {
        var result = await _metrics.TimeInvokeAsync(
            "JoinRoom", () => _conn.InvokeAsync<JoinResultDto>("JoinRoom", code, displayName, variant));
        if (result is { Ok: true })
        {
            ReconnectToken = result.ReconnectToken;
        }
        return result;
    }

    /// <summary>Host-only. Sends all five declared StartRound args (templateId null) exactly as the web client does, so argument binding is unambiguous.</summary>
    public Task<StartRoundResultDto?> StartRoundAsync(string code) =>
        _metrics.TimeInvokeAsync(
            "StartRound",
            () => _conn.InvokeAsync<StartRoundResultDto>(
                "StartRound", code, true, Config.LengthPreference, Config.Mode, null));

    /// <summary>Submit one word for one assigned blank. Returns true only on an Ok=true envelope.</summary>
    public async Task<bool> SubmitWordAsync(string code, int blankIndex, string word)
    {
        _metrics.SubmitAttempted();
        var result = await _metrics.TimeInvokeAsync(
            "SubmitWord", () => _conn.InvokeAsync<SubmitWordResultDto>("SubmitWord", code, blankIndex, word));
        if (result is null)
        {
            return false;   // transport error, already bucketed
        }
        if (result.Ok)
        {
            _metrics.SubmitOk();
            return true;
        }
        _metrics.SubmitRejected();
        return false;
    }

    public Task LeaveRoomAsync(string code) =>
        _metrics.TimeInvokeVoidAsync("LeaveRoom", () => _conn.InvokeAsync("LeaveRoom", code));

    /// <summary>Arm fresh per-round completion sources. MUST be called on every roster player BEFORE the host calls StartRound.</summary>
    public void ArmRound()
    {
        _yourBlanks = NewBlanksSource();
        _reveal = NewRevealSource();
    }

    /// <summary>Await THIS player's YourBlanks (its assigned blank indices), or null on timeout.</summary>
    public async Task<IReadOnlyList<int>?> WaitYourBlanksAsync(TimeSpan timeout)
    {
        var source = _yourBlanks;
        var winner = await Task.WhenAny(source.Task, Task.Delay(timeout));
        return winner == source.Task ? source.Task.Result : null;
    }

    /// <summary>Await the room's RevealReady (round complete), or false on timeout / abort.</summary>
    public async Task<bool> WaitRevealAsync(TimeSpan timeout)
    {
        var source = _reveal;
        var winner = await Task.WhenAny(source.Task, Task.Delay(timeout));
        return winner == source.Task && source.Task.Result;
    }

    /// <summary>
    /// Deliberate mid-round churn: cleanly Stop the connection (the server holds the
    /// seat through its grace window - it is NOT evicted and the round is NOT aborted),
    /// then Start a fresh connection and spend the seat's reconnect token via Rejoin.
    /// Returns this seat's still-OUTSTANDING blank indices so the caller submits only
    /// what is left, or null if the reconnect/rejoin failed.
    /// </summary>
    public async Task<IReadOnlyList<int>?> ChurnReconnectAsync(string code)
    {
        if (ReconnectToken is null)
        {
            return null;
        }

        _metrics.ChurnDrop();
        try
        {
            await _conn.StopAsync();
        }
        catch (Exception ex)
        {
            _metrics.RecordError("ChurnStop", ex);
        }

        // Restart the SAME connection object (it negotiates a fresh server connection
        // id), gated by the ramp like any other connect.
        await _connectGate.WaitAsync();
        try
        {
            await _conn.StartAsync();
        }
        catch (Exception ex)
        {
            _metrics.RecordError("ChurnStart", ex);
            return null;
        }
        finally
        {
            _connectGate.Release();
        }

        var rejoin = await _metrics.TimeInvokeAsync(
            "Rejoin", () => _conn.InvokeAsync<RejoinResultDto>("Rejoin", code, ReconnectToken));
        if (rejoin is null || !rejoin.Ok)
        {
            _metrics.ChurnRejoinFailed();
            return null;
        }

        _metrics.ChurnRejoin();
        return rejoin.YourBlanks?.BlankIndices ?? Array.Empty<int>();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _conn.DisposeAsync();
        }
        catch
        {
            // Best-effort teardown: a dispose fault must never break the run summary.
        }
    }

    private static TaskCompletionSource<IReadOnlyList<int>> NewBlanksSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> NewRevealSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
