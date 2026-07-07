<!--
  Story 05 of on-demand AI generation - the AI word-bank jumble: the FIRST, cheapest live-generation
  payload, decomposed from the feature.md sketch into a buildable story. Rides the ai-cost-gate.
  Buildable from this file + implementation.md + the reuse map. No em dashes.
-->

# Story: Generate word-bank options on demand (the AI jumble backend)

**Feature:** On-Demand AI Generation  ·  **Status:** Complete  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #126

> **Shipped 2026-07-03.** `api/src/Ai/Jumble/JumbleWordGenerator.cs` is the first real
> consumer of the gate: it builds the tiny prompt (brand voice + family-safe + category +
> avoid-list), routes ONE call through `GatedAiCompletionClient.CompleteGatedAsync(feature=
> "jumble")` (never a raw provider call), and shapes the gate's already-moderated, per-line
> output into a deduped single-word set - falling back (FellBack=true) on quota/breaker/
> unavailable/too-few. It instructs the model to emit one word per line so the gate's generic
> newline split moderates each candidate INDIVIDUALLY through the same seam (no parallel
> filter - AC-07). Exposed at `POST /api/ai/jumble` (`AiJumbleController`, per-IP rate-limited,
> anonymous InstanceId/session key). Covered by `JumbleWordGeneratorTests` (10 tests). The
> throwaway probe was removed now that this real consumer + its attribution telemetry supersede it.

> **Follow-ups (2026-07-03, post-ship).** Two gpt-5-mini transport fixes were needed
> before the gated call actually returned words on UAT - the #140 "prove it" step
> surfaced it silently falling back on every call (AC-06 held, so gameplay was fine, but
> no AI word ever reached a player): (1) send `max_completion_tokens`, not the legacy
> `max_tokens`, which gpt-5-mini rejects with a 400 (PR #146); (2) pin
> `reasoning_effort=minimal` (PR #148) - at the default effort the reasoning model spent
> the ENTIRE output-token budget on hidden reasoning and returned empty content. Then
> VERIFIED live: real on-theme words + a `feature=jumble` attribution event in App
> Insights (AC-05). PR #149 enriched the prompt on top (still AC-01/02): a SOFT theme
> steer from the template's curated `tags.themes` (the model never sees the story text,
> so no spoiled reveal; tags sanitized server-side as untrusted input), a cheeky PG-13
> grown-up voice when family-safe is OFF (see `/02` - still bounded by the always-on
> profanity/slur filter), and the named avoid-list raised 20 -> 40.

## Context
The lightest, cheapest, safest live-generation payload in the whole product, and the
deliberate proving ground for the AI cost gate (feature.md sketch story 05; ROADMAP
horizon 3). When a Word Bank player taps "Fresh runes" and wants AI-fresh options,
this story generates a small set (~8-10) of fresh, on-theme, family-safe WORDS for
one blank's category via an AI call - NOT a whole template. It rides the shared
`ai-cost-gate` (server proxy + quota + breaker + moderation), consumes ADR 0001's
model (Azure AI Foundry, gpt-5-mini - the ADR picked gpt-4o-mini, but it was
deprecated by deploy time, so gpt-5-mini is the deployed model per PR #131), and
falls back to `game-modes/07`'s free
deterministic reshuffle whenever AI is unavailable. It reuses the SAME generate +
moderate pipeline the later whole-template on-demand generation (story 01) will use -
it is never a fork. The consuming UX + the free fallback live in `game-modes/07`;
this story owns the moderated AI GENERATION of the words. See
[feature.md](./feature.md) and [ADR 0001](../../adr/0001-ai-provider.md).

## Acceptance Criteria
- [x] AC-01: Given a request for AI word-bank options for a blank's category, when it
      runs, then it calls the `ai-cost-gate` server proxy (`ai-cost-gate/01`) with a
      small prompt (brand-voice system instruction + the category + words to avoid)
      and parses the model's reply into a set of ~8-10 short single words for that
      category - server-side; the browser never calls AI (game-modes/07 AC-06 boundary).
- [x] AC-02 (on-theme, on-brand, deduped): Given the generated words, then they are
      single common words appropriate to the category, in QuibbleStone's family-safe
      playful voice, and de-duplicated against the words just shown (favor fresh
      options) - a malformed or empty model reply is treated as "AI unavailable" and
      the caller falls back (AC-06), never surfaces a broken set.
- [x] AC-03 (child-safety, non-negotiable): Given AI-generated words are NOT pre-vetted,
      then every word passes the gate's moderate-before-display seam (`ai-cost-gate/05`:
      the existing `IContentSafetyFilter` + family-safe) BEFORE it is returned to any
      client or made tappable; a family-safe session only ever receives family-safe
      words. This story does NOT implement a second filter - it consumes the gate's seam
      (README section 6; game-modes/07 AC-04).
- [x] AC-04 (metered + capped, alpha-open): Given the AI call, then it passes through the
      gate's rate-limit/quota (`ai-cost-gate/03`) and spend circuit-breaker
      (`ai-cost-gate/04`) - so a player cannot spam it and a runaway cannot bust the $20
      ceiling. In alpha it is reachable by every session (ADR 0001 C), gated by
      quota/breaker, not entitlement; the entitlement seam (`ai-cost-gate/02`) is captured
      at session-creation and default-unlocked.
- [x] AC-05 (attribution): Given the AI call, then it emits the gate's ONE attribution
      telemetry event (`ai-cost-gate/04`) tagged with the FEATURE `jumble`, the model,
      token counts, estimated cost, and the anonymous session/room id - so this feature's
      share of AI spend is measurable from day one. No PII, no generated words, in
      telemetry.
- [x] AC-06 (fallback is the free reshuffle): Given AI is unavailable, quota-exhausted,
      breaker-open, times out, or returns nothing usable (or too few survive moderation),
      then the caller degrades to `game-modes/07`'s free deterministic reshuffle - the
      player always gets fresh runes, just curated ones, with no error and a brief
      "carving fresh words..." then a graceful result.
- [x] AC-07 (shared pipeline, not a fork): Given this is the lightest payload, then it
      uses the SAME server proxy + moderation path the whole-template on-demand generation
      (story 01) and its moderation (story 02) will use - a tiny payload today, the same
      plumbing tomorrow. It must not stand up a parallel generator or a parallel filter
      (feature.md: "reuse the factory, do not fork it").

## Out of Scope
- The consuming UX (the "Fresh runes" button, the "N left" meter, the "carving..."
  state) and the free deterministic reshuffle itself - `game-modes/07` owns those; this
  story is the AI generation backend it delegates to.
- Whole-template generation from a player prompt ("a story about our dog") - that is
  story 01, the heavier payload that ships later; this is only the per-category word set.
- Building the cost gate (`ai-cost-gate/01-06`) - this story CONSUMES it. If the gate is
  not yet built, this story is blocked on it (see the cross-feature DAG).
- Prompt-side moderation of player free text - the jumble has no player prompt (it
  jumbles by category); prompt moderation is story 02's concern for story 01's payload.
- Per-player personalization ("words about YOUR dog") - parked (game-modes feature.md);
  this jumbles by category, the same set for whoever is on the blank.

## Technical Notes
- **Where:** a small generation service under the on-demand feature's API surface (e.g.
  `api/src/Ai/Jumble/` or a method on an on-demand generation service) that: builds the
  prompt, calls `IAiCompletionClient` (`ai-cost-gate/01`), parses the reply (expect a
  JSON array of words; be defensive), and returns the moderated set via the gate's
  moderation seam (`ai-cost-gate/05`). The quota/breaker checks (03/04) wrap the proxy
  call in the gate's pipeline - this story calls the gate, it does not re-implement them.
- **Prompt shape (ADR 0001 sizing):** short system instruction (family-safe rules +
  stone-carving brand voice + "JSON array of single words only"), user = the category +
  a short avoid-list. Keep `maxOutputTokens` small (the payload is ~8-10 short words).
  This is the ~$0.0001-0.00015/call payload the gate is proved on.
- **Parsing defensively:** the model may wrap output or add stray text; parse leniently,
  and on any parse failure treat as "unavailable" -> fallback (AC-02, AC-06). Never throw
  into gameplay.
- **Wire contract:** extend the jumble result envelope `game-modes/07` reads (hand-mirrored
  DTO / hub method - no codegen), carrying the moderated words + the gate's remaining-quota
  count for the meter.
- **Consult the model docs** before wiring the call (the repo convention). Provider key in
  Key Vault via the gate, never `VITE_*`. Async; nullable; no PII to telemetry.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests` (mock `IAiCompletionClient`): a category request yields a parsed ~8-10 word set from the proxy reply |
| AC-02 | `api/tests`: on-theme single words, deduped vs shown; a malformed reply -> unavailable -> fallback |
| AC-03 | `api/tests` + code review: every AI word passes the gate's moderation seam; family-safe session gets only family-safe words |
| AC-04 | `api/tests` + manual: the call passes quota/breaker; at quota or breaker-open it does not call AI |
| AC-05 | manual (App Insights): one attribution event tagged feature=jumble with model/tokens/cost/anon id, no PII |
| AC-06 | manual: AI unavailable / quota / breaker / bad reply all degrade to the free reshuffle, no error |
| AC-07 | code review: uses the shared proxy + moderation seam; no parallel generator or filter |

## Dependencies
- `ai-cost-gate/01` (proxy), `/03` (quota), `/04` (breaker + attribution), `/05`
  (moderation), `/02` (entitlement capture) - the whole gate this rides.
- `ai-on-demand-generation/02` (the generated-word moderation policy this feature applies
  via the gate's seam).
- `game-modes/07` (the consuming UX + the free deterministic reshuffle this falls back to).
- `template-model` (`WordBankEntry` / category model the generated words conform to).
- `child-safety/01` + `/02` (the filter + family-safe the gate's moderation composes).
