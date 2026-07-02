<!--
  Implementation plan for the game-modes feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Game Modes Engine

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

This is the most important architectural piece (README section 4): **one engine, every mode a thin configuration of
three axes**. Stories 01/02 (Complete) built the engine + the mode-config interface and the first concrete mode,
Classic blind, expressed as config (not a fork). This pass (**re-planned 2026-07-01**, superseding the prior 6-mode
look-ahead) is a tight, **foundation-first** slice: story 03 makes the two shared screens (`FillBlank.tsx`,
`Reveal.tsx`) mode-aware via optional, purely-additive slots; stories 04, 05, and 06 then each ship exactly ONE new
`ModeConfig` proving exactly ONE of the engine's three declared-but-unbuilt axis values (`answer: 'word-bank'`,
`see: 'progressive-story'`, `reveal: 'progressively'`), each as a file-disjoint plug-in that never edits 03's files
or the engine itself. The whole feature is **pure engine TS + two page components + three new plug-in files** - it
never touches `GameHub.cs`.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Pure-TS engine module | the `web/src/engine/` established by **template-model/01** and **game-modes/01** | `web/src/engine/template.ts`, `assemble.ts`, `engine.ts`, `mode.ts` |
| Mode-agnostic collection + assembly | `collectWord` / `skipBlank` / `toOrderedWords` / `isCollectionComplete` / `assembleStory` (**game-modes/01**) - NOT touched by 03-06 | `web/src/engine/engine.ts` |
| The Classic blind pattern every mode mirrors | a plain `ModeConfig` literal (**game-modes/02**) | `web/src/engine/modes/classicBlind.ts` |
| Mode-aware screen slots (**new in 03**, consumed by 04/05/06) | `ModeSurfaces` contract (`answerSurface` / `seeContext` / `revealPresentation`) + the `classicBlindSurfaces` default | `web/src/pages/modeSurfaces.ts` |
| FillBlank's optional slots (**new in 03**) | `seeContext` (rendered above the prompt card) and `answerSurface` (replaces the free-text input) | `web/src/pages/FillBlank.tsx` |
| Reveal's optional slot (**new in 03**) | `revealPresentation` (replaces the default coral-highlight body) | `web/src/pages/Reveal.tsx` |
| Word-highlight rendering (reused read-only by 05/06) | `buildRevealParts()` + the coral highlight approach (**the-reveal/01**) | `web/src/pages/revealParts.ts`, `web/src/pages/Reveal.tsx` |
| Word-bank source data | `Template.wordBank` / `WordBankEntry` (**template-model/01**, already defined, optional field) | `web/src/engine/template.ts` |
| Word-bank jumble reshuffle (story 07) | a NEW pure helper mirroring `wordBankOffering.ts`; category filter reuses `wordsForCategory` (**gm/04**) | `web/src/content/wordBankJumble.ts` (new) |
| AI-generated jumble words (story 07, AI layer) | the live generate + moderate pipeline (**ai-on-demand-generation/05**) riding the shared **ai-cost-gate** (proxy + quota + breaker + moderation) - delegated, NOT re-implemented; alpha-free (quota/breaker-gated, not entitlement, per ADR 0001) | `docs/features/ai-on-demand-generation/`, `docs/features/ai-cost-gate/` |
| Family-safe content gating (reused for word-bank offering, 04) | the family-safe rule (**child-safety/02**) | `web/src/content/familySafe.ts` |
| Styling / theme tokens | the MUI theme (**design-system/01**) | `web/src/theme.ts` |
| Shared UI contracts | gold-CTA Button, teal spark/category Chips, `BottomActionBar` (**design-system/01**) | `web/src/components/` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Test harness | Vitest (**platform-devops/01**) for pure engine/content logic | `web/vitest.config.ts` |

What this feature **exports** that others import:
- The **mode config** type (the three axes) and the **engine** functions (collect words, assemble) - unchanged by
  this pass, still the contract `single-player` and `group-play` drive a round through.
- The **Classic blind** mode config and the **FillBlank**/**Reveal** screens/views - reused by `single-player/01`
  (solo filler) and `group-play` (per-player filler), now mode-aware via optional slots with zero change to how
  Classic blind itself renders (backward-compatible).
- (New, this pass) The `ModeSurfaces` contract (`web/src/pages/modeSurfaces.ts`, story 03) that any future mode
  colocates its plug-in surfaces with. Three new `ModeConfig` + `ModeSurfaces` pairs: Word Bank (04), Progressive
  Story (05), Progressive Reveal (06) - each a self-contained plug-in a future mode picker (single-player/group-play,
  out of this feature's scope) can select and wire in.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. Story 03 is the one serial foundation;
04/05/06 are file-disjoint from 03's footprint and from each other, so they fan out in the same wave with no further
coordination.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 mode-interface | #27 | `web/src/engine/mode.ts`, `web/src/engine/engine.ts` | template-model/01 | - | 0 (done) | medium |
| 02 classic-blind | #28 | `web/src/engine/modes/classicBlind.ts`, `web/src/pages/FillBlank.tsx` | gm/01, template-model/01, child-safety/01, design-system/01 | - | 0 (done) | high |
| 03 mode-aware-surfaces | TBD | `web/src/pages/FillBlank.tsx` (edit, optional-prop only), `web/src/pages/Reveal.tsx` (edit, optional-prop only), `web/src/pages/modeSurfaces.ts` (new), unit tests | gm/01, gm/02, the-reveal/01 | - | 1 | high |
| 04 word-bank | #53 | `web/src/engine/modes/wordBank.ts`, `web/src/pages/fillblank/WordBankAnswer.tsx`, `web/src/content/wordBankOffering.ts`, tests | gm/03, template-model/01, child-safety/02 | 05, 06 | 2 | medium |
| 05 progressive-story | TBD | `web/src/engine/modes/progressiveStory.ts`, `web/src/pages/fillblank/StorySoFarContext.tsx`, tests | gm/03, the-reveal/01 | 04, 06 | 2 | medium |
| 06 progressive-reveal | #52 | `web/src/engine/modes/progressiveReveal.ts`, `web/src/pages/reveal/ProgressiveRevealPresentation.tsx`, tests | gm/03, the-reveal/01 | 04, 05 | 2 | medium |
| 07 word-bank-jumble (FREE layer) | #128 | `web/src/pages/fillblank/WordBankAnswer.tsx` (add jumble control + swappable source), `web/src/content/wordBankJumble.ts` (new, pure reshuffle) + test | gm/04, template-model/01, child-safety/01+02 | - | 3 (ships first, no AI) | medium |
| 07 word-bank-jumble (AI layer) | #128 | the client wiring that prefers the AI source and falls back to the reshuffle (same `WordBankAnswer.tsx` + the jumble result DTO) | 07-free, the whole **ai-cost-gate** (01-05), **ai-on-demand-generation/05** | - | after the gate + ai-on-demand/05 (see ai-cost-gate cross-feature DAG) | medium |

**Concurrency per wave:** Wave 0 (01, 02) is already Complete. **Wave 1 = 03 alone, serial** - it is the only story
permitted to edit `FillBlank.tsx`/`Reveal.tsx`, and 04/05/06 all depend on the slots it adds (`ModeSurfaces`,
`answerSurface`, `seeContext`, `revealPresentation`), so nothing in wave 2 can start until it lands. **Wave 2 = {04,
05, 06} run fully in parallel** - each owns a disjoint `web/src/engine/modes/*.ts` config file, a disjoint new
component file (`fillblank/WordBankAnswer.tsx` vs. `fillblank/StorySoFarContext.tsx` vs.
`reveal/ProgressiveRevealPresentation.tsx`), and (04 only) a disjoint content-selection helper - no two stories in
this wave touch the same file, so the orchestrator's Phase 1 can fan them out with no further analysis. None of 04,
05, or 06 touches `web/src/engine/engine.ts`, `assemble.ts`, `mode.ts`, `template.ts`, `FillBlank.tsx`, or
`Reveal.tsx` - if a builder finds itself needing to, that is an abstraction leak per the Cross-cutting concerns
below; stop and flag it rather than patching around it.

## Per-story tech notes

### 01 - Mode interface (the three axes) [Complete]
Model a mode as a small config object over three axes; the engine collects/assembles independently of the active
mode. See `docs/features/game-modes/01-mode-interface.md` for full history - unchanged by this pass.

### 02 - Classic blind mode [Complete]
"The engine, configured this way" - subject-only view, free-text answers, end reveal. See
`docs/features/game-modes/02-classic-blind.md` for full history - unchanged by this pass except that story 03
extends `FillBlank.tsx` with new OPTIONAL props (regression parity is an explicit AC on 03).

### 03 - Mode-aware FillBlank + Reveal (foundation)
- **Approach:** add three optional, purely-additive slots so the two shared screens defer to whatever mode they are
  given instead of hardcoding Classic blind's shape: `FillBlank` gains `seeContext?: ReactNode` (rendered above the
  prompt card) and `answerSurface?: ReactNode` (replaces the free-text input + spark chips when supplied); `Reveal`
  gains `revealPresentation?: ReactNode` (replaces the default coral-highlight body when supplied). Ship ONLY the
  Classic-blind defaults working end to end when no slots are supplied - byte-for-byte parity with today's rendering
  is an explicit AC, proven by NOT touching `Solo.tsx`/`GroupRound.tsx` at all.
- **Owns / exports:** the new `ModeSurfaces` contract type (`web/src/pages/modeSurfaces.ts`) - `{ answerSurface?,
  seeContext?, revealPresentation? }` - plus a documented `classicBlindSurfaces: ModeSurfaces = {}` default, so 04/05/06
  each colocate their surfaces with their `ModeConfig` in a uniform shape.
- **Gotchas:** this is the ONE story permitted to edit `FillBlank.tsx`/`Reveal.tsx` in this feature - every later mode
  is a plug-in, never a further edit to these two files. `ModeConfig` itself (`mode.ts`) is NOT touched: surfaces are
  data the PARENT resolves and passes down, not a field on the axis config. No engine change of any kind. The mode
  picker that actually selects a mode and its surfaces for a live round is explicitly out of scope (single-
  player/group-play territory) - this story ships the slots unused by the two existing callers.

### 04 - Word Bank mode (answer axis)
- **Approach:** `{ see: 'subject-only', answer: 'word-bank', reveal: 'at-end' }` - a tappable curated word list
  (sourced from `template.wordBank`, filtered by the current blank's `category`) plugged into `FillBlank`'s
  `answerSurface` slot. Submission still flows through `engine.ts`'s existing `collectWord`, which already skips the
  free-text filter for `answer === 'word-bank'` - this story proves that documented seam, it does not add a new one.
- **Owns / exports:** `wordBank.ts` (config + `ModeSurfaces` pairing), `WordBankAnswer.tsx` (the answer surface,
  reusing the teal MUI Chip tap language from FillBlank's spark row), `wordBankOffering.ts` (pure "which templates'
  banks are offered given family-safe" helper).
- **Gotchas:** family-safe gating applies to which templates' banks are OFFERED (content-selection/session-setup
  time), never a per-tap check. A bank-less template simply is not offered - no crash, no empty list.

### 05 - Progressive Story mode (see axis)
- **Approach:** `{ see: 'progressive-story', answer: 'free-text', reveal: 'at-end' }` - the player sees the
  story-so-far (already-filled words highlighted coral via `buildRevealParts` against the PARTIAL collection) above
  the prompt card, updating after each submission, plugged into `FillBlank`'s `seeContext` slot. Proven with NO
  change to `engine.ts`/`assemble.ts` - just `assembleStory` called against a partial collection, which `assemble()`
  already handles non-throwing. `reveal` stays `'at-end'` so this story proves the SEE axis cleanly, distinct from
  06's reveal axis.
- **Owns / exports:** `progressiveStory.ts` (config + `ModeSurfaces` pairing), `StorySoFarContext.tsx` (reuses
  `buildRevealParts` read-only, renders only the segments preceding the current blank).
- **Gotchas:** `answer: 'free-text'` so submissions still route through the collection-path safety check; the
  story-so-far view only ever renders already-collected (already-vetted) words, so nothing unfiltered is ever shown,
  even transiently. Live cross-player broadcast of a shared story-so-far is a group-play hub concern, out of scope.

### 06 - Progressive Reveal mode (reveal axis)
- **Approach:** `{ see: 'subject-only', answer: 'free-text', reveal: 'progressively' }` - players fill blind exactly
  like Classic blind (no `seeContext`/`answerSurface` overrides); the REVEAL surface unveils the assembled story one
  filled word at a time (paced/stepped) rather than all at once, plugged into `Reveal`'s `revealPresentation` slot.
  Reuses `buildRevealParts` read-only against the already-complete `AssembledStory`; no engine/assemble change.
- **Owns / exports:** `progressiveReveal.ts` (config + `ModeSurfaces` pairing), `ProgressiveRevealPresentation.tsx`
  (reuses `buildRevealParts` read-only, paces the word parts in over time while literal text renders immediately).
- **Gotchas:** `answer: 'free-text'` -> collection-path safety check during filling; the reveal only paces
  already-vetted words. Real-time synchronization of the paced reveal across a group's players is a group-play hub
  concern, out of scope. No cumulative score, no vote, no Versus-shaped mechanic (that is the parked Versus/Duel
  mode, a genuine engine stretch, not this story's concern).

### 07 - Jumble the word bank (fresh options on demand)
- **Approach:** an enhancement to Word Bank's ANSWER SURFACE, not a new axis. Add a jumble control to
  `WordBankAnswer.tsx` and make the offered options a swappable source. Two layers: (1) FREE deterministic reshuffle -
  a new pure helper `web/src/content/wordBankJumble.ts` re-samples a different in-category subset from the growing
  curated (pre-vetted) pool, unit-tested like `wordBankOffering.ts`; (2) PREMIUM AI jumble - fresh on-theme words
  DELEGATED to `ai-on-demand-generation`'s generate + moderate pipeline, gated by an `ai.*` key at session-creation.
- **Owns / exports:** the jumble control + swappable source in `WordBankAnswer.tsx`, and `wordBankJumble.ts` (pure
  reshuffle). It does NOT own or fork any AI generator - that stays in `ai-on-demand-generation`.
- **Gotchas:** curated words keep skipping the free-text filter (as gm/04), but AI-sourced words are NOT pre-vetted -
  every one MUST pass `IContentSafetyFilter` + the family-safe gate BEFORE display (the one exception to gm/04's
  filter-skip). No engine/axis change and no edit to `FillBlank.tsx`/`Reveal.tsx` (jumbled picks submit via the same
  `collectWord` path) - if jumble forces an engine change, that is an abstraction leak, flag it. Deterministic
  reshuffle is free; only the AI path is gated (entitlement at session-creation) and metered (quota, a separate seam
  from the gate). On-brand label "Fresh runes" (the chosen name), not "shuffle" - a copy/theme token. Out of
  scope: the AI generator itself, a cosmetic reorder of the same words (gm/04's parked shuffle), owner-curated banks,
  and per-player personalization.

## Cross-cutting concerns

- **One engine, many thin modes** is the whole point of this feature. Story 03 is the foundation that makes the
  shared screens mode-aware; every mode after it (04, 05, 06, and any future mode) is a new `ModeConfig` value plus
  ONE plug-in surface component in its own file - never a fork of `engine.ts`, `assemble.ts`, `FillBlank.tsx`, or
  `Reveal.tsx`. If building a mode ever forces a change to any of those four files, that is an abstraction leak
  (playbook Principle 2) - stop and flag it rather than patching around it.
- **File-disjointness is the concurrency enabler.** Wave 2's three modes (04/05/06) can fan out with zero
  coordination specifically because each owns a distinct `web/src/engine/modes/*.ts` file and a distinct new
  component file, and none of them touches 03's footprint. This is deliberately the "prove foundation-first, then
  fan out" alternative to the prior look-ahead pass, which tangled group-play-shaped modes and one engine stretch
  (Versus/Duel, now parked - see feature.md) into the same wave as pure axis config.
- **Inter-feature ordering (prerequisites):** `template-model/01` (the template the engine plays), `child-safety/01`
  (free-text filter), `child-safety/02` (family-safe toggle, for 04's offering gate), `design-system/01` (theme +
  Button + Chips), `the-reveal/01` (`buildRevealParts`, reused read-only by 03/05/06). The mode-picker UI that
  selects a mode for a live round, and any real-time broadcast of shared mode state (a group-visible story-so-far in
  05, a synchronized paced reveal in 06), belong to `single-player`/`group-play` - explicitly out of scope for every
  story in this feature.
- **Child safety:** free-text answers pass the filter regardless of mode, because the call sits on the engine's
  collection path - no mode can bypass it. Word Bank (04) is the one mode that legitimately skips the free-text
  filter (pre-vetted content), documented explicitly rather than left implicit. No PII collected by any mode.
- **No i18n** (plain prompt/UI strings), **big tap targets**, **MUI-theme-only** (no hex/px literals - tokens from
  `web/src/theme.ts`), **FontAwesome-only** icons, **TS strict** (no `any`; guard, don't `!`), **no em dashes**.
