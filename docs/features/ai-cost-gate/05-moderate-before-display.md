<!--
  Story 05 of the AI cost gate - the reusable moderate-before-display seam every AI output passes. No em dashes.
-->

# Story: Moderate AI output before display

**Feature:** AI Cost Gate  ·  **Status:** Not Started  ·  **Issue:** #124

## Context
The gate's fifth piece and a non-negotiable (feature.md; ROADMAP "The AI cost gate"
piece 5; README section 6): AI output is unvetted text, so it passes the safety
filter AND the family-safe gate BEFORE any child sees it. This story builds the
REUSABLE moderation seam every AI feature routes output through - not a
jumble-specific check. Per [ADR 0001](../../adr/0001-ai-provider.md) decision B, the
existing server-side `IContentSafetyFilter` (blocklist) + `FamilySafeContentSelector`
is the enforced hard gate now; Azure AI Content Safety is wired behind a
config-presence flag as an optional second layer, turned on for the larger free-text
payloads (whole templates in `ai-on-demand-generation/01-02`) later. See
[feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01 (hard gate on every AI word): Given AI-generated output, then each item
      passes the existing server-side `IContentSafetyFilter.CheckAsync` BEFORE it is
      returned to any client or made displayable/tappable - an item that fails is
      dropped, never shown. No unfiltered AI text reaches a player (README section 6).
- [ ] AC-02 (family-safe honored): Given a family-safe session, then AI output is
      additionally gated by the family-safe rule (`FamilySafeContentSelector` /
      `isFamilySafe`), so a family-safe session only ever sees family-safe AI output -
      the same toggle the curated content already honors.
- [ ] AC-03 (reusable, not jumble-specific): Given this seam, then it is a general
      "moderate this AI output before display" service reusable by every AI feature
      (jumble now; verdict, on-demand tales, packs later) - it does NOT bake in
      word-bank specifics. The jumble (`ai-on-demand-generation/05`) CONSUMES it; it
      does not fork its own filter (game-modes/07 AC-04).
- [ ] AC-04 (drop-and-continue, enough-left): Given a batch of AI items (e.g. the
      jumble's ~8-10 words) where some fail moderation, then the safe items are kept
      and the unsafe dropped; if too few survive to be useful, the caller degrades to
      the deterministic fallback rather than showing a thin or empty set (never an
      empty list, never a broken surface).
- [ ] AC-05 (Content Safety optional second layer, config-gated): Given Azure AI
      Content Safety configuration is present, then AI output ALSO passes a Content
      Safety check as a second layer; given it is absent (the default now), the seam
      runs the existing filter + family-safe only and behaves identically to today -
      the same config-presence/no-op pattern the API already uses (AC mirrors story
      01 AC-04). Turning Content Safety on is a config flip, not a code change.
- [ ] AC-06 (no evasion teaching): Given output is rejected, then the player-facing
      result is a friendly "no fresh runes right now" style fallback - it never
      explains WHICH item failed or WHY in a way that teaches evasion
      (`ai-on-demand-generation` feature.md moderation posture). Rejections may be
      counted/sampled anonymously for audit (no content, no PII).
- [ ] AC-07 (moderation is not skippable): Given the gate, then there is no code path
      where AI output reaches a client without passing AC-01 (+ AC-02 for family-safe)
      - the moderation call sits in the server proxy path, not in an optional caller
      step a future feature could forget. (Curated, pre-vetted content still skips the
      filter exactly as `game-modes/04` documents; this AC is about AI-SOURCED output
      only.)

## Out of Scope
- The proxy transport (story 01), quota (story 03), and the spend breaker (story 04).
- Moderating a player's free-text PROMPT (for on-demand "a story about our dog")
  - that is `ai-on-demand-generation/02`'s prompt-side concern for the later
  whole-template feature; the jumble has no player prompt (it jumbles by category),
  so this slice moderates OUTPUT only. This story provides the output-moderation seam
  02 will also use.
- Standing up Azure AI Content Safety in Bicep - the Bicep for it is story 06
  (optional resource); this story consumes the config if present and no-ops if not.
- Human moderation queues / audit UI (`ai-content-factory` territory) - anonymous
  sample counts only here.
- Changing the existing curated-content filter-skip (`game-modes/04`) - untouched;
  this is AI-sourced output only (AC-07).

## Technical Notes
- **Where:** a moderation step in the server proxy path (`api/src/Ai/`), applied to
  every `IAiCompletionClient` output before it is returned. Compose the existing
  `IContentSafetyFilter.CheckAsync` (async by design for exactly this drop-in) with
  the `FamilySafeContentSelector` family-safe rule. Keep it a small, testable
  service so a caller cannot bypass it (AC-07).
- **Content Safety second layer:** register behind the config-presence branch (like
  story 01 / `ITelemetrySink`): if `ContentSafety:Endpoint` is present, wrap the
  existing filter with an Azure AI Content Safety check (Azure SDK, server-side, key
  from Key Vault); else the existing filter is the whole gate. For the tiny jumble
  payload (single common words) the existing filter is sufficient; Content Safety
  earns its place on the larger prose payloads later (ADR 0001 B).
- **Batch semantics (AC-04):** moderate the list, keep survivors, and let the caller
  decide "enough left?" (the jumble wants a usable set or it falls back). Return the
  filtered set + a "sufficient" signal, not a thrown error.
- **Audit sampling:** if a rejection is counted, count it anonymously (a number, or
  a scrubbed telemetry event) - never the rejected text, never PII.
- Verbose header comment (CLAUDE.md section 4). Async; nullable; no PII.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests/Ai/AiModerationTests.cs`: an AI item failing the filter is dropped and never returned |
| AC-02 | `api/tests`: a family-safe session drops non-family-safe AI output; a non-family-safe session keeps more |
| AC-03 | code review: the seam is generic (moderate output), consumed by the jumble, not forked |
| AC-04 | `api/tests`: a partially-unsafe batch keeps survivors; too-few-left triggers the caller's deterministic fallback |
| AC-05 | `api/tests`: with Content Safety config absent, behavior equals today's filter; with it present, output passes both |
| AC-06 | code review + manual: rejection yields a friendly fallback, no which/why leak; sampling carries no content/PII |
| AC-07 | code review: no code path returns AI output without moderation; curated content still skips (unchanged) |

## Dependencies
- `child-safety/01` (the `IContentSafetyFilter` blocklist seam) + `child-safety/02`
  (the family-safe toggle / `FamilySafeContentSelector`) - the existing gates this
  composes.
- cost-gate/01 (the proxy path this moderation sits in).
- cost-gate/06 (the optional Content Safety Bicep resource, if that layer is turned
  on; this story no-ops without it).
