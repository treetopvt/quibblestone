// ----------------------------------------------------------------------------
//  HubTelemetryFilter - makes SignalR hub METHOD failures observable in
//  Application Insights (platform-devops/04, AC-03).
//
//  WHY A HUB FILTER: the real-time hub is the scary path (README section 4) - a
//  failing round-start or a disconnect storm must be diagnosable, not invisible.
//  A plain REST 500 already surfaces via the request pipeline, but a SignalR hub
//  invocation that THROWS does not travel that path, so without this filter a hub
//  method exception would go untracked. An IHubFilter wraps EVERY hub invocation
//  on the ONE GameHub, so this is the single, uniform place hub-method exceptions
//  become telemetry - no per-method try/catch, no fork of the hub.
//
//  NO PII, EVER (AC-04, README section 6): when an invocation throws we track the
//  exception carrying ONLY the hub METHOD NAME (e.g. "StartRound"). We NEVER touch
//  invocationContext.HubMethodArguments - those args are exactly the sensitive
//  payload (nicknames, submitted words, join codes) this story must never log.
//  The scrubber (PiiScrubbingTelemetryInitializer) is the backstop, but this
//  filter is written so there is nothing to scrub in the first place: the only
//  custom property attached is the anonymous method name.
//
//  The abnormal-DISCONNECT half of AC-03 lives in GameHub.OnDisconnectedAsync
//  (a hub filter does not see disconnects) - see that override; both halves
//  resolve TelemetryClient from DI and emit anonymous, no-PII telemetry.
//
//  Registered via AddSignalR(o => o.AddFilter<HubTelemetryFilter>()) in
//  Program.cs. When no connection string is configured TelemetryClient is still
//  registered (the SDK is always added) and simply no-ops the track calls, so
//  this filter is a clean pass-through locally (AC-05) - it only ever ADDS a
//  re-throw around the real invocation.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.SignalR;

namespace QuibbleStone.Api.Telemetry;

/// <summary>
/// An <see cref="IHubFilter"/> that tracks an exception in Application Insights
/// whenever a hub method invocation throws (AC-03), carrying ONLY the anonymous
/// hub method name - never the arguments (which include nicknames / words / codes,
/// AC-04). It re-throws so the hub's own error handling is unchanged. Resolves
/// <see cref="TelemetryClient"/> from DI; a clean no-op when no connection string
/// is configured (AC-05).
/// </summary>
public sealed class HubTelemetryFilter : IHubFilter
{
    // The custom-property key under which we record the (anonymous) hub method
    // name on a tracked exception. Never a value from the arguments.
    private const string HubMethodPropertyKey = "HubMethod";

    private readonly TelemetryClient _telemetryClient;

    public HubTelemetryFilter(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }

    /// <summary>
    /// Wraps every hub method invocation: on a throw, tracks the exception with
    /// ONLY the anonymous method name attached, then re-throws so the framework's
    /// own handling (and the caller's result envelope, where used) is unchanged.
    /// </summary>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex)
        {
            // Track ONLY the anonymous method name (AC-04) - never the arguments,
            // which carry nicknames / words / codes. The exception type + stack are
            // anonymous operational data (AC-04's allowed shape).
            var telemetry = new ExceptionTelemetry(ex);
            telemetry.Properties[HubMethodPropertyKey] = invocationContext.HubMethodName;
            _telemetryClient.TrackException(telemetry);

            // Re-throw: this filter OBSERVES failures, it does not swallow them -
            // the hub's own error path (and any result envelope) is unchanged.
            throw;
        }
    }
}
