# Feature: AI Cost Gate

## Summary
The shared plumbing every AI feature rides behind - built once, reused by all of
them. The hard part of AI in QuibbleStone is not any single feature; it is the
cross-cutting seam that keeps a stranger from running up the bill and keeps
unvetted AI text away from kids. This feature is that seam: a **server-side AI
proxy**, an **entitlement check at session-creation**, **rate-limit + quota**
metering, a **real-time spend circuit-breaker + cost attribution**, and
**moderate-before-display**. It meters **compute per anonymous session, never
identity** - players stay accountless (README section 6). The first and cheapest
consumer that proves the whole gate is the Fresh Runes AI jumble
(`game-modes/07` + `ai-on-demand-generation/05`); the expensive consumers (voices,
illustration, on-demand tales) inherit it unchanged.

## README reference
README section 4 (server-side keys in Key Vault; "one app to start", the Azure
Functions carve-out reserved for async AI jobs), section 6 (child safety - no
unvetted AI text to kids, anonymous players, minimal data on minors), section 3
(monetization as a thin entitlement check at session-creation, not per-request),
section 9 (the 5-resource dev footprint the Bicep grows). Roadmap:
[`docs/ROADMAP.md`](../../ROADMAP.md) "The AI cost gate" and horizon 3 ("The AI
question - explore sooner, gate the cost"). Provider/model decision:
[`docs/adr/0001-ai-provider.md`](../../adr/0001-ai-provider.md). CLAUDE.md
load-bearing rule: "the moment any AI call ships, it goes behind the cost gate."

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #120 | Server-side AI proxy (provider key server-only, browser never calls AI) | Not Started |
| 02 | #121 | Entitlement check at session-creation (reserve `ai.*`, default-unlocked) | Not Started |
| 03 | #122 | Rate-limit + quota metering (per-session/per-IP + "N calls left") | Not Started |
| 04 | #123 | Spend circuit-breaker + cost attribution telemetry | Not Started |
| 05 | #124 | Moderate AI output before display | Not Started |
| 06 | #125 | IaC provisioning seam (Foundry + Content Safety + KV secret + budget/action group) | Not Started |

## Dependencies
- `billing-entitlements/01` (issue #70) - the entitlement seam story 02 consumes
  (default-unlocked; the `ai.*` capability keys already reserved in its catalog).
  In alpha the jumble does not require the entitlement (see Decisions); the seam
  is still wired so charging attaches later.
- `platform-devops/04` (issue #106) - the Application Insights pipeline +
  `PiiScrubbingTelemetryInitializer` choke point story 04's attribution event
  emits through; and `05` (issue #107) for the `UsageTelemetry` property/metric
  builder pattern to mirror.
- `child-safety/01` (profanity filter) + `child-safety/02` (family-safe toggle) -
  the existing `IContentSafetyFilter.CheckAsync` seam (async by design for exactly
  this) + `FamilySafeContentSelector` that story 05 routes AI output through.
- `session-engine` - the room/solo session-creation call site story 02 hooks
  (`GameHub.CreateRoom`, the solo entry point).
- `infra` - the already-provisioned Key Vault + Table Storage (unused today) that
  stories 01/04/06 finally consume; App Insights is already wired.
- `ai-on-demand-generation/05` + `game-modes/07` - the first CONSUMERS of the gate
  (not dependencies of it); the gate must exist first.

## Design notes
- **The gate is plumbing, not a feature players see.** Its only player-visible
  surfaces are the "N Fresh Runes left" meter (story 03) and the graceful
  degrade-to-fallback when the breaker trips (story 04). Everything else is
  server-side and invisible. New AI features are **consumers** of this seam; if a
  future AI story invents its own proxy, its own quota, or its own moderation
  path, that is a smell to flag - it belongs behind this gate.
- **Two enforcement layers, one attribution stream (the load-bearing shape).**
  - **Layer 1, real-time (story 04):** the server proxy estimates $ per call from
    the response token usage x the model rate, adds it to a running monthly total
    persisted in Table Storage, and at 100% of the $20 ceiling stops calling AI so
    every AI feature degrades to its deterministic fallback. This is what actually
    enforces the ceiling, because Azure billing data lags hours.
  - **Layer 2, backstop (story 06):** an Azure Cost Management $20 budget with
    alerts at 25/50/75/100% wired to an action group emailing the owner. It is the
    authoritative spend source and catches everything (AI + infra), but it is slow.
    The two are reconciled periodically - fast-but-estimated against
    authoritative-but-slow.
- **Attribution from day one, even with one feature (story 04).** Every AI call
  emits ONE App Insights telemetry event carrying a **feature tag** (`jumble` /
  future `verdict` / `onDemand`), the model, token counts, estimated cost, and the
  **anonymous** session/room id (`Room.InstanceId`). The feature dimension ships
  now even though only the jumble exists - retrofitting a cost dimension later is
  painful; adding it now is free. From it: a per-feature cost breakdown and a
  per-session distribution.
- **Surface a hot session, without identity (story 04).** If one anonymous session
  or room's spend is disproportionate, make it visible in telemetry as an
  abuse/concentration signal, in addition to the per-session quota that rate-limits
  it. Distribution is measured over anonymous session/room ids ONLY - never
  identity, never PII. "A subset of players" means "a subset of anonymous
  sessions." The gate meters compute per session, not identity.
- **Anonymous by construction (README section 6).** Metering keys off the opaque
  per-room `InstanceId` (already used by the serve log) and/or a per-IP bucket for
  abuse control - never a nickname, join code, or any account. Minors stay
  accountless.
- **Entitlement is decided ONCE, at session-creation (story 02).** It answers
  "unlocked / not" for a capability key; it is never a per-tap or per-call check
  (that stays a smell per `billing-entitlements`). Metering ("how many left",
  story 03) is a SEPARATE concern from entitlement - the two must not be conflated.
- **Server-side only (story 01).** The provider key lives in Key Vault (or the App
  Service managed identity via RBAC); the browser never calls AI directly, so every
  call is ours to see, meter, and throttle. Registered with the config-presence /
  no-op branch pattern already in `Program.cs` (absent config = the gate simply has
  no AI to offer, and every consumer uses its deterministic fallback).
- **Moderate-before-display is non-negotiable (story 05, README section 6).** AI
  output is unvetted text; it passes the existing safety filter + family-safe gate
  before any child sees it. Azure AI Content Safety is a config-gated optional
  second layer, turned on for the larger free-text payloads later (see ADR 0001,
  decision B).
- **In-app, not Functions, for this slice (ADR 0001, decision D).** The jumble is a
  sub-second request/response; the in-app server proxy suffices. Azure Functions
  stays parked (CLAUDE.md) until a genuinely async AI job (images, TTS) or Stripe
  webhooks appears.

## Parked - beyond this slice
- **Real charging / Stripe** (`billing-entitlements` 02-05) - explicitly deferred.
  The gate meters compute now; real charging attaches to the same entitlement seam
  later without a refactor. Do not pull it into this slice.
- **Azure AI Content Safety as an always-on layer** - wired behind a config flag
  now (story 05), turned on when the payloads grow to whole templates / prompts
  (`ai-on-demand-generation/01-02` proper).
- **Per-feature budgets** (a separate ceiling per AI feature) - the attribution
  data (story 04) makes this possible later; the first cut is one shared $20
  ceiling across all AI.
- **Moving the proxy to Azure Functions** - revisited only when an async AI job
  justifies it (ADR 0001, decision D).
- **A spend dashboard / workbook** - the attribution events are queryable in App
  Insights without one (demand-driven, README section 12).

## Decisions
- 2026-07-02: Filed as a NET-NEW shared feature (not folded into
  `ai-on-demand-generation` or `billing-entitlements`) because the gate is
  cross-cutting plumbing every AI feature reuses - giving it its own folder keeps
  "consumers, not new gates" honest. Scope, model, and the four open questions were
  resolved by the Phase 0 research spike, recorded in
  [`docs/adr/0001-ai-provider.md`](../../adr/0001-ai-provider.md).
- 2026-07-02: **Model = gpt-4o-mini** (owner's pick for brand-voice headroom; a
  swappable config value, nano is the cheaper fallback). **Moderation = existing
  filter now, Content Safety later** (behind a config flag). **Runtime = in-app
  server proxy** (Functions stays parked). See ADR 0001.
- 2026-07-02: **Free-tier shape = the AI jumble is free for everyone in alpha**,
  gated only by rate-limit/quota (story 03) + the spend circuit-breaker (story 04),
  NOT by entitlement. Story 02 still builds the entitlement seam and reserves the
  `ai.*` key (default-unlocked) so real charging attaches later, but in alpha the
  check evaluates unlocked for the jumble and does not block it. This maximizes
  signal on whether players like the feature and deliberately leans on the
  circuit-breaker as the true cost control - which is the point of building it.
- 2026-07-02: The IaC provisioning seam is its own story (06) rather than being
  split across 01/04/05, so a single owner touches `infra/main.bicep` (avoids the
  merge collision two Bicep-editing stories would cause) and the "I prep the Bicep,
  you run the Azure provisioning" hand-off is one clean unit.
