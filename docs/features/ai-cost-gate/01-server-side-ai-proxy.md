<!--
  Story 01 of the AI cost gate - the foundation every other gate story and every AI consumer imports.
  Buildable from this file + implementation.md's per-story note + the reuse map. No em dashes.
-->

# Story: Server-side AI proxy (the browser never calls AI)

**Feature:** AI Cost Gate  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #120

## Context
This is the foundation of the whole gate (feature.md; ROADMAP "The AI cost gate"
piece 1): a single server-side seam through which every AI call in the product
flows. The provider key lives in Key Vault (or the App Service managed identity);
the browser never calls the AI provider directly, so every call is ours to see,
meter (stories 03/04), and moderate (story 05). It wraps Azure AI Foundry (Azure
OpenAI, model `gpt-5-mini` - see [ADR 0001](../../adr/0001-ai-provider.md), whose
original `gpt-4o-mini` pick was superseded by availability at deploy time), but the
seam is provider-agnostic so the model/provider is a swappable config value. The
first caller is the Fresh Runes jumble (`ai-on-demand-generation/05`), but this
story ships the generic proxy, not the jumble. See
[feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given the API, when it needs AI output, then it calls a single
      server-side seam (a new `api/src/Ai/` service, e.g. `IAiCompletionClient`)
      and NOTHING in `web/` ever holds a provider key or calls the AI provider
      directly - verifiable by grep: no AI SDK, endpoint, or key in `web/`, and no
      AI provider key in any `VITE_*` var (secrets ship to the browser, forbidden
      per CLAUDE.md section 4).
- [ ] AC-02: Given the proxy, when it is invoked, then it returns both the
      generated text AND the call's token usage (input + output token counts) and
      the model id, so the cost circuit-breaker (story 04) can estimate $ per call
      from the response - the usage is surfaced on the proxy's own return type, not
      buried in the SDK response.
- [ ] AC-03: Given the provider key/endpoint, then they come from configuration
      (an App Service app setting sourced from Key Vault, or the managed-identity
      RBAC path) - NEVER committed to source, NEVER a `VITE_*` var. Local dev reads
      them from user-secrets/env only.
- [ ] AC-04 (no-op when unconfigured): Given no AI configuration is present (local
      dev, or before provisioning), when the proxy is resolved, then it registers a
      no-op/unavailable implementation that reports "AI unavailable" cleanly - the
      app builds and runs with zero AI config, and every consumer falls back to its
      deterministic path (mirrors the existing `ITelemetrySink` /
      `AddApplicationInsightsTelemetry` config-presence pattern in `Program.cs`).
- [ ] AC-05 (async, in-app): Given the call, then it is fully async
      (`Task`-returning, no blocking `.Result`/`.Wait()`) and runs in-process in the
      existing ASP.NET Core app - NO Azure Functions project is added (ADR 0001
      decision D; CLAUDE.md parks Functions). A `CancellationToken` is honored so a
      dropped client or a shed round does not leak a call.
- [ ] AC-06 (resilience): Given a provider timeout, error, or rate-limit response,
      then the proxy fails soft - it surfaces a typed "unavailable" result (never an
      unhandled exception into gameplay), applies a sane per-call timeout, and does
      NOT auto-retry in a way that could amplify spend (at most one bounded retry;
      the circuit-breaker in story 04 is the real spend guard).
- [ ] AC-07 (generic, not jumble-specific): Given this story, then the proxy
      exposes a general "complete this prompt, family-safe system instruction, max
      output tokens" shape reusable by future AI features (verdict, on-demand
      tales, packs) - it does NOT bake in word-bank/jumble-specific logic (that
      lives in `ai-on-demand-generation/05`). If jumble specifics leak into the
      proxy, that is a smell to flag.

## Out of Scope
- The jumble prompt/parsing and the word-bank UX - `ai-on-demand-generation/05` and
  `game-modes/07`.
- Rate-limit/quota (story 03), the spend circuit-breaker + attribution (story 04),
  and moderation of the output (story 05) - this story is the transport only; it
  does NOT decide whether a call is allowed or whether output is safe. (It exposes
  the token usage those stories consume.)
- The Bicep to provision the Foundry resource + Key Vault secret - story 06 (this
  story consumes the resulting config; it does not author the infra).
- Streaming responses, function-calling/tools, and multi-turn conversation state -
  not needed for the small jumble payload; add later if a feature needs them.
- Azure AI Content Safety wiring - story 05 (optional, config-gated).

## Technical Notes
- **New `api/src/Ai/` folder** (mirrors `Rooms/`, `Safety/`, `Telemetry/`):
  `IAiCompletionClient` with an async method shaped like
  `Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct)`,
  where `AiCompletionResult` carries `Text`, `InputTokens`, `OutputTokens`,
  `ModelId`, and an `IsAvailable`/status flag (AC-02, AC-06). Register in
  `Program.cs` with the config-presence branch: if `Ai:Endpoint` (+ key or managed
  identity) is present, register the Foundry-backed client; else register
  `NoOpAiCompletionClient` (AC-04) - exactly the `ITelemetrySink` idiom at
  `Program.cs` ~line 123.
- **SDK (validated in the Phase 0 spike, builds on net10.0):** `Azure.AI.OpenAI`
  (companion to `OpenAI`) + `Azure.Identity`. Shape:
  `new AzureOpenAIClient(endpoint, credential).GetChatClient(deployment).CompleteChatAsync(messages, options)`;
  read `completion.Value.Content[0].Text` and `completion.Value.Usage.InputTokenCount` /
  `OutputTokenCount`. Prefer `DefaultAzureCredential` (managed identity) over an API
  key where possible; the Key Vault-stored key mirrors the existing
  `APPLICATIONINSIGHTS_CONNECTION_STRING` KV-reference app-setting.
- **Config keys:** `Ai:Endpoint`, `Ai:Deployment` (e.g. `gpt-5-mini`), optional
  `Ai:ApiKey` (KV-backed), plus the per-model rate constants (input/output $/1M)
  story 04 reads. Keep the model id + rates together so a model swap is one config
  change (ADR 0001).
- **Verbose header comment** on `IAiCompletionClient.cs` and the Foundry
  implementation (CLAUDE.md section 4): what it is, why it is server-side, the
  no-op contract, and that consumers must go through the gate (03/04/05), never
  around it.
- No new dep beyond `Azure.AI.OpenAI` + `Azure.Identity` (both are Azure-native,
  not on the deliberately-excluded list). Async all the way; nullable respected.

## Tests
| AC | Test |
|---|---|
| AC-01 | code review + grep: no AI SDK/endpoint/key anywhere in `web/`; no AI key in any `VITE_*` var |
| AC-02 | `api/tests/Ai/AiCompletionResultTests.cs` (or the mockable client): the result exposes input/output token counts + model id |
| AC-03 | code review: key/endpoint read from config only; grep for any committed key; nothing in `VITE_*` |
| AC-04 | `api/tests`: with no `Ai:*` config, DI resolves the no-op client and `IsAvailable` is false; app boots clean |
| AC-05 | code review: method is async, no `.Result`/`.Wait()`; no Functions project added; `CancellationToken` threaded |
| AC-06 | `api/tests`: a simulated provider timeout/error yields a typed unavailable result, not an exception; at most one bounded retry |
| AC-07 | code review: the proxy signature is generic (prompt/system/maxTokens), no jumble/word-bank specifics |

## Dependencies
- `infra` / cost-gate/06 (the Foundry endpoint + Key Vault secret this consumes;
  06 preps the Bicep - but this story builds and no-ops without it, AC-04, so it
  does not hard-block on provisioning).
- `platform-devops/04` (the `Program.cs` config-presence pattern + DI conventions
  to mirror).
