<!--
  Implementation plan for on-demand AI generation - SCOPED to the pulled-forward slice (stories 02 + 05,
  the AI word-bank jumble and its moderation). Stories 01/03/04 remain sketch and are not planned here;
  they get their own waves when Phase 4 proper is decomposed. Bridges the buildable stories to
  orchestration. The gate itself is planned in ai-cost-gate/implementation.md. No em dashes.
-->

# Implementation Plan: On-Demand AI Generation (jumble slice only)

> The bridge between planning and orchestration for the two pulled-forward stories (02 + 05). The whole
> feature still ships last overall (README Phase 4); only its lightest payload leads, to prove the shared
> [`ai-cost-gate`](../ai-cost-gate/feature.md). The gate is a hard prerequisite - its
> [`implementation.md`](../ai-cost-gate/implementation.md) carries the cross-feature master DAG. Provider/
> model: [`docs/adr/0001-ai-provider.md`](../../adr/0001-ai-provider.md).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Server AI call (never from the browser) | the gate's proxy | `ai-cost-gate/01` -> `api/src/Ai/IAiCompletionClient` |
| Rate-limit / quota / meter | the gate's quota | `ai-cost-gate/03` -> `api/src/Ai/IAiQuota` |
| Spend breaker + attribution telemetry | the gate's breaker | `ai-cost-gate/04` -> `api/src/Ai/AiCost*`, `AiCostTelemetry` |
| Moderate-before-display (the hard gate) | the gate's moderation seam | `ai-cost-gate/05` -> composes `IContentSafetyFilter` + family-safe |
| Entitlement (captured at session-creation, alpha-unlocked) | the gate's entitlement capture | `ai-cost-gate/02` -> `IEntitlementService` |
| Family-safe rule | `FamilySafeContentSelector` / `isFamilySafe` | `api/src/Content/`, `web/src/content/familySafe.ts` |
| Category / word model the output conforms to | `WordBankEntry` / `Template.wordBank` | `template-model` |
| Consuming UX + the free fallback the AI degrades to | the "Fresh runes" button + deterministic reshuffle | `game-modes/07` -> `web/src/content/wordBankJumble.ts`, `WordBankAnswer.tsx` |
| Wire contract (no codegen) | the hand-mirrored jumble result DTO / hub method | `api/src/Hubs/GameHub.cs`, `web/src/signalr/useGameHub.ts` |

## Wave Plan (DAG) - within this slice

Both stories depend on the whole gate existing. Story 05 (generation) and story 02 (its moderation
policy) are closely coupled but file-disjoint enough to build together if the gate is done: 05 owns the
generation/parse/prompt; 02 owns the moderation-policy composition + audit sampling. They share the
result path, so the orchestrator may hand both to one builder or serialize 02 just after 05.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 05 AI word-bank jumble (generation) | #126 | `api/src/Ai/Jumble/*` (prompt build, call, parse), the jumble result DTO field | the whole `ai-cost-gate` (01-05), `game-modes/07` free layer, `template-model` | 02 | (after gate) | high |
| 02 live moderation gate (policy) | #127 | the moderation-policy composition + anonymous audit sampling | `ai-cost-gate/05`, `child-safety/01+02`, 05 | 05 | (after gate) | medium |

**Concurrency:** this slice is a single small wave AFTER the gate lands: {05, 02}, coordinated on the
shared result path (one builder, or 02 serialized right after 05). See the cross-feature DAG below for
where it sits relative to the gate and the free jumble.

## Per-story tech notes

### 05 - AI word-bank jumble (generation)
Build the small prompt (brand-voice system + category + avoid-list), call `IAiCompletionClient`
(`ai-cost-gate/01`) inside the gate pipeline (quota -> breaker -> call -> estimate/emit), parse the reply
defensively (JSON array of words; any failure = unavailable -> fallback), return the moderated set +
remaining-quota count on the jumble DTO. Reuses the gate end to end; no direct Foundry call, no own
filter. Gotcha: keep `maxOutputTokens` tiny (this is the ~$0.0001/call payload the gate is proved on);
never throw into gameplay.

### 02 - Live moderation gate (policy)
Compose `ai-cost-gate/05`'s moderate-before-display seam over the generated words; decide "enough safe?"
and drive the graceful fallback + one warm refusal message (no which/why leak); sample rejections
anonymously for audit (scrubbed telemetry, no PII/content). Shape the policy method so story 01
(whole-template) can extend it to prompt + template later without a fork. Gotcha: this is policy over the
gate's seam, not a second filter.

## Cross-cutting concerns
- **Consume the gate, never fork it.** No direct AI call, no parallel quota/breaker/filter. If a fork is
  tempting, the gate seam is missing something - fix the gate.
- **Child safety is the point (README section 6).** No AI word is shown before moderation; family-safe
  tightens it; rejections teach no evasion; audit sampling is anonymous.
- **Fallback is always the free reshuffle** (`game-modes/07`) - AI unavailable/quota/breaker/bad-reply
  all land there, no error, no charge.
- **Anonymous, no PII** in any telemetry (attribution + audit) - rides the existing PII scrubber.
- **No Functions, no new excluded deps** (the gate owns the SDK refs). No i18n; no em dashes.

## Cross-feature build order

This slice (D) is the LAST phase of the first-AI-slice DAG; the authoritative table lives in
[`ai-cost-gate/implementation.md`](../ai-cost-gate/implementation.md) ("Cross-feature master DAG"):
**A** `game-modes/07` free reshuffle (ships first, no AI) -> **B** `ai-cost-gate/01`+`/06` (proxy + IaC)
-> **C** `ai-cost-gate/02-05` (entitlement, quota, breaker, moderation) -> **D** this slice
(`ai-on-demand-generation/05` + `/02`, the AI generation behind the full gate, degrading to A).
