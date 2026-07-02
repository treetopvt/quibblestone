# Story: Operational observability (Application Insights)

**Feature:** Platform & DevOps  ·  **Status:** In Review  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #106

## Context
The app is effectively unobserved: there is no Application Insights, no exception
tracking, no request/latency/dependency metrics, and no signal at all when something
breaks in a deployed environment. When a round-start throws, a hub connection storms,
or an endpoint 500s, nobody finds out. `platform-devops/feature.md` already names
"observability" as part of this feature's remit but never specified it; this story is
that first cut - the "find and fix bugs" foundation. It wires Application Insights into
the API (and a light client-side error beacon), kept tiny per README section 9 and
strictly no-PII per README section 6 (the audience is kids - operational telemetry must
never carry a nickname, join code, or any story content). Product/usage analytics is a
SEPARATE story (`platform-devops/05`); this one is purely operational health. See
[feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given the dev/UAT footprint, then `infra/main.bicep` provisions an
      Application Insights resource (workspace-based, so also the Log Analytics
      workspace it requires) and exposes its connection string to the API as
      configuration - via an App Service app setting sourced from Key Vault, NEVER
      committed to source and NEVER a `VITE_*` var. The added footprint is documented
      in `infra/README.md` (README section 9 - keep it tiny, make it earn its place).
- [ ] AC-02: Given the API is running with a connection string, then it emits telemetry
      to Application Insights - unhandled exceptions, failed requests (4xx/5xx), request
      rate + duration, and outbound dependency calls - wired via the ASP.NET Core
      Application Insights SDK in `Program.cs`. Verifiable: a deliberately thrown
      exception (or any 500) shows up in App Insights within minutes.
- [ ] AC-03: Given the real-time hub is the scary path (README section 4), then SignalR
      hub failures are observable - a hub method exception and an abnormal disconnect
      surface as telemetry (exception/custom-event on `GameHub`), so a disconnect storm
      or a failing round-start is diagnosable, not invisible.
- [ ] AC-04 (child-safety / PII, non-negotiable): Given any telemetry, then it NEVER
      carries PII or content - no nickname (free text), join code, player session id,
      submitted word, or story text. A telemetry initializer/processor strips or drops
      anything carrying them; only anonymous operational data (route templates, status
      codes, durations, exception types/stacks, dependency names) is sent (README
      section 6). Story content and player words are never logged, ever.
- [ ] AC-05: Given local development, then App Insights is a clean no-op when no
      connection string is present - local runs neither require it nor emit anywhere,
      and no key or connection string is ever committed. Secrets stay in GitHub
      secrets / Key Vault, never in `VITE_*` (which ships to the browser).
- [ ] AC-06 (light web layer): Given the web client, then it reports unhandled
      client-side JS errors anonymously (a minimal error beacon or the App Insights JS
      SDK). If the JS SDK's bundle cost is not worth it on a PWA, a minimal manual
      beacon is acceptable - flag the bundle-size tradeoff at build time (CLAUDE.md
      section 10). Client error reports honor the SAME no-PII rule as AC-04.
- [ ] AC-07: Given the telemetry exists, then at least a minimal ALERT seam is set up or
      documented (e.g. an alert on a server-exception or failed-request spike) so a
      failure notifies rather than sitting silent. Keep it minimal - one or two signals,
      not a full alerting suite.

## Out of Scope
- Product / usage analytics - game types played, session length, approximate unique
  users - that is `platform-devops/05` (this story is operational health only).
- Custom dashboards / workbooks beyond App Insights' default views (demand-driven,
  README section 12).
- Distributed tracing across the Azure SignalR Service (the hub is still in-process;
  Azure SignalR is a later `.AddAzureSignalR(...)` wiring, CLAUDE.md section 10).
- Production hardening: sampling-rate tuning, long retention, private endpoints, a
  separate prod resource (dev/UAT footprint only here).
- Logging story content, player words, or any per-player data (never - AC-04).
- Third-party APM/error tools (Sentry, Datadog) - App Insights is the Azure-native fit
  for this footprint.

## Technical Notes
- **Bicep:** add `Microsoft.Insights/components` (workspace-based) plus the required
  `Microsoft.OperationalInsights/workspaces` to `infra/main.bicep`; output the
  connection string, store it as a Key Vault secret, and reference it from the API App
  Service as the `APPLICATIONINSIGHTS_CONNECTION_STRING` app setting. This is the first
  real consumer of the provisioned-but-unused Key Vault (CLAUDE.md section 10). Keep the
  two new resources documented and minimal (README section 9).
- **API:** add `Microsoft.ApplicationInsights.AspNetCore`; call
  `builder.Services.AddApplicationInsightsTelemetry()` in `Program.cs` (reads the
  connection string from config). Register an `ITelemetryInitializer` /
  `ITelemetryProcessor` that scrubs/drops PII (AC-04) - treat this as the single
  choke point so no call site has to remember.
- **Hub:** emit exception telemetry from `GameHub` method failures and
  `OnDisconnectedAsync` abnormal closes (AC-03) - anonymous, no room/nickname payload.
- **Config:** connection string via config only; no-op when absent (AC-05). Never in
  `VITE_*`.
- **Coordinate telemetry philosophy:** `story-selection/04` designs an anonymous
  content SERVE log (Table Storage) and `platform-devops/05` will add anonymous USAGE
  events - keep all three coherent (operational vs content-curation vs product-usage),
  do not let divergent, overlapping telemetry systems accrete. This story owns the
  operational pipeline the other two coordinate with.
- **Web:** if using the App Insights JS SDK, watch the PWA bundle size (CLAUDE.md
  section 10); a tiny manual `fetch` beacon to a minimal endpoint is a fine fallback.

## Tests
| AC | Test |
|---|---|
| AC-01 | `az bicep build`/validate + manual: the App Insights + workspace resources deploy; the connection string reaches the API as a Key Vault-backed app setting, nothing committed |
| AC-02 | manual: trigger a 500 / thrown exception on the deployed API; confirm it appears in App Insights (Failures) with route + status, no PII |
| AC-03 | manual: force a hub method error / abnormal disconnect; confirm it surfaces as telemetry |
| AC-04 | code review + manual: the initializer/processor strips nickname/join-code/session-id/content; inspect a sample of live telemetry for any PII leak |
| AC-05 | manual: run the API locally with no connection string; confirm no telemetry attempts and no error; grep the repo for any committed key |
| AC-06 | manual: throw an unhandled error in the web client; confirm an anonymous error report arrives (or the beacon fires); check bundle-size delta |
| AC-07 | manual: the exception/failed-request alert exists (or is documented) and fires on a spike |

## Dependencies
- platform-devops/02-deploy-to-dev (a reachable deployed environment to emit telemetry from)
- infra (Key Vault holds the connection string; App Insights + Log Analytics added to the Bicep footprint)
- child-safety/01-profanity-filter (the no-PII / no-content posture the scrubber enforces)
