# Story: Jumble the word bank (fresh options on demand)

**Feature:** Game Modes Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #128

## Context
Word Bank mode (`game-modes/04`) is fun, and the curated word pool is growing
(separate content task). But right now the tappable options for a blank are a fixed
slice of `template.wordBank` - if none of them spark joy, the player is stuck with
them. This story adds a "jumble": a low-friction action that swaps the offered words
for a FRESH set for the same category, so a player who does not like the options just
jumbles for new ones. It has two layers, mirroring the "generous free tier, premium
delight on top" posture:

1. A FREE, deterministic reshuffle that re-samples a different subset from the growing
   curated (already-vetted) pool for that blank's category - instant, offline, always
   safe. **This layer ships FIRST and is fully self-contained** (no AI, no gate) - it
   is also the fallback the AI cost gate's circuit-breaker degrades to.
2. An AI jumble that generates fresh, on-theme/on-brand words via an AI call - delegated
   to `ai-on-demand-generation/05` (the live-generate + moderate pipeline), which rides
   the shared **AI cost gate** (`ai-cost-gate`), because AI output is NOT pre-vetted and
   every AI call must be metered/moderated. Per [ADR 0001](../../adr/0001-ai-provider.md)
   decision C, in ALPHA this AI jumble is free for everyone, gated only by the cost
   gate's rate-limit/quota + spend circuit-breaker (not by entitlement); the `ai.*`
   entitlement key is still reserved so real charging attaches later.

This is an enhancement to the Word Bank ANSWER SURFACE, not a new mode/axis - it
changes the SOURCE of the option list fed to `WordBankAnswer`, and touches neither
`FillBlank.tsx`/`Reveal.tsx` nor the engine (feature.md's foundation-first discipline).
It also gives a home to feature.md's parked "AI-personalized ... word banks generated
per player" note. See [feature.md](./feature.md) and `game-modes/04-word-bank.md`.

## Acceptance Criteria
- [ ] AC-01: Given Word Bank mode and a blank being filled, then the answer surface
      offers a "jumble" action (a button/chip with an on-brand label and a FontAwesome
      glyph, big tap target); tapping it replaces the currently-offered words for that
      blank with a fresh set for the SAME category, without leaving the screen or
      losing my place in the round.
- [ ] AC-02 (free layer): Given the jumble action, then its DEFAULT source is a
      deterministic reshuffle - it re-samples a DIFFERENT subset from the curated,
      already-vetted pool for the blank's category (the growing content pool), favoring
      words not just shown; if the pool is exhausted it cycles gracefully (never an
      empty list, and the action soft-disables or wraps rather than erroring). This
      layer needs NO AI, works offline, and is FREE.
- [ ] AC-03 (AI layer): Given a session where AI is available (in alpha: every session,
      subject to the cost gate's quota/breaker), then jumble can instead pull a fresh set
      of AI-generated, on-theme/on-brand words for the category - and this generation is
      delegated to `ai-on-demand-generation/05` riding the `ai-cost-gate` proxy (it does
      NOT introduce a second, parallel generate/moderate path here). This is the
      "bottomless options" delight; the free deterministic reshuffle (AC-02) remains the
      fallback whenever AI is unavailable, quota-exhausted, breaker-open, or (later)
      unentitled.
- [ ] AC-04 (child-safety, non-negotiable): Given the source of the words, then:
      curated reshuffle words stay pre-vetted and skip the free-text filter exactly as
      `game-modes/04` AC-04 already documents (they come from vetted lists); BUT
      AI-generated words are NOT pre-vetted, so every AI-sourced option MUST pass the
      safety filter AND honor the family-safe toggle BEFORE it is ever displayed or made
      tappable. This is the ONE place Word Bank's filter-skip does not apply - no
      unfiltered AI word reaches a player, and a family-safe session only ever jumbles up
      family-safe words (README section 6).
- [ ] AC-05 (entitlement seam, alpha-open): Given the two layers, then the deterministic
      reshuffle (AC-02) is FREE (a base delight, no gate). The AI jumble (AC-03) rides the
      `ai-cost-gate`: its entitlement key (`ai.onDemand` / reserved `ai.wordBank`) is
      evaluated ONCE at session-creation (never per-tap), but per ADR 0001 decision C it is
      default-UNLOCKED in alpha, so the AI jumble is reachable by every session and its real
      alpha gate is the cost gate's rate-limit/quota + spend circuit-breaker, not the
      entitlement. The reserved key means turning on real gating later is a config flip, not
      a refactor (README section 3 - the free tier is generous; the seam is ready for
      charging without one now).
- [ ] AC-06 (no engine/axis leak): Given this story, then it is expressed purely as an
      enhancement to the Word Bank answer surface plus a swappable option SOURCE - it
      adds NO new `ModeConfig` axis value and does NOT edit `FillBlank.tsx`,
      `Reveal.tsx`, or `web/src/engine/` (collection + assembly are unchanged; a jumbled
      word is submitted through the same `collectWord` path as any word-bank pick, per
      `game-modes/04` AC-03). If jumble ever forces an engine change, that is an
      abstraction leak - flag it (feature.md Design notes).
- [ ] AC-07 (on-brand naming): Given the jumble action, then it is labelled "Fresh
      runes" (the chosen on-brand name, in QuibbleStone's stone/carving voice - not a
      generic "shuffle"). The label lives with the copy/theme, not hardcoded per
      instance, and stays kid-legible with a big tap target (a suitable FontAwesome
      glyph, e.g. dice/wand/sparkles, registered in `web/src/fontawesome.ts`).
- [ ] AC-08 (AI cost/abuse seam): Given the AI jumble path, then it notes a rate-limit /
      quota METERING seam (how many AI jumbles remain) as distinct from the entitlement
      gate - so a player cannot spam unbounded AI calls - consistent with
      `ai-on-demand-generation`'s "entitlement answers unlocked/not; metering answers
      how-many-left" split. The deterministic reshuffle needs no such limit.

## Out of Scope
- The actual AI generation + moderation implementation - that is
  `ai-on-demand-generation`'s pipeline (this story consumes it via AC-03/AC-04 and adds
  a sketch story there; it does not build a second generator).
- A cosmetic reorder of the SAME words (that is `game-modes/04`'s parked "shuffle /
  randomized-order" nicety - jumble deliberately provides DIFFERENT words, not a shuffle
  of the current set).
- Owner-curated word banks (the host supplying the bank) - parked in feature.md.
- Per-player personalization of the jumble ("words about YOUR dog") - the parked
  AI-personalized-per-player idea; this story jumbles by CATEGORY, the same set for
  whoever is on that blank.
- A word-bank authoring UI (templates + pools stay hand-authored TS literals per
  `template-model/02`; the growing pool is a content task, not this story).
- Free-text mode - jumble is a Word Bank affordance only (free-text modes have the
  existing "Need a spark?" chips, a different mechanic).

## Technical Notes
- **Answer surface, not the engine.** Extend `web/src/pages/fillblank/WordBankAnswer.tsx`
  (the `answerSurface` plug-in from `game-modes/04`) with a jumble control and a
  swappable option source, and/or lift the "current options" into a small stateful
  offering the surface renders. Do NOT edit `FillBlank.tsx`/`Reveal.tsx`/`engine.ts`
  (AC-06). A jumbled selection still submits via the standard `collectWord` path.
- **Keep the reshuffle pure + testable** (mirror `web/src/content/wordBankOffering.ts`):
  a new pure helper, e.g. `web/src/content/wordBankJumble.ts` -
  `nextOptions(pool, category, alreadyShown)` returning a fresh in-category subset,
  deterministic given its inputs and unit-tested like the other content helpers. Reuse
  the existing category filter (`wordsForCategory`) and the family-safe rule
  (`familySafe.ts` / `offerWordBankTemplates`) rather than inventing a second gate.
- **AI path is async + delegated + gated.** The AI jumble calls into
  `ai-on-demand-generation/05`, which rides the shared `ai-cost-gate`: the server-side
  proxy (`ai-cost-gate/01`, provider key in Key Vault, never a `VITE_*` var) makes the
  Foundry call; the words route through the gate's quota/breaker (`/03`, `/04`) and its
  moderate-before-display seam (`/05`, composing the existing `IContentSafetyFilter` +
  family-safe) BEFORE display (AC-04). This story does NOT call Foundry directly and does
  NOT build its own filter. Show a brief "carving fresh words..." state; never block the
  round. (The provider/model decision is [ADR 0001](../../adr/0001-ai-provider.md):
  Azure AI Foundry, gpt-4o-mini.)
- **Free layer ships first, independently.** AC-01/02/06/07 (the button + deterministic
  reshuffle) need only the existing Word Bank surface and are a self-contained PR that
  ships before any AI. AC-03/04/05/08 (the AI layer) are wired once `ai-cost-gate` and
  `ai-on-demand-generation/05` land (see this feature's `implementation.md` and
  `ai-cost-gate/implementation.md`'s cross-feature DAG).
- **Entitlement + metering are decided up front** (AC-05, AC-08): the `ai.*` gate at
  session-creation (reserved, alpha-unlocked - `ai-cost-gate/02`), metering as a separate
  quota concern (`ai-cost-gate/03`) - not per-tap checks.
- Every color/spacing token from `web/src/theme.ts`; FontAwesome only
  (`web/src/fontawesome.ts`); TS strict, no `any`.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: a jumble control on the Word Bank surface swaps the offered words for a fresh in-category set without leaving the screen |
| AC-02 | `web/src/content/wordBankJumble.test.ts` - reshuffle returns a different in-category subset, favors not-just-shown words, and cycles (never empty) when the pool is small |
| AC-03 | manual (entitled session): jumble can pull an AI-generated set; falls back to the deterministic reshuffle when AI is unavailable/unentitled |
| AC-04 | integration + code review: curated words skip the filter (as game-modes/04); every AI-sourced word passes the safety filter + family-safe gate before it is shown |
| AC-05 | code review: deterministic reshuffle has no entitlement gate; the AI jumble is gated once at session-creation, no per-tap check |
| AC-06 | code review: no new `ModeConfig` axis; no edit to `FillBlank.tsx`/`Reveal.tsx`/`engine.ts`; jumbled picks submit via `collectWord` |
| AC-07 | manual: the action reads "Fresh runes" (on-brand, not "shuffle"), kid-legible, big tap target |
| AC-08 | code review: an AI-jumble rate-limit/quota metering seam exists, separate from the entitlement gate |

## Dependencies
- game-modes/04-word-bank (the answer surface + word source this enhances)
- template-model/01-template-schema (`WordBankEntry` / `Template.wordBank` - the growing curated pool)
- child-safety/01-profanity-filter (the safety filter AI-sourced words must pass)
- child-safety/02-family-safe-toggle (the family-safe gate on the offered words)
- ai-on-demand-generation/05 (the live generate + moderate pipeline the AI jumble delegates to - now a buildable story)
- ai-cost-gate (the shared proxy + quota + breaker + moderation the AI jumble rides - `01`-`05`; the free reshuffle is the fallback its breaker degrades to)
- billing-entitlements/01 (#70) (the `ai.*` capability key reserved at session-creation - alpha-unlocked per ADR 0001)
