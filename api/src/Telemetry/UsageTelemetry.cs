// ----------------------------------------------------------------------------
//  UsageTelemetry - the SHARED vocabulary + PURE property builders for
//  QuibbleStone's anonymous PRODUCT-USAGE custom events (platform-devops/05).
//
//  WHAT THIS IS (and, just as importantly, what it is NOT): QuibbleStone now has
//  THREE telemetry surfaces, and this is the ONE place that keeps them coherent
//  rather than sprawling into parallel stacks (AC-06):
//    1. OPERATIONAL health (platform-devops/04): "is it broken" - unhandled
//       exceptions, failed requests, abnormal disconnects. Rides App Insights via
//       the injected TelemetryClient (GameHub._appInsights, HubTelemetryFilter,
//       ClientErrorController). See PiiScrubbingTelemetryInitializer.
//    2. CONTENT-CURATION serve log (story-selection/04): "which TEMPLATES get
//       served / liked" - per-template serve + thumbs events written to TABLE
//       STORAGE via ITelemetrySink (a DIFFERENT sink, a DIFFERENT purpose). See
//       TableStorageTelemetrySink / TelemetryController.
//    3. PRODUCT USAGE (this story): "how is the TOY actually used" - which MODES
//       get played, session/round DURATION, and approximate anonymous device
//       reach. This rides App Insights CUSTOM EVENTS on 04's pipeline (the SAME
//       TelemetryClient + the SAME scrubber) - it does NOT stand up a third stack.
//
//  So this class deliberately owns NO transport and NO sink: it is a pure,
//  stateless helper that hands GameHub (group rounds) and UsageController (the
//  solo forwarder) the exact event NAMES and the exact PROPERTY BAGS to pass to
//  TelemetryClient.TrackEvent. Centralizing the shape here is what lets a single
//  unit test assert the no-PII guarantee (AC-04) instead of trusting each call
//  site to remember it.
//
//  ANONYMOUS BY CONSTRUCTION (AC-04, README sections 3 + 6, NON-NEGOTIABLE): the
//  ONLY fields a usage event may ever carry are mode, solo/group context, counts
//  (e.g. player count), a duration, and - for solo only - an anonymous, device-
//  local id (AC-03, an approximate device count, never a verified person). There
//  is NO field here through which a nickname, join/room code, player/connection
//  session id, submitted word, or story text could travel, and the mode is
//  normalized to a stable enum-ish id (never free text). The scrubber
//  (PiiScrubbingTelemetryInitializer) is the backstop; this builder is written so
//  there is nothing to scrub in the first place.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace QuibbleStone.Api.Telemetry;

/// <summary>
/// Pure, stateless vocabulary + property builders for the anonymous product-usage
/// custom events (platform-devops/05). No transport, no state: GameHub and
/// UsageController pass the returned name + properties straight to
/// <c>TelemetryClient.TrackEvent</c>, so every usage event rides 04's App Insights
/// pipeline and its single PII scrubber (AC-04, AC-06). The only fields that can
/// leave here are mode, context, a player count, and an anonymous device id - all
/// anonymous by construction.
/// </summary>
public static class UsageTelemetry
{
    /// <summary>Custom-event name: a round opened (mode + context). AC-01.</summary>
    public const string RoundStartedEvent = "RoundStarted";

    /// <summary>Custom-event name: a round finished (carries a duration metric). AC-02.</summary>
    public const string RoundCompletedEvent = "RoundCompleted";

    /// <summary>The stable, enum-ish mode id (e.g. "classic-blind"); never free text.</summary>
    public const string ModeProperty = "mode";

    /// <summary>The play context: "solo" or "group".</summary>
    public const string ContextProperty = "context";

    /// <summary>An anonymous player COUNT (never identity); attached to group events.</summary>
    public const string PlayerCountProperty = "playerCount";

    /// <summary>
    /// The anonymous, device-local id (AC-03): a random GUID from the solo
    /// client's localStorage. Explicitly ALLOWED to survive the scrubber - it is a
    /// DEVICE count key, never a "player session id" - so it is intentionally NOT
    /// in PiiScrubbingTelemetryInitializer's sensitive-key list. Solo events only.
    /// </summary>
    public const string DeviceIdProperty = "deviceId";

    /// <summary>The round/session duration, in milliseconds, as a custom metric. AC-02.</summary>
    public const string DurationMsMetric = "durationMs";

    /// <summary>Context value for a group (multi-device) round.</summary>
    public const string GroupContext = "group";

    /// <summary>Context value for a solo (single-client) round.</summary>
    public const string SoloContext = "solo";

    /// <summary>The fallback mode label for an unknown/malformed client-supplied mode.</summary>
    public const string UnknownMode = "unknown";

    // The stable, enum-ish mode ids a usage event may carry (mirrors the web's
    // ModeConfig ids, web/src/engine/modes/*). A client-supplied mode outside this
    // set is normalized to "unknown" (NormalizeMode) so a crafted client can never
    // ride arbitrary free text into the usage metrics (AC-01/AC-04). Group rounds
    // use the server-authoritative round.Mode, which is already one of these.
    private static readonly HashSet<string> KnownModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "classic-blind", "word-bank", "progressive-story", "progressive-reveal",
    };

    // The ONLY two shapes the web ever mints for the anonymous device id
    // (web/src/telemetry/deviceId.ts -> safeUuid): a standard crypto.randomUUID
    // GUID, or the non-secure-context fallback "id-<base36>-<base36>". The usage
    // endpoint is UNAUTHENTICATED and deviceId is deliberately allowed past the
    // scrubber (it is a device-count key, AC-03), so we validate its FORMAT here -
    // a crafted client cannot then ride arbitrary free text (a nickname, a word)
    // into the deviceId dimension. Anything that is not one of these shapes is
    // dropped (the event is still recorded, just without a device id).
    private static readonly Regex GuidDeviceIdPattern = new(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private static readonly Regex FallbackDeviceIdPattern = new(
        "^id-[0-9a-z]{1,16}-[0-9a-z]{1,16}$",
        RegexOptions.Compiled);

    /// <summary>
    /// Builds the anonymous property bag for a usage custom event (AC-04). The
    /// result carries ONLY the allowed anonymous fields: mode + context always, a
    /// player count when supplied (group rounds), and an anonymous device id when
    /// supplied (solo rounds). There is intentionally no parameter, and no key,
    /// through which PII could travel; the mode is normalized to a stable enum-ish
    /// id. An empty/whitespace device id is simply omitted rather than recorded.
    /// </summary>
    /// <param name="mode">The stable mode id; normalized (unknown -> "unknown").</param>
    /// <param name="context">"solo" or "group".</param>
    /// <param name="playerCount">An anonymous player count (group); omitted when null.</param>
    /// <param name="deviceId">The anonymous device-local id (solo); omitted when null/empty.</param>
    public static Dictionary<string, string> BuildProperties(
        string mode,
        string context,
        int? playerCount = null,
        string? deviceId = null)
    {
        var properties = new Dictionary<string, string>
        {
            [ModeProperty] = NormalizeMode(mode),
            [ContextProperty] = context,
        };

        if (playerCount is int count)
        {
            properties[PlayerCountProperty] = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Only a WELL-FORMED anonymous device id is recorded (AC-03/AC-04): the
        // endpoint is unauthenticated, so an arbitrary string here could otherwise
        // ride content into the deviceId dimension. A malformed/injected value is
        // simply omitted (the event still counts, just without a device id).
        if (IsWellFormedDeviceId(deviceId))
        {
            properties[DeviceIdProperty] = deviceId;
        }

        return properties;
    }

    /// <summary>
    /// True when <paramref name="deviceId"/> is one of the two shapes the web
    /// actually mints (AC-03): a crypto.randomUUID GUID, or the "id-&lt;base36&gt;-
    /// &lt;base36&gt;" non-secure-context fallback (web/src/telemetry/deviceId.ts).
    /// Anything else - null, empty, over-long, or arbitrary free text from a crafted
    /// client - is rejected so no content can leak through the (allowed) deviceId
    /// dimension. Bounded length first so the regex never runs on a huge input.
    /// </summary>
    public static bool IsWellFormedDeviceId([NotNullWhen(true)] string? deviceId)
    {
        return !string.IsNullOrWhiteSpace(deviceId)
            && deviceId.Length <= 64
            && (GuidDeviceIdPattern.IsMatch(deviceId) || FallbackDeviceIdPattern.IsMatch(deviceId));
    }

    /// <summary>
    /// Builds the single-entry metric bag carrying the round/session duration in
    /// milliseconds (AC-02), clamped to non-negative so a clock skew can never
    /// record a negative session length.
    /// </summary>
    /// <param name="durationMs">The measured duration in milliseconds.</param>
    public static Dictionary<string, double> BuildDurationMetric(double durationMs)
    {
        return new Dictionary<string, double>
        {
            [DurationMsMetric] = Math.Max(0, durationMs),
        };
    }

    /// <summary>
    /// Normalizes a client-supplied mode to a stable, known enum-ish id, defaulting
    /// to "unknown" for null/empty/unrecognized input (AC-01/AC-04). Mirrors the
    /// defensive posture of GameHub.NormalizeVariant / NormalizeLengthPreference:
    /// arbitrary free text can only ever collapse to "unknown", never leak.
    /// </summary>
    public static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode) || !KnownModes.Contains(mode))
        {
            return UnknownMode;
        }

        return mode.ToLowerInvariant();
    }
}
