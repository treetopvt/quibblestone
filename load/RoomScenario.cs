// ----------------------------------------------------------------------------
//  RoomScenario - the "full round" flow for ONE simulated room, run concurrently
//  with every other room.
//
//  The flow it drives (the real GameHub lifecycle, exactly as the app does it):
//    1. A host client connects and CreateRoom -> capture the join code.
//    2. playersPerRoom-1 joiners connect and JoinRoom with unique nicknames.
//    3. For each round: arm every player, host StartRound, each player awaits its
//       own YourBlanks and SubmitWord's each assigned blank (benign words), then
//       await RevealReady (round complete). StartRound again replays in the same
//       room (the server increments the round number - no BackToLobby needed).
//    4. Optional churn: a fraction of joiners drop + Rejoin mid-round (the seat is
//       held through the server's grace window, so the round still completes).
//    5. Optional AI: when --ai is on, fire a few real /api/ai/jumble calls per room
//       OVERLAPPING the round (realistic contention), awaited before teardown.
//    6. Teardown: LeaveRoom + dispose every connection so no room lingers server-side.
//
//  RESILIENCE: every hub invoke returns null/false on a transport fault (bucketed
//  in Metrics) instead of throwing, so one bad call degrades a single room's round
//  rather than the run. Program.cs wraps the whole room in a try/catch too.
//
//  Prose style: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.LoadTest;

public sealed class RoomScenario
{
    // Generous waits so healthy-but-loaded rounds are not falsely marked incomplete;
    // a genuine hole (a round that never reveals) still surfaces within these bounds.
    private static readonly TimeSpan YourBlanksTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RevealTimeout = TimeSpan.FromSeconds(60);

    // All benign, all pass the server safety filter - the word content is irrelevant
    // to the load, only the submit round-trips matter.
    private static readonly string[] BenignWords =
    {
        "banana", "wobble", "sparkly", "noodle", "pickle", "jelly", "sock", "wiggle",
        "bouncy", "muffin", "sprocket", "waffle", "pebble", "giggle", "turnip", "zigzag",
    };

    // The six known Guardian variants (api/src/Hubs/GameHub.cs KnownVariants). The
    // server normalizes anything unknown to "teal", so any value is safe - using the
    // real set just keeps the simulated roster faithful.
    private static readonly string[] Variants = { "purple", "gold", "coral", "teal", "sand", "plum" };

    private readonly Config _config;
    private readonly Metrics _metrics;
    private readonly SemaphoreSlim _connectGate;
    private readonly AiJumbleClient? _ai;

    public RoomScenario(Config config, Metrics metrics, SemaphoreSlim connectGate, AiJumbleClient? ai)
    {
        _config = config;
        _metrics = metrics;
        _connectGate = connectGate;
        _ai = ai;
    }

    public async Task RunAsync()
    {
        // Every connection created (host + joiners, whether or not they joined) is
        // tracked so teardown disposes all of them.
        var allConnections = new List<HubPlayer>();
        string? code = null;

        try
        {
            // 1. Host connects + creates the room.
            var host = new HubPlayer(_config.HubUrl, _metrics, _connectGate);
            allConnections.Add(host);
            if (!await host.ConnectAsync())
            {
                return; // connect failure already bucketed
            }

            var created = await host.CreateRoomAsync("Host", Variants[0]);
            if (created is not { Ok: true, Room: not null })
            {
                _metrics.RoomCreateFailed();
                return;
            }
            _metrics.RoomCreated();
            code = created.Room.Code;

            // 2. Joiners connect + join in parallel (ramp-gated inside ConnectAsync).
            var joiners = new List<HubPlayer>();
            var joinTasks = new List<Task>();
            for (var i = 1; i < _config.PlayersPerRoom; i += 1)
            {
                var joiner = new HubPlayer(_config.HubUrl, _metrics, _connectGate);
                allConnections.Add(joiner);
                var nickname = $"P{i}";                    // unique within the room
                var variant = Variants[i % Variants.Length];
                joinTasks.Add(JoinOneAsync(joiner, code, nickname, variant, joiners));
            }
            await Task.WhenAll(joinTasks);

            // The live roster the server dealt blanks to = host + everyone who joined.
            var roster = new List<HubPlayer>(joiners.Count + 1) { host };
            roster.AddRange(joiners);
            if (roster.Count < 2)
            {
                _metrics.RoomUnderStaffed(); // StartRound needs >= 2
                return;
            }

            // 3. Rounds. AI (if on) fires once for the room, overlapping round 0.
            Task? aiTask = null;
            for (var round = 0; round < _config.Rounds; round += 1)
            {
                foreach (var player in roster)
                {
                    player.ArmRound();
                }

                var start = await host.StartRoundAsync(code);
                if (start is null || !start.Ok)
                {
                    _metrics.RoundStartRejected();
                    break;
                }
                _metrics.RoundStarted();

                if (round == 0 && _ai is not null && _config.AiCallsPerRoom > 0)
                {
                    aiTask = FireAiCallsAsync(code);
                }

                var churnSet = SelectChurn(joiners, round);
                var roundCode = code;
                var playerTasks = roster.Select(p => PlayRoundAsync(p, roundCode, churnSet.Contains(p)));
                await Task.WhenAll(playerTasks);

                if (await host.WaitRevealAsync(RevealTimeout))
                {
                    _metrics.RoundCompleted();
                }
                else
                {
                    _metrics.RoundIncomplete();
                }
            }

            if (aiTask is not null)
            {
                await aiTask; // AI outcomes are recorded inside; this never throws
            }
        }
        finally
        {
            // Best-effort teardown: leave the room then dispose every connection.
            if (code is not null)
            {
                foreach (var player in allConnections)
                {
                    await player.LeaveRoomAsync(code);
                }
            }
            foreach (var player in allConnections)
            {
                await player.DisposeAsync();
            }
        }
    }

    private async Task JoinOneAsync(HubPlayer joiner, string code, string nickname, string variant, List<HubPlayer> joiners)
    {
        if (!await joiner.ConnectAsync())
        {
            _metrics.JoinFailed();
            return;
        }

        var result = await joiner.JoinRoomAsync(code, nickname, variant);
        if (result is { Ok: true })
        {
            _metrics.JoinOk();
            lock (joiners)
            {
                joiners.Add(joiner);
            }
        }
        else
        {
            _metrics.JoinFailed();
        }
    }

    private async Task PlayRoundAsync(HubPlayer player, string code, bool churn)
    {
        var blanks = await player.WaitYourBlanksAsync(YourBlanksTimeout);
        if (blanks is null)
        {
            return; // never received its blanks (a real finding if it recurs) - can't submit
        }

        if (churn)
        {
            // Drop + rejoin mid-round; submit only what is still outstanding afterwards.
            var remaining = await player.ChurnReconnectAsync(code);
            if (remaining is not null)
            {
                blanks = remaining;
            }
        }

        foreach (var blankIndex in blanks)
        {
            var word = BenignWords[Random.Shared.Next(BenignWords.Length)];
            await player.SubmitWordAsync(code, blankIndex, word);
        }
    }

    private async Task FireAiCallsAsync(string code)
    {
        // Sequential per room so one room's AI load stays modest; the aggregate rate
        // across rooms is what exercises the per-IP limiter (429s are expected + healthy).
        for (var i = 0; i < _config.AiCallsPerRoom; i += 1)
        {
            await _ai!.FireAsync(code, CancellationToken.None);
        }
    }

    private HashSet<HubPlayer> SelectChurn(List<HubPlayer> joiners, int round)
    {
        var set = new HashSet<HubPlayer>();
        if (_config.Churn <= 0 || joiners.Count == 0)
        {
            return set;
        }

        var count = (int)Math.Round(joiners.Count * _config.Churn, MidpointRounding.AwayFromZero);
        count = Math.Clamp(count, 0, joiners.Count);

        // Rotate the starting index by round so different joiners churn each round.
        for (var i = 0; i < count; i += 1)
        {
            set.Add(joiners[(round + i) % joiners.Count]);
        }
        return set;
    }
}
