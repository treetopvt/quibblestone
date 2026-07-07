// ----------------------------------------------------------------------------
//  Config - the harness run configuration, parsed from CLI args and/or env vars.
//
//  Every knob has a MODEST default so a first run is safe (10 rooms x 6 players x
//  1 round against a LOCAL hub - see README's safety notes). Precedence is
//  CLI > environment variable > default, so a scripted run can set env vars and a
//  one-off can override any of them on the command line.
//
//  Supported flags (also `--flag=value`) and their env equivalents:
//    --rooms N              QS_LOAD_ROOMS              rooms to simulate (default 10)
//    --players-per-room N   QS_LOAD_PLAYERS_PER_ROOM   players incl. host (default 6, min 2)
//    --rounds N             QS_LOAD_ROUNDS             full rounds per room (default 1)
//    --hub-url URL          QS_LOAD_HUB_URL            hub endpoint (default local :5180)
//    --ramp N               QS_LOAD_RAMP               max concurrently-connecting clients (default 50)
//    --churn F              QS_LOAD_CHURN              fraction 0..1 of joiners to drop+rejoin mid-round (default 0)
//    --ai                   QS_LOAD_AI=true            opt-in: fire real AI jumble calls per room (default OFF)
//    --ai-calls-per-room N  QS_LOAD_AI_CALLS_PER_ROOM  AI calls per room when --ai is on (default 2)
//    --help / -h                                       print usage and exit
//
//  AI SAFETY: --ai is OFF by default, so a normal run makes ZERO AI calls and
//  incurs ZERO AI cost. When ON, each room fires a small number of real
//  POST /api/ai/jumble requests; on a UAT target (where Ai:Endpoint IS set) these
//  hit gpt-5-mini and cost real (tiny) money, BOUNDED server-side by the per-
//  session quota (20), the per-IP limiter (30/min), and the $20/month spend
//  breaker. See the README's AI cost notes.
//
//  StartRound requires >= 2 players (the host plus at least one other carver), so
//  players-per-room is validated to be >= 2 and the harness fails fast with a
//  clear message rather than starting rooms that can never start a round.
//
//  Prose style: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Globalization;

namespace QuibbleStone.LoadTest;

public sealed class Config
{
    public int Rooms { get; private init; } = 10;
    public int PlayersPerRoom { get; private init; } = 6;
    public int Rounds { get; private init; } = 1;
    public string HubUrl { get; private init; } = "http://localhost:5180/hubs/game";
    public int Ramp { get; private init; } = 50;
    public double Churn { get; private init; }

    // Opt-in AI jumble scenario. OFF by default: zero AI calls, zero cost. When
    // ON, each room fires AiCallsPerRoom real POST /api/ai/jumble requests.
    public bool Ai { get; private init; }
    public int AiCallsPerRoom { get; private init; } = 2;

    // The story-length preference StartRound is called with. "full" (>= 7 blanks)
    // is used deliberately so every player in a full room is dealt at least one
    // blank - more submissions per round, a heavier real-time exercise. This is a
    // scenario constant, not a public flag (the scenario the harness drives is
    // fixed - see the task/README); change it here to probe other lengths.
    public const string LengthPreference = "full";

    // The mode every round runs in. "classic-blind" is an OFFERED group mode
    // (api/src/Content/GameModeCatalog.cs) and is eligible for every family-safe
    // template, so the selection pipeline always finds a pool.
    public const string Mode = "classic-blind";

    /// <summary>
    /// The API base URL (scheme + host + port) the REST AI jumble endpoint lives
    /// on, derived from the hub URL by dropping its path (the hub is at
    /// {base}/hubs/game; the jumble is at {base}/api/ai/jumble). Authority-based, so
    /// it is correct for both local (:5180) and UAT (azurewebsites.net) targets.
    /// </summary>
    public string ApiBaseUrl => new Uri(HubUrl).GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// Parse the run configuration. Returns null when help was requested or the
    /// args were invalid (a message is printed first), so Main can exit cleanly.
    /// </summary>
    public static Config? Parse(string[] args)
    {
        // Seed from environment first; CLI overrides below.
        int rooms = EnvInt("QS_LOAD_ROOMS", 10);
        int players = EnvInt("QS_LOAD_PLAYERS_PER_ROOM", 6);
        int rounds = EnvInt("QS_LOAD_ROUNDS", 1);
        string hubUrl = Environment.GetEnvironmentVariable("QS_LOAD_HUB_URL") is { Length: > 0 } u
            ? u
            : "http://localhost:5180/hubs/game";
        int ramp = EnvInt("QS_LOAD_RAMP", 50);
        double churn = EnvDouble("QS_LOAD_CHURN", 0.0);
        bool ai = EnvBool("QS_LOAD_AI", false);
        int aiCallsPerRoom = EnvInt("QS_LOAD_AI_CALLS_PER_ROOM", 2);

        for (var i = 0; i < args.Length; i += 1)
        {
            var (key, inlineValue) = SplitFlag(args[i]);

            // A value is either inline ("--rooms=20") or the next token ("--rooms 20").
            string? Value()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }
                if (i + 1 < args.Length)
                {
                    i += 1;
                    return args[i];
                }
                return null;
            }

            switch (key)
            {
                case "--help" or "-h":
                    PrintUsage();
                    return null;
                case "--rooms":
                    if (!TryInt(Value(), key, out rooms)) return null;
                    break;
                case "--players-per-room":
                    if (!TryInt(Value(), key, out players)) return null;
                    break;
                case "--rounds":
                    if (!TryInt(Value(), key, out rounds)) return null;
                    break;
                case "--hub-url":
                    var hv = Value();
                    if (string.IsNullOrWhiteSpace(hv)) { Fail($"{key} needs a URL."); return null; }
                    hubUrl = hv;
                    break;
                case "--ramp":
                    if (!TryInt(Value(), key, out ramp)) return null;
                    break;
                case "--churn":
                    if (!TryDouble(Value(), key, out churn)) return null;
                    break;
                case "--ai":
                    // A bare boolean toggle. Accept an optional inline value
                    // (--ai=false) but no separate token, so "--ai --rooms 5" works.
                    ai = inlineValue is null || ParseBool(inlineValue);
                    break;
                case "--ai-calls-per-room":
                    if (!TryInt(Value(), key, out aiCallsPerRoom)) return null;
                    break;
                default:
                    Fail($"Unknown argument '{args[i]}'. Try --help.");
                    return null;
            }
        }

        // Validate into safe ranges with clear, actionable messages.
        if (rooms < 1) { Fail("--rooms must be >= 1."); return null; }
        if (players < 2) { Fail("--players-per-room must be >= 2 (StartRound needs the host plus at least one other player)."); return null; }
        if (rounds < 1) { Fail("--rounds must be >= 1."); return null; }
        if (ramp < 1) { Fail("--ramp must be >= 1."); return null; }
        if (churn is < 0 or > 1) { Fail("--churn must be between 0 and 1 (a fraction of joiners)."); return null; }
        if (aiCallsPerRoom < 0) { Fail("--ai-calls-per-room must be >= 0."); return null; }
        if (!Uri.TryCreate(hubUrl, UriKind.Absolute, out _)) { Fail($"--hub-url '{hubUrl}' is not a valid absolute URL."); return null; }

        return new Config
        {
            Rooms = rooms,
            PlayersPerRoom = players,
            Rounds = rounds,
            HubUrl = hubUrl,
            Ramp = ramp,
            Churn = churn,
            Ai = ai,
            AiCallsPerRoom = aiCallsPerRoom,
        };
    }

    /// <summary>Echo the resolved configuration so a run is self-documenting in its own output.</summary>
    public void Print()
    {
        Console.WriteLine("QuibbleStone SignalR load harness");
        Console.WriteLine("---------------------------------");
        Console.WriteLine($"  hub-url          : {HubUrl}");
        Console.WriteLine($"  rooms            : {Rooms}");
        Console.WriteLine($"  players-per-room : {PlayersPerRoom}  (1 host + {PlayersPerRoom - 1} joiners)");
        Console.WriteLine($"  rounds           : {Rounds}");
        Console.WriteLine($"  ramp (max conc.) : {Ramp} connecting clients");
        Console.WriteLine($"  churn            : {Churn:0.###}  (fraction of joiners dropped+rejoined mid-round)");
        Console.WriteLine($"  length / mode    : {LengthPreference} / {Mode} (family-safe)");
        Console.WriteLine($"  total clients    : {Rooms * PlayersPerRoom}");
        if (Ai)
        {
            Console.WriteLine($"  AI jumble        : ON  ({AiCallsPerRoom}/room -> {Rooms * AiCallsPerRoom} real POST {ApiBaseUrl}/api/ai/jumble)");
            Console.WriteLine("                     WARNING: real gpt-5-mini calls (small cost) on a target where Ai:Endpoint is set (e.g. UAT).");
        }
        else
        {
            Console.WriteLine("  AI jumble        : OFF (zero AI calls, zero cost)");
        }
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            QuibbleStone SignalR load harness - drives the real GameHub with many
            concurrent simulated players across many rooms and reports latency + errors.

            Usage:
              dotnet run --project load/QuibbleStone.LoadTest.csproj -- [options]

            Options (env var in parentheses; CLI wins):
              --rooms N              (QS_LOAD_ROOMS)             rooms to simulate           [10]
              --players-per-room N   (QS_LOAD_PLAYERS_PER_ROOM)  players incl. host, min 2   [6]
              --rounds N             (QS_LOAD_ROUNDS)            full rounds per room        [1]
              --hub-url URL          (QS_LOAD_HUB_URL)           hub endpoint                [http://localhost:5180/hubs/game]
              --ramp N               (QS_LOAD_RAMP)              max concurrent connects     [50]
              --churn F              (QS_LOAD_CHURN)             0..1 joiners drop+rejoin     [0]
              --ai                   (QS_LOAD_AI=true)           opt-in real AI jumble calls  [off]
              --ai-calls-per-room N  (QS_LOAD_AI_CALLS_PER_ROOM) AI calls per room when --ai  [2]
              --help, -h                                         show this help

            Examples:
              # local rehearsal (no AI)
              dotnet run --project load/QuibbleStone.LoadTest.csproj -- --rooms 50 --players-per-room 6 --rounds 3 --ramp 100
              # UAT run with the opt-in AI scenario (small real cost)
              dotnet run --project load/QuibbleStone.LoadTest.csproj -- \
                --hub-url https://<uat-api-host>/hubs/game --rooms 10 --players-per-room 5 --ramp 20 --ai
            """);
    }

    // ----- small parsing helpers -------------------------------------------------

    private static (string key, string? inlineValue) SplitFlag(string arg)
    {
        var eq = arg.IndexOf('=');
        return eq >= 0 ? (arg[..eq], arg[(eq + 1)..]) : (arg, null);
    }

    private static bool TryInt(string? raw, string key, out int value)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        Fail($"{key} needs an integer (got '{raw}').");
        return false;
    }

    private static bool TryDouble(string? raw, string key, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        Fail($"{key} needs a number (got '{raw}').");
        return false;
    }

    private static bool ParseBool(string raw) =>
        raw.Equals("true", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("1", StringComparison.Ordinal)
        || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("on", StringComparison.OrdinalIgnoreCase);

    private static bool EnvBool(string name, bool fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? ParseBool(v) : fallback;

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    private static double EnvDouble(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    private static void Fail(string message) => Console.Error.WriteLine($"Error: {message}");
}
