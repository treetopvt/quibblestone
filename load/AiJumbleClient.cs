// ----------------------------------------------------------------------------
//  AiJumbleClient - the opt-in (--ai) REST driver for the AI "Fresh Runes" jumble.
//
//  This is the ONLY code path in the harness that can incur AI cost, and it only
//  runs when --ai is passed. It POSTs to /api/ai/jumble (api/src/Controllers/
//  AiJumbleController.cs) exactly the way the web client does (web/src/ai/
//  jumbleClient.ts): a controlled category + the family-safe flag + the group
//  join code (which the server resolves to the anonymous Room.InstanceId to key
//  the gate's per-session quota + attribution). No PII, no story text.
//
//  EVERY outcome except a transport exception is a HEALTHY signal to RECORD, not a
//  failure (this is the AI cost gate WORKING):
//    - 200 + fellBack=false + words -> a real, moderated gpt-5-mini generation (a
//      small real cost on a target where Ai:Endpoint is set, e.g. UAT).
//    - 200 + fellBack=true          -> the gate degraded (quota exhausted, breaker
//      open, or AI simply not configured) and the client would reshuffle for free.
//    - 429                          -> the per-IP abuse limiter (30/min) bit, as it
//      is designed to under a burst of load.
//  Only a thrown HttpRequestException / timeout is bucketed as an error.
//
//  Cost is bounded server-side (documented in the README): per-session quota (20),
//  per-IP limiter (30/min), and the $20/month spend circuit-breaker. A jumble is a
//  few hundred tokens on gpt-5-mini ($0.25/$2.00 per 1M in/out), i.e. a fraction of
//  a cent per call - a default --ai run (a couple of calls per room) is negligible.
//
//  Prose style: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

namespace QuibbleStone.LoadTest;

public sealed class AiJumbleClient
{
    // A controlled set of category labels (letters only, well within the controller's
    // 24-char letters/hyphens rule) so the request always passes input validation and
    // reaches the gate rather than degrading on a bad category. Not player free text.
    private static readonly string[] Categories =
    {
        "noun", "verb", "adjective", "animal", "food", "place", "color", "thing",
    };

    private readonly HttpClient _http;
    private readonly Metrics _metrics;

    public AiJumbleClient(HttpClient http, Metrics metrics)
    {
        _http = http;
        _metrics = metrics;
    }

    /// <summary>
    /// Fire ONE jumble request for a live room (keyed on its join code). Records the
    /// latency and the outcome bucket; never throws (a transport fault is bucketed as
    /// an "AiJumble" error and swallowed, so it can never fail the room's flow).
    /// </summary>
    public async Task FireAsync(string roomCode, CancellationToken cancellationToken)
    {
        _metrics.AiAttempted();

        // The wire shape AiJumbleController binds (camelCase; MVC binds case-insensitively).
        // Group play: send the join code (-> anonymous InstanceId), no solo sessionId.
        var body = new
        {
            category = Categories[Random.Shared.Next(Categories.Length)],
            familySafe = true,
            avoid = (string[]?)null,
            themes = (string[]?)null,
            roomCode,
            sessionId = (string?)null,
        };

        var start = Stopwatch.GetTimestamp();
        try
        {
            using var response = await _http.PostAsJsonAsync("/api/ai/jumble", body, cancellationToken);
            _metrics.RecordLatency("AiJumble", Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _metrics.AiRateLimited();   // per-IP limiter working (healthy under load)
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                _metrics.AiHttpError();
                return;
            }

            var parsed = await response.Content.ReadFromJsonAsync<AiJumbleResponseDto>(cancellationToken);
            if (parsed is null)
            {
                _metrics.AiHttpError();
                return;
            }

            if (parsed.FellBack || parsed.Words is null || parsed.Words.Count == 0)
            {
                _metrics.AiFellBack();      // gate degraded -> free reshuffle (healthy)
            }
            else
            {
                _metrics.AiGenerated();     // a real, moderated AI generation
            }
        }
        catch (Exception ex)
        {
            // Network failure / timeout only. Bucketed, never rethrown.
            _metrics.RecordError("AiJumble", ex);
        }
    }
}
