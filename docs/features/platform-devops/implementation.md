<!--
  Implementation plan for the platform-devops feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Platform & DevOps

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

The walking-skeleton PR already delivered the monorepo, the one ASP.NET Core app (REST + in-process SignalR), the
React/Vite/MUI client, the five-resource Bicep footprint, and a green build-both CI. The two remaining Slice-1
stories are independent and file-disjoint, so both can run in the first foundation wave. Keep the footprint tiny
(README section 9) - new resources or pipeline steps must earn their place.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| CI pipeline (extend, do not replace) | the build-both GitHub Actions workflow | `.github/workflows/ci.yml` |
| Deploy pipeline (manual, secret-gated) | the existing deploy workflow | `.github/workflows/deploy.yml` |
| IaC (the 5-resource dev footprint) | the existing Bicep | `infra/main.bicep` (+ `infra/main.bicepparam`) |
| App Insights + Log Analytics (story 04, NEW resources) | add to the existing Bicep; connection string via Key Vault | `infra/main.bicep`, `infra/README.md` |
| API host wiring (story 04: `AddApplicationInsightsTelemetry` + PII scrubber) | the one ASP.NET Core composition root | `api/src/Program.cs`, `api/QuibbleStone.Api.csproj` |
| Anonymous telemetry philosophy (stories 04-05 coordinate with it) | the anonymous serve-log stance | `docs/features/story-selection/04-story-delivery-metrics.md` |
| Anonymous device id (story 05) | the device-local, anonymous, account-free posture | `web/src/identity.ts` |
| Smoke target (what the E2E asserts) | the skeleton landing page reaching "Connected" | `web/src/App.tsx`, `web/src/components/ConnectionStatus.tsx` |
| Web config at build time | `import.meta.env` (`VITE_*`) | `web/src/signalr/useGameHub.ts`, `web/.env.development` |
| Test-strategy guidance | the testing agent brief | `.claude/agents/testing-agent.md` |

What this feature **enables** for others:
- A **test harness** (Vitest for pure logic, Playwright for the real-time flow) that every later feature writes its
  tests against - especially the pure engine logic (`template-model`, `game-modes`, `group-play/02`) and the scary
  2-player sync (`group-play`).
- A **reachable dev environment** so the main-session verification checkpoint (playbook Phase 4) can run against
  deployed code, not just localhost.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 test-harness | #18 | `web/vitest.config.ts`, a sample pure-logic test, `playwright.config.ts`, `tests/smoke.spec.ts`, `web/package.json` (dev deps + scripts); edits `.github/workflows/ci.yml` (unit-test step); docs in `web/README.md` / `CLAUDE.md` | none (builds on the skeleton) | 02 (disjoint files), child-safety/01, design-system/01-02, template-model/01 | 1 | medium |
| 02 deploy-to-dev | #19 | edits `.github/workflows/deploy.yml` (secrets/vars wiring), `infra/main.bicepparam`; deploy runbook notes | none | 01 (disjoint files) | 1 | medium |
| 04 operational-observability | TBD | `infra/main.bicep` (App Insights + Log Analytics + Key Vault secret + app setting), `infra/README.md`, `api/QuibbleStone.Api.csproj` (package), `api/src/Program.cs` (`AddApplicationInsightsTelemetry` + PII scrubber), `api/src/Hubs/GameHub.cs` (hub exception/disconnect telemetry), light web error beacon | 02 (a deployed env to emit from), infra Key Vault, child-safety/01 | - | 2 | medium |
| 05 anonymous-usage-metrics | TBD | usage custom events in `api/src/Hubs/GameHub.cs` (RoundStarted/complete) + a minimal solo client wrapper (`web/src/`), anonymous device id (reuse `identity.ts`) | 04 (reuses its App Insights pipeline + scrubber), game-modes, single-player/group-play, story-selection/04 (coordinate) | - | 3 | low |

**Concurrency per wave:** Wave 1 = 2 (stories 01 and 02 in parallel). They are disjoint: 01 touches `ci.yml` +
`web/` test config + `web/package.json`; 02 touches `deploy.yml` + `infra/`. No shared file. Both are otherwise
independent of the rest of the slice.

Observability lands later (a deployed environment must exist first): **Wave 2 = 04** (needs 02's reachable env),
**Wave 3 = 05** (reuses 04's App Insights pipeline + PII scrubber). They serialize because 05's usage events ride
the exact pipeline and scrubber 04 stands up - do not build a second telemetry stack. Both touch `GameHub.cs`, so
even if reordered they cannot run truly concurrently without a disjointness check.

## Per-story tech notes

### 01 - Test harness (Vitest + Playwright)
- **Approach:** add **Vitest** in `web/` for pure TS (AC-01) - the engine logic (`template-model` assembly,
  `game-modes` config, `group-play/02` distribution) is the natural target, so prefer extracting pure functions and
  testing those over rendering components. Add **Playwright** at the repo root (or `tests/`) with one smoke test
  that loads the app and asserts it reaches "Connected" (AC-02). Wire the unit-test step into `ci.yml` so a failing
  test fails the run (AC-03), and document the commands (AC-04).
- **Key files it owns:** `web/vitest.config.ts`, a sample test, `playwright.config.ts`, `tests/smoke.spec.ts`,
  `web/package.json` scripts, the `ci.yml` unit step, docs.
- **Exports:** the test commands + config every later feature reuses. No second unit framework (no Jest/RTL) without
  cause (Out of Scope).
- **Gotchas:** Playwright Chromium is pre-installed at `/opt/pw-browsers/chromium` (do not run `playwright install`).
  Multi-player real-time E2E (two contexts) is **not** in this story - it lands with `group-play` on top of this
  harness. Keep coverage targeted at real risk, not a full pyramid (Out of Scope).

### 02 - Deploy to a dev environment
- **Approach:** stand up the dev resource group from the existing Bicep (`az deployment group create`, AC-01), set
  the deploy workflow's required secrets/vars so the API publishes to App Service and the web client to the Static
  Web App (AC-02), and confirm the deployed web client reaches `/health` and opens the hub ("Connected", AC-03).
  The web build must point at the deployed API URL via build-time `VITE_API_BASE_URL` / `VITE_SIGNALR_HUB_URL`, not
  localhost (AC-04).
- **Key files it owns:** `deploy.yml` wiring, `infra/main.bicepparam`, a short deploy runbook.
- **Gotchas:** secrets never go in committed config or `VITE_*` source - they are GitHub secrets / Key Vault. Keep
  the in-process SignalR hub for now (wiring Azure SignalR Service is explicitly Out of Scope - a later
  `.AddAzureSignalR(...)` one-liner). No production hardening (VNet, private endpoints), no custom domains, dev only.

### 04 - Operational observability (Application Insights)
- **Approach:** add workspace-based App Insights (+ the required Log Analytics workspace) to `infra/main.bicep`,
  store its connection string as a Key Vault secret (the first real consumer of the provisioned-but-unused Key
  Vault, CLAUDE.md section 10) and surface it to the API as the `APPLICATIONINSIGHTS_CONNECTION_STRING` app setting.
  Wire `AddApplicationInsightsTelemetry()` in `Program.cs` and register ONE `ITelemetryInitializer`/processor that
  scrubs PII + content as the single choke point. Emit hub exception/disconnect telemetry from `GameHub`. Add a light
  anonymous web error beacon (watch PWA bundle size).
- **Owns / exports:** the operational App Insights pipeline + the PII scrubber that story 05 (and any future
  telemetry) reuses. One or two alerts (exceptions / failed-request spike).
- **Gotchas:** no-PII is an AC, not a nicety - nicknames are free text, join codes and session ids are identifiers,
  words/story text are content: none may ever be sent. No-op cleanly when no connection string is present (local
  dev), and never commit a key or put it in `VITE_*`. Keep the two new resources minimal (README section 9). Out of
  scope: product usage (05), dashboards, Azure SignalR distributed tracing, prod hardening.

### 05 - Anonymous product-usage metrics
- **Approach:** record `RoundStarted` (mode + solo/group) and a completion event with a duration as App Insights
  CUSTOM EVENTS on 04's pipeline - server-side in `GameHub` for group, a minimal client wrapper for solo. Reach is an
  anonymous device/session id (random GUID in `localStorage`, mirroring `identity.ts`) or App Insights' anonymous
  ids - documented as APPROXIMATE (no accounts, README section 3). Fire-and-forget; never blocks a round.
- **Owns / exports:** the usage-event vocabulary (modes played, session length, approximate reach) answerable by a
  plain App Insights query - no dashboard (demand-driven, README section 12).
- **Gotchas:** reuse 04's scrubber (single choke point) so no usage event leaks identity/content (AC-04). Coordinate
  with `story-selection/04`'s serve log - complementary (content curation vs product usage), not a duplicate stack
  (AC-06). No entitlement gate, no player-facing UI. Unique-PERSON counting is explicitly deferred to accounts
  (Phase 2).

## Cross-cutting concerns

- **Observability is no-PII / no-content by construction.** Stories 04-05 add telemetry to an app whose players are
  anonymous minors (README sections 3, 6): a single PII/content scrubber (04) is the choke point, unique-person
  identity is deferred to accounts, and product usage (05) is aggregate + anonymous. One App Insights pipeline, one
  philosophy shared with `story-selection/04`'s serve log - never three parallel telemetry systems.
- **Inter-feature ordering:** both stories are foundation wave and independent of every other feature - schedule
  them with `child-safety/01`, `design-system/01`, and `template-model/01`. The test harness (01) ideally lands
  early so later features can add tests as they build, but no story is hard-blocked on it (CI builds remain green
  without it).
- **Tiny footprint** (README section 9): resist gold-plating. This feature exists to make the rest testable and
  deployable, not to grow infrastructure.
- **No new excluded deps** (no Azure Functions, etc.) - the test tooling is Vitest + Playwright only.
- **No em dashes** in workflow comments, runbooks, or docs.
