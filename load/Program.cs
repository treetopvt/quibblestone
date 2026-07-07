// ----------------------------------------------------------------------------
//  Program - the entry point for the QuibbleStone SignalR load harness.
//
//  A standalone console tool (NOT part of the app / solution / CI - see the
//  .csproj header). It parses the run configuration, spins up every room scenario
//  concurrently (connection establishment throttled by the ramp semaphore so you
//  can find the knee), and prints a metrics summary at the end.
//
//  Each room runs independently and its scenario swallows its own faults into the
//  metrics, so one bad room can never tear down the whole run - Task.WhenAll still
//  observes them all. The only shared, thread-safe state is the Metrics collector,
//  the ramp SemaphoreSlim, and (only when --ai is on) the one HttpClient the AI
//  jumble driver posts through.
//
//  Prose style: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Diagnostics;
using QuibbleStone.LoadTest;

var config = Config.Parse(args);
if (config is null)
{
    return; // help was printed, or the args were invalid (message already shown)
}

config.Print();

var metrics = new Metrics();
using var connectGate = new SemaphoreSlim(config.Ramp, config.Ramp);

// The REST client (and therefore ANY AI cost) exists ONLY when --ai is passed.
using HttpClient? httpClient = config.Ai
    ? new HttpClient { BaseAddress = new Uri(config.ApiBaseUrl), Timeout = TimeSpan.FromSeconds(30) }
    : null;
var aiClient = httpClient is not null ? new AiJumbleClient(httpClient, metrics) : null;

if (config.Ai)
{
    Console.WriteLine(
        $"AI scenario ON: up to {config.Rooms * config.AiCallsPerRoom} real jumble calls to {config.ApiBaseUrl}/api/ai/jumble");
    Console.WriteLine(
        "  bounded server-side by per-session quota (20), per-IP limiter (30/min), and the $20/month spend breaker.");
    Console.WriteLine(
        "  429s and fell-back responses under load are EXPECTED, healthy gate signals - recorded, not errors.");
    Console.WriteLine();
}

Console.WriteLine($"Running {config.Rooms} room scenario(s)...");
var stopwatch = Stopwatch.StartNew();

var rooms = Enumerable
    .Range(0, config.Rooms)
    .Select(_ => RunRoomSafeAsync(config, metrics, connectGate, aiClient))
    .ToArray();
await Task.WhenAll(rooms);

stopwatch.Stop();
metrics.PrintSummary(config, stopwatch.Elapsed);

// One room's whole flow, with a final safety net: RoomScenario already buckets its
// own invoke faults, but an unexpected throw is caught here so it never faults the
// Task.WhenAll or hides the summary.
static async Task RunRoomSafeAsync(Config config, Metrics metrics, SemaphoreSlim connectGate, AiJumbleClient? ai)
{
    try
    {
        var scenario = new RoomScenario(config, metrics, connectGate, ai);
        await scenario.RunAsync();
    }
    catch (Exception ex)
    {
        metrics.RecordError("Room", ex);
    }
}
