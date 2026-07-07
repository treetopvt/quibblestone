// ----------------------------------------------------------------------------
//  Metrics - the thread-safe collector every simulated player writes into.
//
//  Rooms and their players run concurrently, so every counter here is mutated
//  from many threads: scalar counts use Interlocked, per-method latency samples
//  are guarded by a small lock, and the error buckets live in a ConcurrentDictionary.
//  There are NO external stats dependencies (CLAUDE.md keeps the harness light) -
//  percentiles are a tiny nearest-rank helper over the collected samples.
//
//  What it captures (the summary at the end prints all of it):
//    - Connection attempts / successes / failures.
//    - Per-hub-method invocation latency (count, p50, p95, p99, max, mean),
//      measured with a Stopwatch around each InvokeAsync (SUCCESS calls only, so
//      the percentiles describe healthy latency; failures are bucketed separately).
//    - Result-envelope outcomes for CreateRoom / JoinRoom / StartRound / SubmitWord
//      (Ok=true vs a friendly Ok=false rejection - distinct from a thrown error).
//    - Rounds started vs rounds that reached RevealReady (the completion rate).
//    - Broadcasts observed (RoundStarted / CollectProgress / RevealReady / RoundAborted).
//    - Disconnects / reconnects observed (SignalR auto-reconnect) and churn drops/rejoins.
//    - Errors bucketed by "method / ExceptionType".
//
//  Prose style: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.LoadTest;

public sealed class Metrics
{
    // --- connection lifecycle ---
    private long _connAttempted, _connSucceeded, _connFailed;

    // --- result-envelope outcomes (Ok true/false, an EXPECTED validation result) ---
    private long _roomsCreated, _roomsCreateFailed;
    private long _joinsOk, _joinsFailed;
    private long _roundsStarted, _roundsStartRejected;
    private long _submitsAttempted, _submitsOk, _submitsRejected;

    // --- round completion ---
    private long _roundsCompleted, _roundsIncomplete;

    // --- broadcasts observed (server -> client fan-out actually arriving) ---
    private long _bcRoundStarted, _bcCollectProgress, _bcRevealReady, _bcRoundAborted, _bcRosterChanged;

    // --- connection churn / recovery ---
    private long _reconnecting, _reconnected;
    private long _churnDrops, _churnRejoins, _churnRejoinFailed;

    // --- rooms that could not be staffed enough to start ---
    private long _roomsUnderStaffed;

    // --- opt-in AI jumble scenario (POST /api/ai/jumble). Every outcome except a
    //     transport error is a HEALTHY signal (the gate + fallback working). ---
    private long _aiAttempted, _aiGenerated, _aiFellBack, _aiRateLimited, _aiHttpError;

    // Per-method latency series, and error buckets keyed "method / ExceptionType".
    private readonly ConcurrentDictionary<string, LatencySeries> _latency = new();
    private readonly ConcurrentDictionary<string, long> _errors = new();

    // ----- connection lifecycle -------------------------------------------------
    public void ConnectAttempted() => Interlocked.Increment(ref _connAttempted);
    public void ConnectSucceeded() => Interlocked.Increment(ref _connSucceeded);
    public void ConnectFailed() => Interlocked.Increment(ref _connFailed);

    // ----- result-envelope outcomes ---------------------------------------------
    public void RoomCreated() => Interlocked.Increment(ref _roomsCreated);
    public void RoomCreateFailed() => Interlocked.Increment(ref _roomsCreateFailed);
    public void JoinOk() => Interlocked.Increment(ref _joinsOk);
    public void JoinFailed() => Interlocked.Increment(ref _joinsFailed);
    public void RoundStarted() => Interlocked.Increment(ref _roundsStarted);
    public void RoundStartRejected() => Interlocked.Increment(ref _roundsStartRejected);
    public void SubmitAttempted() => Interlocked.Increment(ref _submitsAttempted);
    public void SubmitOk() => Interlocked.Increment(ref _submitsOk);
    public void SubmitRejected() => Interlocked.Increment(ref _submitsRejected);

    // ----- round completion -----------------------------------------------------
    public void RoundCompleted() => Interlocked.Increment(ref _roundsCompleted);
    public void RoundIncomplete() => Interlocked.Increment(ref _roundsIncomplete);
    public void RoomUnderStaffed() => Interlocked.Increment(ref _roomsUnderStaffed);

    // ----- broadcasts observed --------------------------------------------------
    public void BroadcastRoundStarted() => Interlocked.Increment(ref _bcRoundStarted);
    public void BroadcastCollectProgress() => Interlocked.Increment(ref _bcCollectProgress);
    public void BroadcastRevealReady() => Interlocked.Increment(ref _bcRevealReady);
    public void BroadcastRoundAborted() => Interlocked.Increment(ref _bcRoundAborted);
    public void BroadcastRosterChanged() => Interlocked.Increment(ref _bcRosterChanged);

    // ----- churn / recovery -----------------------------------------------------
    public void Reconnecting() => Interlocked.Increment(ref _reconnecting);
    public void Reconnected() => Interlocked.Increment(ref _reconnected);
    public void ChurnDrop() => Interlocked.Increment(ref _churnDrops);
    public void ChurnRejoin() => Interlocked.Increment(ref _churnRejoins);
    public void ChurnRejoinFailed() => Interlocked.Increment(ref _churnRejoinFailed);

    // ----- AI jumble outcomes ---------------------------------------------------
    public void AiAttempted() => Interlocked.Increment(ref _aiAttempted);
    public void AiGenerated() => Interlocked.Increment(ref _aiGenerated);   // 200, fellBack=false, real words
    public void AiFellBack() => Interlocked.Increment(ref _aiFellBack);     // 200, fellBack=true (gate degraded - healthy)
    public void AiRateLimited() => Interlocked.Increment(ref _aiRateLimited); // 429 (per-IP limiter - healthy)
    public void AiHttpError() => Interlocked.Increment(ref _aiHttpError);   // other non-2xx

    /// <summary>Record one SUCCESSFUL invocation's wall-clock latency (ms) for a hub method.</summary>
    public void RecordLatency(string method, double milliseconds) =>
        _latency.GetOrAdd(method, _ => new LatencySeries()).Add(milliseconds);

    /// <summary>Bucket an error by method + exception type (e.g. "SubmitWord / HubException").</summary>
    public void RecordError(string method, Exception ex)
    {
        var key = $"{method} / {ex.GetType().Name}";
        _errors.AddOrUpdate(key, 1, (_, n) => n + 1);
    }

    /// <summary>
    /// Time a hub InvokeAsync: record its latency on success, bucket its exception on
    /// failure, and return the result (or null on failure so callers stay resilient -
    /// one bad invoke never tears down a whole room). All harness invokes route through
    /// here so latency + errors are captured uniformly.
    /// </summary>
    public async Task<T?> TimeInvokeAsync<T>(string method, Func<Task<T>> invoke) where T : class
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var result = await invoke();
            RecordLatency(method, ElapsedMs(start));
            return result;
        }
        catch (Exception ex)
        {
            RecordError(method, ex);
            return null;
        }
    }

    /// <summary>Time a void-returning hub invoke (e.g. LeaveRoom): record latency on success, bucket exceptions, never throw.</summary>
    public async Task TimeInvokeVoidAsync(string method, Func<Task> invoke)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await invoke();
            RecordLatency(method, ElapsedMs(start));
        }
        catch (Exception ex)
        {
            RecordError(method, ex);
        }
    }

    private static double ElapsedMs(long startTimestamp) =>
        System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    /// <summary>Print the full end-of-run summary.</summary>
    public void PrintSummary(Config config, TimeSpan wall)
    {
        var seconds = Math.Max(wall.TotalSeconds, 0.0001);

        Console.WriteLine();
        Console.WriteLine("================ QuibbleStone load summary ================");
        Console.WriteLine($"wall clock            : {wall.TotalSeconds:0.00} s");
        Console.WriteLine();

        Console.WriteLine("connections");
        Console.WriteLine($"  attempted           : {Read(ref _connAttempted)}");
        Console.WriteLine($"  succeeded           : {Read(ref _connSucceeded)}");
        Console.WriteLine($"  failed              : {Read(ref _connFailed)}");
        Console.WriteLine();

        Console.WriteLine("rooms + rounds");
        Console.WriteLine($"  rooms created       : {Read(ref _roomsCreated)} / {config.Rooms}");
        Console.WriteLine($"  rooms create-failed : {Read(ref _roomsCreateFailed)}");
        Console.WriteLine($"  rooms understaffed  : {Read(ref _roomsUnderStaffed)}  (fewer than 2 players seated - could not start)");
        Console.WriteLine($"  joins ok / failed   : {Read(ref _joinsOk)} / {Read(ref _joinsFailed)}");
        var started = Read(ref _roundsStarted);
        var completed = Read(ref _roundsCompleted);
        var completionRate = started > 0 ? 100.0 * completed / started : 0.0;
        Console.WriteLine($"  rounds started      : {started}");
        Console.WriteLine($"  rounds start-reject : {Read(ref _roundsStartRejected)}");
        Console.WriteLine($"  rounds completed    : {completed}  (reached RevealReady)");
        Console.WriteLine($"  rounds incomplete   : {Read(ref _roundsIncomplete)}  (timed out / aborted before reveal)");
        Console.WriteLine($"  completion rate     : {completionRate:0.0}%");
        Console.WriteLine($"  submits ok/rej/att  : {Read(ref _submitsOk)} / {Read(ref _submitsRejected)} / {Read(ref _submitsAttempted)}");
        Console.WriteLine();

        Console.WriteLine("broadcasts observed (server -> client fan-out arriving)");
        Console.WriteLine($"  RoundStarted        : {Read(ref _bcRoundStarted)}");
        Console.WriteLine($"  CollectProgress     : {Read(ref _bcCollectProgress)}");
        Console.WriteLine($"  RevealReady         : {Read(ref _bcRevealReady)}");
        Console.WriteLine($"  RoundAborted        : {Read(ref _bcRoundAborted)}");
        Console.WriteLine($"  RosterChanged       : {Read(ref _bcRosterChanged)}");
        Console.WriteLine();

        Console.WriteLine("disconnects / reconnects");
        Console.WriteLine($"  auto reconnecting   : {Read(ref _reconnecting)}");
        Console.WriteLine($"  auto reconnected    : {Read(ref _reconnected)}");
        Console.WriteLine($"  churn drops         : {Read(ref _churnDrops)}");
        Console.WriteLine($"  churn rejoins ok    : {Read(ref _churnRejoins)}");
        Console.WriteLine($"  churn rejoin failed : {Read(ref _churnRejoinFailed)}");
        Console.WriteLine();

        if (config.Ai)
        {
            Console.WriteLine("AI jumble (POST /api/ai/jumble) - all but 'transport errors' are HEALTHY gate signals");
            Console.WriteLine($"  attempted           : {Read(ref _aiAttempted)}");
            Console.WriteLine($"  ai-generated        : {Read(ref _aiGenerated)}  (200, real words - a paid gpt-5-mini call)");
            Console.WriteLine($"  fell back           : {Read(ref _aiFellBack)}  (200, gate degraded: quota/breaker/no-AI -> free reshuffle)");
            Console.WriteLine($"  rate limited (429)  : {Read(ref _aiRateLimited)}  (per-IP 30/min guard - expected under load)");
            Console.WriteLine($"  other http errors   : {Read(ref _aiHttpError)}");
            Console.WriteLine("  (transport errors, if any, are in the error buckets below under 'AiJumble')");
            Console.WriteLine();
        }

        Console.WriteLine("throughput");
        Console.WriteLine($"  rounds completed/s  : {completed / seconds:0.00}");
        Console.WriteLine($"  submits ok/s        : {Read(ref _submitsOk) / seconds:0.00}");
        Console.WriteLine();

        Console.WriteLine("per-method latency (ms), successful invokes only");
        Console.WriteLine($"  {"method",-14}{"count",8}{"p50",10}{"p95",10}{"p99",10}{"max",10}{"mean",10}");
        foreach (var method in _latency.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var s = _latency[method].Snapshot();
            Console.WriteLine(
                $"  {method,-14}{s.Count,8}{s.P50,10:0.0}{s.P95,10:0.0}{s.P99,10:0.0}{s.Max,10:0.0}{s.Mean,10:0.0}");
        }
        Console.WriteLine();

        Console.WriteLine("errors (method / exception type)");
        if (_errors.IsEmpty)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var (key, count) in _errors.OrderByDescending(e => e.Value))
            {
                Console.WriteLine($"  {count,8}  {key}");
            }
        }
        Console.WriteLine("===========================================================");
    }

    private static long Read(ref long field) => Interlocked.Read(ref field);

    /// <summary>
    /// One method's latency samples. Guarded by a lock (simple + correct for a dev
    /// tool); Snapshot sorts a copy and computes nearest-rank percentiles.
    /// </summary>
    private sealed class LatencySeries
    {
        private readonly object _lock = new();
        private readonly List<double> _samples = new();

        public void Add(double ms)
        {
            lock (_lock)
            {
                _samples.Add(ms);
            }
        }

        public LatencySnapshot Snapshot()
        {
            double[] sorted;
            lock (_lock)
            {
                sorted = _samples.ToArray();
            }
            Array.Sort(sorted);

            var n = sorted.Length;
            if (n == 0)
            {
                return new LatencySnapshot(0, 0, 0, 0, 0, 0);
            }

            double sum = 0;
            foreach (var v in sorted)
            {
                sum += v;
            }

            return new LatencySnapshot(
                Count: n,
                P50: Percentile(sorted, 50),
                P95: Percentile(sorted, 95),
                P99: Percentile(sorted, 99),
                Max: sorted[n - 1],
                Mean: sum / n);
        }

        // Nearest-rank percentile over an ascending-sorted array (no interpolation -
        // enough for a load harness, and avoids a stats dependency).
        private static double Percentile(double[] sorted, double p)
        {
            var n = sorted.Length;
            var rank = (int)Math.Ceiling(p / 100.0 * n);
            rank = Math.Clamp(rank, 1, n);
            return sorted[rank - 1];
        }
    }

    private readonly record struct LatencySnapshot(int Count, double P50, double P95, double P99, double Max, double Mean);
}
