<!--
  ADR 0001 - AI provider + model for the first AI slice, and the shape of the AI cost gate.
  This is the committed output of the Phase 0 research spike for the "Fresh Runes AI jumble"
  slice (docs/ROADMAP.md "The AI cost gate"). It records findings + a recommendation; the
  open product decisions it surfaces are resolved in the "Decision" section once the owner has
  made the calls. Establishes the docs/adr/ convention for durable architectural decisions.
  Use hyphens/colons/parentheses, never em dashes.
-->

# ADR 0001: AI provider, model, and the AI cost gate shape

- **Status:** Accepted (spike complete; owner resolved the four open decisions on 2026-07-02 - see Decision)
- **Date:** 2026-07-02
- **Context feature:** `ai-on-demand-generation` (the jumble backend) + a new shared `ai-cost-gate` feature
- **Supersedes / superseded by:** none

## Context

The roadmap's next AI step is deliberately the cheapest, safest payload: the **Fresh Runes AI
jumble** - a Word Bank player taps "Fresh runes" and gets a fresh set of ~6-10 short,
family-safe, on-theme words for one blank's category (`game-modes/07`, backed by
`ai-on-demand-generation/05`). The moment any AI call ships it must ride behind the **AI cost
gate** (CLAUDE.md load-bearing rule; ROADMAP "The AI cost gate"): server-side proxy +
entitlement-at-session-start + rate-limit/quota + a spend circuit-breaker + moderate-before-display.

This ADR is the committed result of the Phase 0 research spike. The preferred path was **Azure AI
Foundry** (stay in the Azure ecosystem: Key Vault + Storage are already provisioned in `infra/` and
currently unused). The spike treated that as a hypothesis to validate, not a foregone conclusion.

**Spike method + honest limits.** This was a timeboxed, read-and-validate spike. Pricing and SDK
facts below are from current Azure documentation (July 2026) and a compile-validated .NET spike
(see point 3). It was **not** possible to make a live Foundry call from the planning sandbox (no
Azure subscription / deployment / credentials here), so the per-call token counts are estimates
from prompt shape, not measured. The first build story includes a "measure one real call and
confirm the estimate" step. Per the task, the deliverable of the spike is this written
recommendation.

## Findings (the seven spike questions)

### 1. Model + cost for the jumble payload

The jumble is a tiny text call: a short system prompt (brand voice + family-safe rules), a category,
and a short exclusion list; the output is 8-10 short single words as a JSON array. This favors the
cheapest small model that can hold a brand voice and a safety instruction.

| Model (Foundry) | Input $/1M | Output $/1M | Notes |
|---|---|---|---|
| gpt-4.1-nano (spike default) | $0.10 | $0.40 | Cheapest 4.1-family; built for high-volume simple tasks (classify/tag/generate short lists); text. The swap-to target if 4o-mini is overkill. |
| **gpt-4o-mini** (chosen - see Decision) | $0.15 | $0.60 | Slightly stronger/pricier; owner's pick for brand-voice headroom. Per-call cost still negligible. |
| Phi-4-mini (MaaS serverless) | ~$0.07 | ~$0.23 | Cheapest overall, but an open small model - likely needs more prompt tuning to hold the stone-carving brand voice. |

**Per-call estimate (gpt-4.1-nano):** ~400 input tokens (system + category + exclusions) x $0.10/1M
= ~$0.00004; ~60 output tokens x $0.40/1M = ~$0.000024. **~$0.0001 per jumble** (roughly a
hundredth of a cent).

**Rough monthly ceiling at alpha volume:**
- Realistic alpha (~50 sessions/mo x ~20 jumbles = ~1,000 calls/mo): **~$0.06/mo** on the LLM.
- Stress (10,000 calls/mo): **~$0.64/mo** on the LLM.
- To spend the whole $20/mo on the LLM alone you would need **~300,000 jumbles/mo**.

**Consequence:** at the jumble's payload size, the LLM is almost free. The $20/mo ceiling is not
there to cap organic alpha play - it is a **backstop against a bug or abuse** (a runaway retry loop,
a scraper hammering the endpoint) and against the more expensive AI payloads that reuse this gate
later (whole templates, images, voices). Frame the circuit-breaker that way.

### 2. Moderation (how AI output is vetted before any child sees it)

Two options, and they are complementary, not exclusive:

- **The existing server-side filter** (`api/src/Safety/IContentSafetyFilter`): deterministic
  blocklist + normalization, plus the separate `FamilySafeContentSelector` family-safe gate. Free,
  instant, already built, and - critically - `CheckAsync` was made **async from day one specifically
  so a remote/AI moderation check is a drop-in**. For single common dictionary words (exactly the
  jumble's output), a blocklist + family-safe wordlist handles the risk well.
- **Azure AI Content Safety**: contextual ML moderation (hate/violence/sexual/self-harm severities).
  $0.38 per 1,000 text records (S0; a record is up to 1,000 chars), and a **free F0 tier of 5,000
  text records/month**. For a bag of 10 short words (well under 1,000 chars = 1 record/call), alpha
  volume sits inside the free tier or costs a few dollars at stress. Its real value shows up on the
  **larger free-text payloads** (whole generated templates in `ai-on-demand-generation/01-02`), not
  on single words.

**Recommendation:** the existing filter is the **enforced hard gate** on every AI-sourced word
(non-negotiable, README section 6) - no AI word is displayed or made tappable until it passes the
filter AND the family-safe gate. Wire **Azure AI Content Safety behind a config-presence flag** as
an optional second layer (the same no-op-when-absent pattern the API already uses), turned on for
the bigger payloads later. This keeps the cheapest slice cheapest while establishing the seam. (See
open decision B.)

### 3. .NET integration from the net10.0 API

- **Package:** `Azure.AI.OpenAI` (companion to the official `OpenAI` .NET library) + `Azure.Identity`
  for managed-identity auth. Microsoft's newer guidance points at a Foundry-native `FoundryChatClient`
  for Foundry projects, but `Azure.AI.OpenAI` works against both classic Azure OpenAI and Foundry
  deployments and is the stable, well-documented path.
- **Validated shape (compile-checked spike, `Azure.AI.OpenAI 2.9.0-beta.1` on net10.0, builds clean):**
  `new AzureOpenAIClient(endpoint, credential).GetChatClient(deployment).CompleteChat(messages, options)`,
  reading `completion.Content[0].Text` and - load-bearing for the cost gate - `completion.Usage.InputTokenCount`
  / `completion.Usage.OutputTokenCount`. **Token usage is on the response**, so we can estimate $/call
  deterministically (point 5).
- **Latency + sync/async shape:** the jumble is a sub-second, small request/response. A **synchronous
  request/response inside the in-app server proxy suffices**; show a brief "carving fresh words..."
  state and never block the round. This does **not** justify standing up Azure Functions. CLAUDE.md
  parks Functions and names "async AI jobs" as the reason to revisit - the jumble is not one. Revisit
  Functions only for genuinely long-running/async AI (image generation, TTS) or Stripe webhooks. (See
  open decision D.)
- **Auth:** prefer the App Service system-assigned managed identity (already present in Bicep) against
  Foundry via RBAC; a Key Vault-stored API key is the simpler fallback and matches the existing
  `APPLICATIONINSIGHTS_CONNECTION_STRING` Key Vault-reference app-setting pattern. Either way the key
  never reaches the browser and never lives in a `VITE_*` var.

### 4. Quota / rate controls: Foundry-native vs. ours

- **Foundry-native:** per-deployment tokens-per-minute (TPM) and requests-per-minute quotas set at
  deploy time. These protect the *deployment* from overload; they are coarse and per-deployment, not
  per-anonymous-session, and they do **not** enforce a monthly dollar ceiling or per-session fairness.
- **Ours (must build):** the per-session quota ("N Fresh Runes left"), the per-IP rate limit, and the
  monthly spend circuit-breaker. Treat Foundry's TPM quota as a concurrency backstop, set sanely, not
  as the business control.

### 5. Cost estimation per call (feeds the real-time circuit-breaker)

From the response `Usage`: `estUsd = (InputTokenCount * inputRate + OutputTokenCount * outputRate) / 1e6`,
where the per-model rates are a small config constant for the deployed model (e.g. nano: 0.10 / 0.40).
Add `estUsd` to a **running monthly total persisted in the already-provisioned Table Storage** (the
same account the serve-log/telemetry sink already uses; `Azure.Data.Tables` is already referenced),
keyed by UTC month. This estimate is the fast enforcer; Azure billing (point 6) is the slow
authority, and the two are reconciled periodically.

### 6. Azure Cost Management budget (backstop + notify)

- **Bicep:** `Microsoft.Consumption/budgets` (resource-group scope), `amount: 20`, `timeGrain: Monthly`,
  with `notifications` at **25 / 50 / 75 / 100%** (`thresholdType: Actual`, plus optionally a
  `Forecasted` 100%), wired to a `Microsoft.Insights/actionGroups` with an **email receiver**. The
  email is a **Bicep parameter / deploy input, never hardcoded** into committed markdown or source.
- **How fast it fires:** budget evaluation is driven by billing data, which **lags hours (typically
  evaluated every 8-24h)**. This is why it is the authoritative-but-slow backstop that catches
  *everything* (AI + infra), and why the app circuit-breaker (point 5) is the real-time enforcer - we
  cannot wait for billing to stop a runaway.
- **Faster pre-billing warning:** we can *also* emit an App Insights metric alert on the app's running
  estimate (point 5) at the same thresholds, for warning hours-to-days before the billing budget sees
  it. Recommended as a light add; the authoritative notifications remain the budget action group.

### 7. Go / no-go

**GO on Azure AI Foundry + gpt-4.1-nano.** Rationale: stays in the Azure ecosystem the infra is
already built for (Key Vault + Storage + managed identity), the .NET SDK path is validated and stable,
and the per-call cost is negligible. Alternatives noted and not chosen now: **gpt-4o-mini** (the
in-ecosystem fallback if nano quality disappoints), **Phi-4-mini** (cheapest, but more prompt work),
and **a direct non-Azure provider** (e.g. an Anthropic Haiku-class model via the `claude-api` skill) -
rejected for this slice only to keep the first AI call inside the existing Azure security/identity
footprint, not on quality grounds; it stays available if we ever leave Azure.

## The cost gate this ADR commits to (shape only; stories decompose it)

Built once, reused by every later AI feature. Five pieces:

1. **Server-side proxy** - the provider key in Key Vault (or managed identity); the browser never
   calls AI. Registered with the config-presence/no-op branch pattern already in `Program.cs`.
2. **Entitlement at session-creation** - one check when a room/solo session is minted, consuming the
   `billing-entitlements/01` seam (issue #70, default-unlocked). Meters **compute per session, never
   identity** - players stay anonymous.
3. **Rate-limit + quota** - per-session and per-IP limits + an "N calls left" meter, distinct from the
   entitlement gate (entitlement answers unlocked/not; metering answers how-many-left).
4. **Real-time circuit-breaker + attribution telemetry** - the running monthly estimate in Table
   Storage stops AI at 100% and degrades to the deterministic fallback; every AI call emits ONE App
   Insights telemetry event carrying a **feature tag** (jumble / [future] verdict / on-demand), model,
   token counts, estimated cost, and the **anonymous** session/room id (`Room.InstanceId`), flowing
   through the existing `PiiScrubbingTelemetryInitializer` choke point. The feature dimension ships
   from day one even though only the jumble exists (retrofitting a cost dimension later is painful).
5. **Moderate-before-display** - AI output passes the existing safety filter + family-safe gate before
   any child sees it (point 2), with Azure AI Content Safety as the optional second layer.

**Explicitly deferred:** Stripe / real charging (`billing-entitlements` 02-05). The gate meters
compute now; real charging attaches to the same seam later.

## Open decisions (surfaced to the owner before the plan is finalized)

- **A - Model:** gpt-4.1-nano (recommended) vs. gpt-4o-mini vs. Phi-4-mini.
- **B - Moderation:** existing filter as the hard gate + Content Safety as an optional later layer
  (recommended) vs. both from day one vs. existing-filter-only.
- **C - Free-tier shape:** free/unentitled sessions get the deterministic reshuffle only, AI jumble
  fully behind entitlement (recommended, simplest cost story) vs. a capped free AI "taste" (N free AI
  jumbles/session) vs. AI jumble free for everyone in alpha.
- **D - Async vs in-app:** in-app synchronous server proxy for the jumble (recommended) vs. stand up
  Azure Functions now.

## Decision

Resolved by the owner on 2026-07-02:

- **A - Model: gpt-4o-mini.** The owner leaned to 4o-mini for a little more brand-voice headroom.
  Per-call cost is negligible either way (~$0.0001-0.00015), so the small premium buys quality
  insurance. The model is a **config value, swappable to nano** if 4o-mini proves to be overkill.
- **B - Moderation: existing filter now, Azure AI Content Safety later.** The existing blocklist +
  family-safe gate is the enforced hard gate on every AI word; Content Safety is wired behind a
  config-presence flag and turned on for the larger free-text payloads (whole templates) later.
- **C - Free-tier shape: AI jumble is free for everyone in alpha.** It is gated **only by rate-limit /
  quota (piece 3) + the spend circuit-breaker (piece 4), not by entitlement**. The entitlement seam
  (piece 2) is still built and the `ai.*` capability key still reserved - so real charging attaches to
  the same seam later without a refactor - but in alpha the entitlement check evaluates
  default-unlocked for the jumble and does not block it. This maximizes signal on whether players like
  the feature and deliberately leans on the circuit-breaker as the true cost control (which is the
  point of building it).
- **D - Runtime: in-app synchronous server proxy.** Azure Functions stays parked (CLAUDE.md) until a
  genuinely async AI job (image generation, TTS) or Stripe webhooks appears.

### Voice narration cost note (context for why the gate matters, not a decision here)

The owner asked, while weighing the model, what one voice reading of a story would cost. Voice is a
**separate cost center** from the jumble: Azure AI Speech TTS is billed **per character synthesized**,
not per chat token, so the jumble model choice is independent of it. Current rates (July 2026):
**Standard Neural $16/1M chars, Neural HD $22/1M chars, free tier 500K chars/month**. A finished
QuibbleStone tale is ~350-900 characters, so **one reading is ~$0.006-$0.02** - roughly **50-200x the
cost of a jumble** (~$0.0001). At ~2,000 readings/month, voice alone approaches the whole $20 ceiling.
This is exactly why the cost gate is built now on the cheap jumble payload: voice (when
`ai-voice-narration` is decomposed) will be the real stress test, and it must inherit a proven gate,
not invent one. `ai-voice-narration` will make its own TTS engine/voice-tier decision in its own ADR.

## Consequences

- A new shared `ai-cost-gate` feature folder becomes the home of the five-piece plumbing every future
  AI feature reuses; new AI features are consumers, not new gates.
- `infra/main.bicep` grows a Foundry (Azure OpenAI) resource + deployment, an optional Content Safety
  resource, a Key Vault secret (or managed-identity RBAC), and the Cost Management budget + action
  group. Provisioning is the owner's to run ("I prep the Bicep, you run the Azure provisioning").
- The first real AI call is metered from the very first commit; there is no ungated AI in the tree.
