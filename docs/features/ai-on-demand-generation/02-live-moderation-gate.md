<!--
  Story 02 of on-demand AI generation - the live moderation gate, decomposed from the feature.md
  sketch. Scoped for THIS slice to the generated-word (jumble) payload, riding ai-cost-gate/05's
  reusable seam; the heavier prompt + whole-template moderation extends this when story 01 ships.
  No em dashes.
-->

# Story: Live moderation gate for AI-generated content (before anyone plays)

**Feature:** On-Demand AI Generation  ·  **Status:** Complete  ·  **Issue:** #127

> **Shipped 2026-07-03** (scoped to the jumble word payload, per this story). The policy is
> realized by `JumbleWordGenerator` COMPOSING the gate's `ai-cost-gate/05` seam
> (`AiOutputModerator` = `IContentSafetyFilter` + family-safe + optional Content Safety) - no
> second filter or parallel path (AC-05). Every AI word passes moderation before it is
> returned (AC-01); a family-safe round keeps only family-safe words (AC-02). Refuse-gracefully
> (AC-03): too-few-safe -> the generator returns FellBack and the client degrades to the free
> reshuffle with no which/why leak. Audit-sample (AC-04): `AiOutputModerator` logs an anonymous
> dropped-COUNT only (no text, no PII); a richer aggregate can layer on later. Content Safety
> stays config-gated (AC-06). The flow is general enough for story 01's prompt/template payload
> to extend rather than fork (AC-07). Heavier prompt/whole-template moderation remains story 01.

> **Follow-up (2026-07-03, PR #149).** Family-safe OFF is now an intentional cheeky
> grown-up mode, driven by the GENERATOR steering the prompt (`/05`) - NOT a change to
> this moderation policy. AC-01's always-on hard gate (`IContentSafetyFilter`) still
> drops profanity, slurs, and explicit terms regardless of the toggle; AC-02's stricter
> family-safe layer still applies only when the toggle is ON. So "edgier" can only ever
> mean blocklist-clean grown-up words, never unsafe output - the safety floor is
> unchanged.

## Context
Moderation IS the feature (feature.md Design notes; README section 6): live AI output
to children is the highest-risk surface in the product, so the generated content must
pass automated safety BEFORE it is playable or visible - with no per-item human gate
(which is why it must be strong). This story owns the on-demand feature's MODERATION
POLICY. It is decomposed from the sketch and scoped to what THIS slice needs: the
AI-generated WORD payload of the jumble (`ai-on-demand-generation/05`), moderated via
the shared `ai-cost-gate/05` seam (the existing `IContentSafetyFilter` + family-safe,
with Azure AI Content Safety as the optional config-gated layer per ADR 0001 B). The
heavier concerns the sketch names - moderating a player's free-text PROMPT and a whole
generated TEMPLATE - attach to the whole-template generation (story 01) when it ships;
this story establishes the refuse-gracefully + audit-sample PATTERN they will extend,
applied to the word payload now. It does NOT re-implement a filter; it composes the
gate's seam. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01 (output moderated before play, non-negotiable): Given AI-generated words
      (the jumble payload), then EVERY word passes the gate's moderate-before-display
      seam (`ai-cost-gate/05`) BEFORE it is returned to any client or made
      playable/tappable - unsafe words are dropped, never shown (README section 6).
- [x] AC-02 (family-safe tightens it): Given a family-safe session, then moderation is
      stricter - only family-safe words survive - honoring the same toggle the curated
      content respects (`child-safety/02`).
- [x] AC-03 (refuse gracefully, no evasion teaching): Given content fails moderation,
      then the player-facing outcome is a friendly fallback (the free reshuffle) with a
      warm message - it NEVER explains which word failed or why in a way that teaches a
      player to evade the filter (feature.md: "never explain the rejection in a way that
      teaches evasion"). If too few words survive to be useful, degrade to the
      deterministic fallback rather than show a thin set.
- [x] AC-04 (audit sample, anonymous): Given moderation runs, then rejections are
      sampled/counted for ongoing human audit - anonymously, carrying NO PII and NO
      identity (an aggregate count or a scrubbed telemetry signal, never a nickname or
      join code) - so the automated gate's blind spots can be reviewed and tightened
      (feature.md: "sample/log for ongoing human audit").
- [x] AC-05 (shared seam, not a fork): Given the gate already provides the
      moderate-before-display seam, then this story COMPOSES it (and the family-safe
      gate) into the on-demand feature's policy - it does NOT stand up a second filter or
      a parallel moderation path (feature.md: "reuse the factory, do not fork it";
      game-modes/07 AC-04).
- [x] AC-06 (Content Safety optional, config-gated): Given Azure AI Content Safety is
      configured (via `ai-cost-gate/05` + `/06`), then generated content also passes it
      as a second layer; given it is not (the slice default), the existing filter +
      family-safe is the whole gate and behavior is unchanged - a config flip, not code
      (ADR 0001 B). Content Safety earns its place as the payloads grow to whole
      templates (story 01).
- [x] AC-07 (extensible to prompt + template, later): Given the whole-template
      generation (story 01) ships later, then the refuse-gracefully + audit-sample
      pattern this story establishes extends to moderating the player PROMPT and the
      generated TEMPLATE - this story does NOT build prompt/template moderation now (the
      jumble has no player prompt), but leaves the seam shaped so story 01 extends it
      rather than forking it.

## Out of Scope
- Moderating a player free-text PROMPT and a whole generated TEMPLATE - deferred to
  story 01 (the whole-template payload); the jumble has no player prompt. This story
  establishes the pattern they will reuse (AC-07), scoped to the word payload now.
- Building the moderation seam itself - that is `ai-cost-gate/05` (the reusable
  filter + family-safe composition); this story is the on-demand feature's POLICY on top
  of it.
- A human moderation queue / review UI - `ai-content-factory` territory; this story only
  samples anonymously for audit (AC-04).
- The AI generation of the words - story 05; this story is the safety gate on that
  output.
- Changing the curated-content filter-skip (`game-modes/04`) - untouched; AI-sourced
  output only.

## Technical Notes
- **Compose, do not fork.** Apply `ai-cost-gate/05`'s moderate-before-display seam
  (existing `IContentSafetyFilter.CheckAsync` + `FamilySafeContentSelector`) to the
  generated word set. The policy layer decides "enough survived?" and drives the
  graceful fallback (AC-03) - the filtering itself is the gate's.
- **Audit sampling (AC-04):** count rejections anonymously - a scrubbed App Insights
  signal (through `PiiScrubbingTelemetryInitializer`) or an aggregate counter. Never the
  rejected text, never PII. Keep it cheap and fire-and-forget.
- **Refuse-gracefully copy (AC-03):** a single warm fallback message ("no fresh runes
  right now") reused with the quota/breaker fallbacks - do NOT branch the message by
  rejection reason.
- **Shape for later (AC-07):** keep the policy method general enough that story 01 can
  pass a template/prompt through the same "moderate -> enough-safe? -> refuse-gracefully
  -> audit-sample" flow, rather than duplicating it.
- Async; nullable; no PII to telemetry; no em dashes.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests`: an unsafe generated word is dropped and never returned/playable |
| AC-02 | `api/tests`: a family-safe session keeps only family-safe words; a non-family-safe session keeps more |
| AC-03 | manual + `api/tests`: a rejection yields the warm fallback with no which/why leak; too-few-safe -> deterministic fallback |
| AC-04 | code review + manual: rejections are sampled anonymously (no content, no PII) and queryable for audit |
| AC-05 | code review: composes the gate's seam; no second filter or parallel moderation path |
| AC-06 | `api/tests`: Content Safety absent = today's behavior; present = output passes both layers |
| AC-07 | code review: the policy flow is general enough for story 01 to extend to prompt/template without a fork |

## Dependencies
- `ai-cost-gate/05` (the reusable moderate-before-display seam this composes) + `/06`
  (the optional Content Safety resource).
- `child-safety/01` (the filter) + `/02` (family-safe) - the gates the seam is built on.
- `ai-on-demand-generation/05` (the AI word generation whose output this moderates).
- `platform-devops/04` (the App Insights pipeline + PII scrubber the audit sample rides).
