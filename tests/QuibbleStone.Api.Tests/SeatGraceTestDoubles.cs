// ----------------------------------------------------------------------------
//  SeatGraceTestDoubles - minimal SignalR fakes for the disconnect grace-window
//  tests (session-engine/07).
//
//  SeatGraceService broadcasts its grace-expiry epilogue through
//  IHubContext<GameHub> (the originating hub invocation has ended by the time the
//  timer fires, so the hub's own Clients are no longer valid). These hand-rolled
//  doubles let a spec BUILD that context and INSPECT what the epilogue broadcast,
//  without a mocking framework (the harness has none) and without a running host.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

/// <summary>
/// An <see cref="IHubClients"/> that records EVERY send (group + method + args) so a
/// grace-window test can assert on the full epilogue (RoundAborted then RosterChanged),
/// not just the last call.
/// </summary>
public sealed class RecordingHubClients : IHubClients
{
    /// <summary>Every recorded send, in order.</summary>
    public List<(string? Group, string Method, object?[] Args)> Sends { get; } = new();

    private IClientProxy Proxy(string? group) => new RecordingProxy(this, group);

    public IClientProxy All => Proxy(null);
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy(null);
    public IClientProxy Client(string connectionId) => Proxy(null);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy(null);
    public IClientProxy Group(string groupName) => Proxy(groupName);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy(groupName);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy(null);
    public IClientProxy User(string userId) => Proxy(null);
    public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy(null);

    private sealed class RecordingProxy(RecordingHubClients owner, string? group) : IClientProxy, ISingleClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            owner.Sends.Add((group, method, args));
            return Task.CompletedTask;
        }

        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.FromResult(default(T)!);
    }
}

/// <summary>An <see cref="IGroupManager"/> that does nothing (grace never touches groups).</summary>
public sealed class NoopGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// A fake <see cref="IHubContext{GameHub}"/> over a <see cref="RecordingHubClients"/>,
/// so a grace-window test can drive SeatGraceService and read back the epilogue it sent.
/// </summary>
public sealed class FakeGameHubContext : IHubContext<GameHub>
{
    /// <summary>The recording clients, exposed so a test can inspect the sends.</summary>
    public RecordingHubClients Recorder { get; } = new();

    public IHubClients Clients => Recorder;
    public IGroupManager Groups { get; } = new NoopGroupManager();
}

/// <summary>
/// Builds a <see cref="SeatGraceService"/> for tests (mirrors TestTelemetry.NoOp's
/// role): a real service over a fake hub context, with a caller-chosen grace window.
/// </summary>
public static class TestSeatGrace
{
    /// <summary>
    /// A grace service bound to <paramref name="rooms"/> with a LONG window (5 minutes)
    /// so a scheduled eviction never fires during a synchronous unit test - for hub
    /// tests that only assert the immediate hold, not the deferred eviction.
    /// </summary>
    public static SeatGraceService NoOp(RoomRegistry rooms) =>
        new SeatGraceService(
            new FakeGameHubContext(),
            rooms,
            TestTelemetry.NoOp,
            NullLogger<SeatGraceService>.Instance,
            TimeSpan.FromMinutes(5));
}
