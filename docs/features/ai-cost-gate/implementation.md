<!--
  Implementation plan for the AI cost gate feature. Bridges feature.md + stories to orchestration.
  Also carries the CROSS-FEATURE master DAG (gate -> free jumble -> AI jumble) since the gate is the
  spine the thin slice rides. Look-ahead pass (2026-07-02): story 06 (IaC) delivered + deployed via
  PR #131 as a separate infra/ai.bicep on its own subscription (see its Wave Plan row + notes below);
  stories 01-05 (code) remain Not Started. No em dashes.
-->

# Implementation Plan: AI Cost Gate

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the
> `orchestrate-feature` skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md). This file also
> holds the **cross-feature master DAG** (bottom) because the gate is the spine the first AI slice
> (`game-modes/07` + `ai-on-demand-generation/05`) rides. Provider/model decisions:
> [`docs/adr/0001-ai-provider.md`](../../adr/0001-ai-provider.md).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| DI + config-presence/no-op registration (the load-bearing pattern) | the `ITelemetrySink` / `AddApplicationInsightsTelemetry` branch on config presence | `api/src/Program.cs` (~line 123) |
| Per-concern service folder layout | `Rooms/`, `Safety/`, `Telemetry/` | `api/src/` (new: `api/src/Ai/`) |
| Anonymous session/room grouping id (for quota + attribution) | `Room.InstanceId` (already the serve-log grouping key) | `api/src/Rooms/Room.cs` |
| Session-creation call site (the one place entitlement runs) | the room-create hub method + the solo entry point | `api/src/Hubs/GameHub.cs`, `web/src/pages/Solo.tsx` |
| Child safety (the hard moderation gate) | the async-by-design `IContentSafetyFilter.CheckAsync` | `api/src/Safety/IContentSafetyFilter.cs`, `ContentSafetyFilter.cs` |
| Family-safe gate | `FamilySafeContentSelector` / `isFamilySafe` | `api/src/Content/`, `web/src/content/familySafe.ts` |
| Telemetry pipeline + PII choke point (attribution emits through this) | `TelemetryClient` (App Insights) + `PiiScrubbingTelemetryInitializer` | `api/src/Telemetry/`, `GameHub.cs` |
| Telemetry vocabulary/builder to mirror for AI cost events | `UsageTelemetry` (event constants + property/metric builders) | `api/src/Telemetry/UsageTelemetry.cs` |
| Durable counters (the persisted monthly spend total) | the existing `Azure.Data.Tables` sink + storage connection | `api/src/Telemetry/TableStorageTelemetrySink.cs`, `infra/main.bicep` (`storage`) |
| Entitlement seam (consumed at session-creation) | `IEntitlementService` + the `ai.*` catalog keys | `api/src/Entitlements/` (billing-entitlements/01, #70 - new) |
| Secrets (provider key) | not needed - keyless cross-subscription managed-identity role assignment instead (see story 06) | `infra/ai.bicep` |
| IaC footprint conventions (naming, tags, role-assignment pattern) | the App Insights + Key Vault + Storage wiring in `main.bicep`, mirrored (not extended - separate file/subscription, see story 06) | `infra/main.bicep` (pattern source), `infra/ai.bicep` (actual AI footprint), `infra/README.md` |
| Per-IP rate limiting | ASP.NET Core `AddRateLimiter` (partitioned) | `api/src/Program.cs` (new registration) |
| AI SDK (spike-validated on net10.0) | `Azure.AI.OpenAI` + `Azure.Identity` | `api/QuibbleStone.Api.csproj` (new refs) |

New surfaces this feature introduces (become reuse targets once built):
- `api/src/Ai/` - `IAiCompletionClient` (proxy, story 01), `IAiQuota` (story 03), the spend
  estimator + breaker + `AiCostTelemetry` (story 04), the moderation composition (story 05). The
  contract every future AI feature (verdict, voice, illustration, on-demand) imports.

## Wave Plan (DAG) - within this feature

Sizing rule: a builder owns files disjoint from its concurrent siblings. Story 01 is the hard
foundation (the `api/src/Ai/` proxy + result type everything imports). Stories 03/04/05 all extend the
proxy call path in `api/src/Ai/`, so they overlap that folder - but on largely separate files
(`AiQuota`, the estimator/breaker/telemetry, the moderation service); the orchestrator either
serializes the ones that touch a shared file (the proxy call pipeline) or gives that pipeline seam to
one builder and lets the leaf services fan out. Story 06 (Bicep) is file-disjoint from all code and,
as delivered, from `infra/main.bicep` too - it lives entirely in its own `infra/ai.bicep` on a separate
subscription (see per-story notes) - and can run in parallel throughout. Story 02 touches the
session-creation call site (`GameHub.CreateRoom`) and depends on billing-entitlements/01.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 server-side AI proxy (foundation) | #120 | `api/src/Ai/IAiCompletionClient.cs` + Foundry/no-op impls, `Program.cs` registration, `.csproj` refs | infra config (soft; no-ops without) | 06 | 1 | high |
| 06 IaC provisioning seam | #125 | `infra/ai.bicep`, `infra/ai.uat.bicepparam`, `infra/README.md` (delivered - separate file/subscription from `main.bicep`, see notes) | infra (existing footprint, as a pattern to mirror, not a shared file) | 01, 02, 03, 04, 05 (disjoint: infra only) | 1 | medium |
| 02 entitlement at session-creation | #121 | edits at `GameHub.CreateRoom`/solo entry; reads `IEntitlementService` | billing-entitlements/01 (#70), session-engine | 03, 04, 05 | 2 | medium |
| 03 rate-limit + quota | #122 | `api/src/Ai/AiQuota*`, `Program.cs` rate-limiter reg, result-envelope field | 01 | 04, 05 (mostly disjoint; coordinate the proxy pipeline seam) | 2 | medium |
| 04 spend breaker + attribution | #123 | `api/src/Ai/AiCost*` (estimator/breaker), `AiCostTelemetry.cs`, Table Storage total | 01, platform-devops/04 | 03, 05 (coordinate the proxy pipeline seam) | 2 | high |
| 05 moderate-before-display | #124 | `api/src/Ai/AiModeration*` composing `IContentSafetyFilter` + family-safe | 01, child-safety/01+02 | 03, 04 | 2 | medium |

**Concurrency per wave:** Wave 1 = {01, 06} (01 the code foundation, 06 the disjoint Bicep - parallel).
Wave 2 = {02, 03, 04, 05} - all extend the gate; 06 is done. The four leaf services are mostly
file-disjoint, but 03/04/05 each hook the proxy CALL PIPELINE (entitlement-captured -> quota -> breaker
-> call -> estimate/emit -> moderate). Orchestrator choice: give the pipeline wiring (the ordered
call path in `api/src/Ai/`) to ONE builder (fold into 01's foundation or a thin 03/04/05 lead) and let
the other leaf services (the quota store, the estimator, the moderation composer) build in parallel
against it. This avoids two builders editing the pipeline file. 02 is independent of the pipeline (it
is the session-creation capture) and runs alongside.

## Per-story tech notes

### 01 - Server-side AI proxy (foundation)
New `api/src/Ai/`. `IAiCompletionClient.CompleteAsync(request, ct) -> AiCompletionResult { Text,
InputTokens, OutputTokens, ModelId, IsAvailable }`. Foundry impl via `Azure.AI.OpenAI`
(spike-validated); no-op impl when `Ai:*` config absent (mirror `ITelemetrySink`). Generic prompt/
system/maxTokens shape - no jumble specifics. Exports the type stories 03/04/05 wrap. Gotcha: fail
soft on provider errors (typed unavailable, at most one bounded retry - the breaker is the spend
guard, not retries).

### 02 - Entitlement at session-creation
Call `IEntitlementService.EvaluateForSession` once at `GameHub.CreateRoom`/solo; capture on the
session. Reserve/consume the `ai.*` key. Default-unlocked; in alpha the jumble does not require it
(ADR 0001 C). Gotcha: #70 is unbuilt - either serialize after it or ship a thin contract-compatible
default-unlocked `IEntitlementService` #70 later subsumes (do NOT invent a different shape).

### 03 - Rate-limit + quota
`IAiQuota` checked before the proxy; per-session (InstanceId) count + ASP.NET `AddRateLimiter`
per-IP. Return remaining count on the result envelope for the meter. Fail-safe: unreadable quota ->
fallback, never unlimited. In-memory quota is fine (only the spend total must persist).

### 04 - Spend breaker + attribution
Estimator: `(in*inRate + out*outRate)/1e6` from story 01's usage, rates a config constant. Persist the
running UTC-month total in Table Storage (ETag-safe increment). Breaker: read total before the call;
at 100% of the `$20` config ceiling, open -> fallback for the rest of the month; reset next month.
Emit ONE `TrackEvent` per call via `AiCostTelemetry` (mirror `UsageTelemetry`): props `{feature, model,
hot?}`, metrics `{inputTokens, outputTokens, estCostUsd}`, anonymous `InstanceId`. Confirm keys pass
`PiiScrubbingTelemetryInitializer`. Gotcha: fail to the safe side - unreadable total = treat as
at-ceiling.

### 05 - Moderate-before-display
Compose `IContentSafetyFilter.CheckAsync` + family-safe over every AI output before return; drop
unsafe, keep survivors, signal "enough left?" so the caller can fall back. Content Safety second layer
behind `ContentSafety:*` config-presence (no-op default). Reusable seam (not jumble-specific);
`ai-on-demand-generation/05` consumes it. Gotcha: no bypass path (AC-07); curated content still skips
the filter unchanged.

### 06 - IaC provisioning seam (delivered as `infra/ai.bicep`, separate PAYG subscription - PR #131)
Planned as one owner in `infra/main.bicep`; delivered instead as a **separate file, `infra/ai.bicep`**,
deployed to a **new resource group (`quibblestone-ai-rg`) on a separate Pay-As-You-Go subscription**
("Playground"), because the app's "Azure for Students" subscription cannot host Azure OpenAI at all
(student offer + spending limit block Cognitive Services OpenAI accounts; the target model family has
0 real-time quota there). Foundry account + **`gpt-5-mini`** deployment (superseded from `gpt-4o-mini` -
that model and the wider 4o/4.1-mini family are `Deprecating` by deploy time, and the cheaper nano
models have 0 real-time quota in eastus2; model name/version/SKU are now Bicep params); optional
Content Safety (param, default off, not deployed); a **cross-subscription keyless managed-identity role
assignment** (the API identity's `principalId` passed as a plain Bicep parameter - no key, no Key Vault
secret needed for the model call); Cost Management `$20` budget (param) + action group email (param,
never hardcoded) at 25/50/75/100% + Forecasted-100. `Ai:Endpoint` / `Ai:Deployment` are set on the API
app as a **post-deploy step** from `ai.bicep` outputs (the two Bicep files/subscriptions cannot share
app-setting wiring). `az bicep build --file infra/ai.bicep` clean; `infra/README.md` documents the
footprint + the hand-off. Gotcha: email + `apiPrincipalId` are deploy inputs, never committed as
secrets (`apiPrincipalId` is a GUID, not sensitive, so it is fine as a committed bicepparam value).

## Cross-cutting concerns
- **Consumers, not new gates.** Every AI feature routes through `api/src/Ai/` (proxy + quota + breaker
  + moderation). A second proxy/quota/filter is a smell to flag.
- **Anonymous, per session (README section 6).** Quota + attribution key off `Room.InstanceId` and a
  transient IP; never a nickname/join-code/account. Attribution flows through the PII scrubber; new
  keys must pass it.
- **Two enforcement layers, reconciled.** App breaker (04, real-time, estimated) is the enforcer;
  Azure budget (06, slow, authoritative) is the backstop. Reconcile periodically.
- **Degrade, not bill or break.** Quota-hit (03), breaker-open (04), and moderation-reject (05) all
  land on ONE graceful fallback: the deterministic reshuffle for the jumble; no error, no charge.
- **Entitlement once, metering separate.** Session-creation entitlement (02) is never a per-call
  check; quota (03) is the how-many-left concern. Do not conflate them.
- **Secrets in Key Vault / deploy inputs, never `VITE_*`, never committed.** Provider key, Content
  Safety key, and the alert email all follow this.
- **No Functions, no new excluded deps.** In-app proxy (ADR 0001 D). Only `Azure.AI.OpenAI` +
  `Azure.Identity` (+ optional Content Safety SDK) are added, all Azure-native. No i18n; no em dashes.

## Cross-feature master DAG (the first AI slice)

The gate is the spine; the thin slice proves it on the cheapest payload. Build order across features:

| Phase | Work | Feature/story | Gates on |
|---|---|---|---|
| **A. Free jumble first (no AI, no gate)** | The "Fresh runes" button + deterministic reshuffle from the curated pool - the always-safe fallback the breaker degrades to. Ships and is fun on its own. | `game-modes/07` (free layer: AC-01/02/06/07) | nothing (only existing Word Bank surface) |
| **B. Gate foundation** | The server-side proxy + IaC seam. | `ai-cost-gate/01` + `/06` | A is independent; can overlap |
| **C. Gate controls** | Entitlement capture, quota/meter, breaker + attribution, moderation seam. | `ai-cost-gate/02, 03, 04, 05` | B (01); 02 also on billing-entitlements/01 (#70) |
| **D. AI generation behind the gate** | The AI word-bank generation (calls the proxy, routed through quota/breaker/moderation) + the client wiring that makes the button prefer AI and fall back to A. | `ai-on-demand-generation/05` (+ its moderation `/02`) | B + C (the whole gate) + A (the fallback) |

**The serialized spine:** `game-modes/07 free layer (A)` and `ai-cost-gate/01+06 (B)` can proceed
early and in parallel; `ai-cost-gate/02-05 (C)` follow the foundation; `ai-on-demand-generation/05
(D)` is last because it needs the entire gate AND the free fallback to degrade to. This is the
"gate foundation serialized first -> free jumble -> AI jumble behind the gate" order the roadmap
calls for. `game-modes/07`'s AI-layer ACs (AC-03/04/05/08) are satisfied by D wiring, not by A.

**Deferred (not in this slice):** Stripe/charging (`billing-entitlements` 02-05); always-on Content
Safety; per-feature budgets; voices/illustration/on-demand-tales (they reuse the gate later).
