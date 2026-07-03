<!--
  Story 04 of the AI cost gate - the real-time spend circuit-breaker + per-feature/per-session
  cost attribution telemetry. This is the piece that actually enforces the $20 ceiling. No em dashes.
-->

# Story: Spend circuit-breaker + cost attribution telemetry

**Feature:** AI Cost Gate  ·  **Status:** Not Started  ·  **Issue:** #123

## Context
The gate's fourth piece and the one that actually enforces the $20/month ceiling
(feature.md; ROADMAP "The AI cost gate" piece 4; cost-control section). Azure
billing data lags hours, so a runaway cannot wait for it - this story estimates $
per AI call from the response token usage x the model rate, keeps a running monthly
total in the already-provisioned Table Storage, and at 100% of the ceiling STOPS
calling AI so every AI feature degrades to its deterministic fallback (the free
jumble). It also emits ONE telemetry event per AI call carrying a feature tag,
model, token counts, estimated cost, and the anonymous session/room id - so we can
answer "which AI feature costs most", "is spend concentrated or even", and "is one
anonymous session running hot". The feature dimension ships from day one even
though only the jumble exists. See [feature.md](./feature.md) and
[ADR 0001](../../adr/0001-ai-provider.md).

## Acceptance Criteria
- [ ] AC-01 (estimate per call): Given an AI call returns token usage (story 01
      AC-02), then the proxy estimates its cost as
      `(inputTokens * inputRate + outputTokens * outputRate) / 1e6` using the deployed
      model's configured rates (gpt-5-mini: 0.25 / 2.00 per 1M; a config constant so
      a model swap is one change), and this estimate is recorded.
- [ ] AC-02 (running monthly total, persisted): Given estimates accrue, then a
      running total for the current UTC month is persisted in Azure Table Storage
      (the already-provisioned account the serve log/telemetry sink uses) - it
      survives a process recycle/redeploy (unlike the in-memory quota of story 03),
      because it is the authoritative fast-path spend figure.
- [ ] AC-03 (the breaker): Given the running monthly total reaches 100% of the $20
      ceiling (the ceiling is configuration, not a literal in many places), then the
      gate STOPS calling AI for the rest of that UTC month and every AI feature
      degrades to its deterministic fallback (the free reshuffle for the jumble) -
      players still play, just without AI. At the start of a new UTC month the total
      resets and AI resumes.
- [ ] AC-04 (degrade, never bill-or-break): Given the breaker is open, then the
      player experience is the deterministic fallback with no error and no charge -
      "degrade, not bill" (feature.md). This is the same fallback path story 03 uses
      at quota, so the two share one graceful-degrade seam.
- [ ] AC-05 (one attribution event per call): Given any AI call (allowed and
      completed), then it emits exactly ONE Application Insights custom event via the
      `platform-devops/04` pipeline (`TelemetryClient.TrackEvent`) carrying:
      a FEATURE tag (`jumble` now; `verdict`/`onDemand` reserved), the model id, input
      and output token counts, the estimated cost, and the ANONYMOUS session/room id
      (`Room.InstanceId`). The feature dimension is present from day one (AC restated:
      do not defer it).
- [ ] AC-06 (child-safety / PII, non-negotiable): Given the attribution event, then
      it carries NO PII or content - no nickname, join code, player session id, IP,
      submitted word, or generated text; only the anonymous InstanceId, feature tag,
      model, token counts, and cost. It flows through the existing
      `PiiScrubbingTelemetryInitializer` choke point, and its property/metric keys are
      chosen to pass that scrubber (mirrors `UsageTelemetry`'s allowed keys) (README
      section 6).
- [ ] AC-07 (per-feature + per-session answerable): Given the events, then a
      straightforward App Insights query yields a per-FEATURE cost breakdown (which
      AI feature costs most) and a per-SESSION distribution (spend by anonymous
      InstanceId) - no dashboard required (demand-driven, README section 12).
- [ ] AC-08 (hot-session signal): Given the per-session distribution, then a
      disproportionately-spending anonymous session/room is made VISIBLE as a
      concentration/abuse signal (e.g. a flagged event or a queryable threshold),
      in addition to being rate-limited by the per-session quota (story 03).
      Concentration is measured over anonymous session/room ids ONLY - never
      identity (README section 6).
- [ ] AC-09 (fail-soft telemetry): Given a telemetry or Table Storage write fails,
      then it never blocks, delays, or errors a round or an AI call - fire-and-forget,
      the same posture `platform-devops/05` AC-08 sets. BUT the spend total write
      (AC-02) is best-effort-then-safe: if the running total cannot be read to check
      the breaker, the gate treats spend as at-ceiling and degrades to fallback
      rather than calling AI blind (fail to the safe side, mirrors story 03 AC-07).
- [ ] AC-10 (optional faster warning): Given the running estimate, then the story at
      least DOCUMENTS (and may wire) an App Insights metric alert on the app's
      running monthly estimate at 25/50/75/100% for a pre-billing warning, distinct
      from the authoritative Azure budget alerts (story 06). Recommended as a light
      add; the budget action group remains the authoritative notify path.

## Out of Scope
- The Azure Cost Management $20 budget + action-group email alerts - that is the
  BACKSTOP, and it is Bicep, owned by story 06. This story is the real-time app-level
  breaker + attribution; the two are reconciled periodically (feature.md).
- The proxy transport (story 01), the entitlement check (story 02), and the
  per-session quota (story 03) - this story consumes story 01's token usage and
  coordinates with 03's fallback path.
- A spend dashboard/workbook (queryable without one, AC-07).
- Per-feature separate ceilings - one shared $20 ceiling now; attribution enables
  per-feature budgets later (feature.md Parked).
- Reconciling the estimate against actual Azure billing automatically - the
  reconciliation is a periodic human/ops check for now (feature.md), not code here.

## Technical Notes
- **Estimate + breaker live in the proxy path** (`api/src/Ai/`), between the quota
  check (story 03) and, or wrapping, `IAiCompletionClient.CompleteAsync`. Order:
  entitlement (captured, story 02) -> quota (story 03) -> breaker check (this) -> AI
  call -> estimate + record + emit telemetry (this). The breaker check reads the
  persisted monthly total BEFORE calling AI; the estimate/record happens AFTER.
- **Persisted total:** a small Table Storage row keyed by the UTC month
  (PartitionKey = e.g. `spend`, RowKey = `YYYY-MM`), incremented per call. Reuse the
  existing `Azure.Data.Tables` dependency + the storage account/connection
  (`Telemetry:StorageConnectionString`) the serve-log sink already uses - no new
  resource. Concurrency: use an atomic-enough update (ETag retry or a merge) so
  concurrent calls do not lose increments; exactness to the penny is not required,
  but it must not systematically undercount past the ceiling.
- **Telemetry vocabulary:** add an `AiCostTelemetry`-style pure builder mirroring
  `api/src/Telemetry/UsageTelemetry.cs` (constants for the event name + property/
  metric builders): properties `{ feature, model }` (+ `hot` flag for AC-08),
  metrics `{ inputTokens, outputTokens, estCostUsd }`, and the anonymous
  `InstanceId` as the grouping property. Emit with the injected `TelemetryClient`
  (already in `GameHub`); it flows through `PiiScrubbingTelemetryInitializer`
  automatically. Confirm the new keys are NOT in the scrubber's sensitive set (they
  are anonymous - `feature`/`model`/`instanceId`/token counts/cost all pass, like
  the allowed `mode`/`context`/`deviceId`).
- **Rates config:** keep the per-model input/output rates beside the model id
  (story 01), so the estimate and a model swap stay in one place (ADR 0001).
- **Ceiling config:** the $20 value is a single config setting, not scattered.
- Verbose header comment on the breaker + the telemetry builder (CLAUDE.md section
  4). Async; nullable; fail to the safe side (AC-09).

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests/Ai/AiCostEstimatorTests.cs`: tokens x rates yields the expected estimate for gpt-5-mini |
| AC-02 | `api/tests` (Table Storage emulator/fake): the monthly total round-trips and survives a simulated restart |
| AC-03 | `api/tests`: at 100% of a (test) ceiling, the breaker opens and no AI call is made; a new month resets it |
| AC-04 | manual + `api/tests`: with the breaker open, the jumble returns the deterministic reshuffle, no error/charge |
| AC-05 | `api/tests` + manual (App Insights): one event per call with feature/model/tokens/cost/InstanceId |
| AC-06 | code review + live-sample: no PII/content on the event; keys pass the PII scrubber |
| AC-07 | manual: App Insights queries return per-feature cost and per-session (InstanceId) distribution |
| AC-08 | manual: a hot anonymous session surfaces as a flagged/queryable concentration signal, no identity |
| AC-09 | `api/tests`: a failed telemetry/total write does not break the round; an unreadable total degrades to fallback |
| AC-10 | code review: the app-estimate metric alert at 25/50/75/100% is wired or documented, distinct from story 06 |

## Dependencies
- cost-gate/01 (the proxy + its token-usage result this consumes).
- cost-gate/03 (the shared graceful-degrade fallback path).
- `platform-devops/04` (#106) - the App Insights `TelemetryClient` pipeline + the
  `PiiScrubbingTelemetryInitializer` choke point this emits through.
- `platform-devops/05` (#107) - the `UsageTelemetry` builder pattern to mirror.
- `infra` (the provisioned Table Storage account the monthly total persists in).
