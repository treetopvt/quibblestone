// ----------------------------------------------------------------------------
//  TestTelemetry - a no-op Application Insights TelemetryClient for unit tests
//  (platform-devops/04).
//
//  GameHub now takes a TelemetryClient (AC-03: it tracks abnormal disconnects on
//  the OPERATIONAL App Insights pipeline). In production the DI container supplies
//  it; in a unit test we want a client that constructs cleanly and simply drops
//  every Track call - a TelemetryConfiguration with no connection string / no
//  channel does exactly that (the same clean no-op the app relies on locally,
//  AC-05). This mirrors how FakeTelemetrySink stands in for the content serve log.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;

namespace QuibbleStone.Api.Tests;

/// <summary>
/// Supplies a no-op <see cref="TelemetryClient"/> for GameHub tests: a client over
/// an unconfigured <see cref="TelemetryConfiguration"/> whose Track calls go
/// nowhere (no connection string, no channel), so tests exercise the hub's real
/// behaviour without emitting anything.
/// </summary>
public static class TestTelemetry
{
    /// <summary>A TelemetryClient whose Track calls are a clean no-op.</summary>
    public static TelemetryClient NoOp { get; } = new TelemetryClient(new TelemetryConfiguration());
}
