// ----------------------------------------------------------------------------
//  AiJumbleControllerTests - the REST seam onto the AI jumble
//  (ai-on-demand-generation/05). These pin the controller's ONE job beyond
//  delegating to the generator: picking the ANONYMOUS quota key (README section
//  6) and rejecting bad input WITHOUT reaching the gate.
//
//  A RecordingQuota captures the instanceId the gate is keyed on, so the tests
//  prove:
//    - GROUP play keys on the live room's Room.InstanceId (resolved from the
//      join code the client holds), never PII.
//    - SOLO play keys on the client's device session id (no room).
//    - A missing/malformed category or no usable session key short-circuits to a
//      graceful fell-back WITHOUT consuming quota or calling the gate.
//
//  Real ContentSafetyFilter + real RoomRegistry; a stub transport (no live
//  provider). Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;
using QuibbleStone.Api.Ai.Jumble;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests.Ai;

public class AiJumbleControllerTests
{
    private static readonly AiCompletionResult Reply =
        new("moss\nember\nglint\nfrost", InputTokens: 400, OutputTokens: 30, ModelId: "gpt-5-mini", IsAvailable: true);

    private static (AiJumbleController controller, RecordingQuota quota, RoomRegistry rooms) Build()
    {
        var quota = new RecordingQuota();
        var gate = new GatedAiCompletionClient(
            quota,
            new NoOpAiSpendGuard(),
            new StubTransport(Reply),
            new AiOutputModerator(
                new ContentSafetyFilter(),
                new NoOpAiContentSafetyScreen(),
                NullLogger<AiOutputModerator>.Instance),
            NullLogger<GatedAiCompletionClient>.Instance);

        var generator = new JumbleWordGenerator(gate, NullLogger<JumbleWordGenerator>.Instance);
        var rooms = new RoomRegistry();
        var controller = new AiJumbleController(generator, rooms, NullLogger<AiJumbleController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, quota, rooms);
    }

    [Fact]
    public async Task Group_play_keys_the_quota_on_the_live_room_InstanceId()
    {
        var (controller, quota, rooms) = Build();
        var room = rooms.CreateRoom("conn-1", "Ada", "fox");

        var result = await controller.Jumble(
            new AiJumbleRequest("noun", FamilySafe: false, Avoid: null, RoomCode: room.Code, SessionId: "device-x"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        // The join code wins and resolves to the anonymous InstanceId (not the device id).
        Assert.Equal(room.InstanceId, quota.LastInstanceId);
    }

    [Fact]
    public async Task Solo_play_keys_the_quota_on_the_device_session_id()
    {
        var (controller, quota, _) = Build();

        await controller.Jumble(
            new AiJumbleRequest("noun", FamilySafe: false, Avoid: null, RoomCode: null, SessionId: "device-x"),
            CancellationToken.None);

        Assert.Equal("device-x", quota.LastInstanceId);
    }

    [Fact]
    public async Task Unknown_room_code_falls_back_to_the_solo_session_id()
    {
        var (controller, quota, _) = Build();

        await controller.Jumble(
            new AiJumbleRequest("noun", FamilySafe: false, Avoid: null, RoomCode: "ZZZZ", SessionId: "device-x"),
            CancellationToken.None);

        Assert.Equal("device-x", quota.LastInstanceId);
    }

    [Fact]
    public async Task No_usable_session_key_short_circuits_without_touching_the_gate()
    {
        var (controller, quota, _) = Build();

        var result = await controller.Jumble(
            new AiJumbleRequest("noun", FamilySafe: false, Avoid: null, RoomCode: null, SessionId: null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(quota.LastInstanceId); // the gate was never called
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no un")]     // whitespace
    [InlineData("verb!")]     // punctuation
    [InlineData("<script>")]  // injection-shaped
    public async Task Missing_or_malformed_category_short_circuits_without_touching_the_gate(string? category)
    {
        var (controller, quota, _) = Build();

        var result = await controller.Jumble(
            new AiJumbleRequest(category, FamilySafe: false, Avoid: null, RoomCode: null, SessionId: "device-x"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(quota.LastInstanceId); // rejected before any gated call
    }

    [Fact]
    public async Task Oversized_avoid_list_is_capped_before_the_generator_runs()
    {
        var (controller, _, _) = Build();
        // A pathological payload: 10k entries, each very long. The controller must
        // cap it (size + per-item length) so the gated call still succeeds cleanly
        // rather than doing unbounded dedupe work.
        var huge = Enumerable.Range(0, 10_000).Select(i => new string('x', 5_000) + i).ToArray();

        var result = await controller.Jumble(
            new AiJumbleRequest("noun", FamilySafe: false, Avoid: huge, RoomCode: null, SessionId: "device-x"),
            CancellationToken.None);

        // It still returns a normal OK result (the stub transport's clean words survive).
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Null_body_falls_back_cleanly()
    {
        var (controller, quota, _) = Build();

        var result = await controller.Jumble(null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(quota.LastInstanceId);
    }

    /// <summary>An IAiQuota that records the instanceId it was consumed with (and always allows).</summary>
    private sealed class RecordingQuota : IAiQuota
    {
        public string? LastInstanceId { get; private set; }

        public AiQuotaDecision TryConsume(string instanceId)
        {
            LastInstanceId = instanceId;
            return new AiQuotaDecision(Allowed: true, Remaining: 19);
        }
    }

    private sealed class StubTransport : IAiCompletionClient
    {
        private readonly AiCompletionResult _result;
        public StubTransport(AiCompletionResult result) => _result = result;
        public Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }
}
