<!--
  Implementation plan for the game-modes feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Game Modes Engine

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

This is the most important architectural piece (README section 4): **one engine, every mode a thin configuration of
three axes**. Story 01 builds the engine + the mode-config interface; story 02 is the first concrete mode, Classic
blind, expressed as config (not a fork). The whole feature is **pure engine TS + the FillBlank screen** - it never
touches `GameHub.cs`, so it runs **in parallel with the entire `session-engine` chain**.

**Look-ahead update (2026-07-01):** stories 03-06 add the rest of README section 5's named modes ahead of their
build wave, so the backlog stays ahead of development. 03 (Progressive reveal), 04 (Blind + word bank), and 05
(Owner-curated word bank, built on 04) stay pure `ModeConfig` values, same shape as 02 - no new engine branch. 06
(Versus / Duel) is the one honest **engine stretch**: it generalizes `engine.ts`'s collection model to allow multiple
answers per blank and adds a reusable vote-collection primitive, both living in `web/src/engine/` rather than a
Versus-only path. 06 shares that vote primitive with `reveal-delight/03` (Golden Guardian) - see both stories' cross-
references. All four (03-06) are group-shaped (need a live roster/round) and their group-play-side wiring (broadcast
events, host-authoring distribution) is called out in each story's Technical Notes but is NOT in their own file
footprint - it belongs to whichever `group-play` wave eventually schedules the mode.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Pure-TS engine module | the `web/src/engine/` established by **template-model/01** | `web/src/engine/template.ts`, `assemble.ts` |
| Template type the engine plays | `Template` / `Blank` / `BlankCategory` (**template-model/01**) | `web/src/engine/template.ts` |
| Child safety (free-text answers) | the server-side filter (**child-safety/01**); for solo, the engine boundary call | `api/src/Safety/` |
| Styling / theme tokens | the MUI theme (**design-system/01**) | `web/src/theme.ts` |
| Shared UI contracts | gold-CTA Button, category/spark Chips, `BottomActionBar` (**design-system/01**) | `web/src/components/` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Test harness | Vitest (**platform-devops/01**) for the pure engine | `web/vitest.config.ts` |
| Word-highlight rendering (reused for progressive story-so-far, 03) | `buildRevealParts()` + the coral highlight approach (**the-reveal/01**) | `web/src/pages/revealParts.ts`, `web/src/pages/Reveal.tsx` |
| Family-safe content gating (reused for word-bank offering, 04/05) | `selectTemplates` / the family-safe rule (**child-safety/02**) | `web/src/content/familySafe.ts` |
| Round-start broadcast (extended for host-authored banks, 05, and duel setup, 06) | the round-start hub method (**group-play/01**) | `api/src/Hubs/GameHub.cs` |
| Round-robin distribution (extended for duel-blank assignment, 06) | pure distribution function (**group-play/02**) | `web/src/engine/distribute.ts` |
| Reveal broadcast (extended for vote tally, 06) | the reveal-transition hub logic (**group-play/03**) | `api/src/Hubs/GameHub.cs` |

What this feature **exports** that others import:
- The **mode config** type (the three axes) and the **engine** functions (collect words, assemble) - the contract
  `single-player` and `group-play` drive a round through.
- The **Classic blind** mode config and the **FillBlank** screen/view - reused by both `single-player/01` (solo
  filler) and `group-play` (per-player filler).
- (Look-ahead, 03-06) Three more `ModeConfig` values (Progressive reveal, Blind + word bank, Owner-curated word
  bank) and one genuine engine generalization (Versus/Duel's many-answers-per-blank collection shape) plus a shared
  **vote-collection primitive** (`web/src/engine/vote.ts`, story 06) that `reveal-delight/03` also imports.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 mode-interface | #27 | `web/src/engine/mode.ts` (the three-axes config type), `web/src/engine/engine.ts` (collect + assemble orchestration), unit tests | template-model/01 | session-engine chain (other feature, disjoint) | 1 | medium |
| 02 classic-blind | #28 | `web/src/engine/modes/classicBlind.ts` (config), `web/src/pages/FillBlank.tsx` (the filler screen) | gm/01, template-model/01, child-safety/01, design-system/01 | session-engine chain | 2 | high |
| 03 progressive-reveal | TBD | `web/src/engine/modes/progressiveReveal.ts` (config), edits `web/src/pages/FillBlank.tsx` (story-so-far preview slot, or a sibling wrapper) | gm/01, gm/02, the-reveal/01 | 04, 05 (disjoint config files; both touch FillBlank-family UI so verify no overlapping edit before running concurrently) | 3 | medium |
| 04 blind-word-bank | TBD | `web/src/engine/modes/blindWordBank.ts` (config), a word-bank answer surface (new component or FillBlank variant) | gm/01, gm/02, template-model/01, child-safety/02 | 03 | 3 | medium |
| 05 owner-curated-word-bank | TBD | a host-authoring step (new screen/component), a "bank source" selection helper in `web/src/engine/` | gm/04 | 03 | 4 | medium |
| 06 versus-duel | TBD | edits `web/src/engine/engine.ts` (multi-answer-per-blank generalization), new `web/src/engine/vote.ts` (shared primitive), edits `web/src/engine/distribute.ts` (duel-blank assignment), a multi-answer reveal + vote UI | gm/01, gm/02, group-play/02, group-play/03, the-reveal/01 | - (touches shared `engine.ts`; serialize against any concurrent engine-touching story) | 5 | high |

**Concurrency per wave:** 1 within the Slice-1 pair (02 imports 01's interface + uses the engine -> serialize). The
feature as a whole runs **concurrently with `session-engine`** (engine files + `FillBlank.tsx` are disjoint from
`GameHub.cs` / Home / Join / Lobby). For the look-ahead stories: 03, 04, and 05 are each pure new `ModeConfig` files
and can in principle run in the same wave, but 03/04/05 all touch FillBlank-adjacent UI (a story-so-far preview, a
word-bank answer surface, a host-authoring step) - confirm actual file disjointness at Phase 1 before fanning them
out; if two land on the same file, serialize or merge. **06 is the one story in this feature that edits `engine.ts`
itself** (the multi-answer-per-blank generalization) - it must NOT run concurrently with any other story touching
`web/src/engine/engine.ts`, and it depends on `group-play/02` + `/03` for the duel-blank distribution and vote-tally
broadcast plumbing, so it is scheduled after the group-play chain lands, not just after 01/02.

## Per-story tech notes

### 01 - Mode interface (the three axes)
- **Approach:** model a mode as a **small config object** over three axes - what the player sees (nothing / subject
  only / progressive story), how they answer (free text / word bank), when the reveal happens (at the end /
  progressively) (AC-01). The **engine** collects words for a template's blanks and assembles the final story
  **independently of the active mode** (AC-02), so adding a mode is configuration only - no change to collection or
  assembly (AC-03). The same template plays under any mode (AC-04). The **safety-filter call lives on the collection
  path** so every mode inherits it for free (AC-05).
- **Owns / exports:** `mode.ts` (the axes type), `engine.ts` (collect + assemble orchestration, building on
  `template-model`'s `assemble()`).
- **Gotchas:** keep it pure and unit-tested (Vitest). The interface must **allow** progressive reveal and word-bank
  answering even though Slice 1 implements neither (Out of scope) - design the axes to express them, do not build
  them. If implementing a mode ever forces a change here, the abstraction has leaked (flag it).

### 02 - Classic blind mode
- **Approach:** "**the engine, configured this way**" - subject-only view, free-text answers, end reveal (AC-08) -
  **not** a bespoke code path. The FillBlank screen renders the current blank from the engine/template: progress row
  + 8-segment bar, stone-tablet prompt card (category chip, prompt sentence with the category word in purple,
  sub-hint), carved free-text input (maxLength 20) with a "Need a spark?" row of 3 tappable example chips
  (`setWord(chipText)`), and the blind-mode reassurance panel (AC-01 to AC-04). The gold "Next word ->" submits
  **after the safety filter passes** (AC-05); a low-pressure "Stuck? Skip this word" ghost link advances leaving the
  blank empty (AC-06). On completion, transition to Waiting (group) or straight to the reveal (solo), having never
  shown the story (AC-07).
- **Owns / exports:** the Classic blind config and the FillBlank view. FillBlank is built **reusable** by both solo
  and group play (this story owns the single-filler mechanics only - multi-player distribution is `group-play/02`).
- **Gotchas:** safety filter runs **server-side on submission** in group play and **at the engine boundary** for
  solo - either way before the word is recorded. Progress bar segment count adapts to the number of assigned blanks.
  Spark chips can be hardcoded per category for Slice 1 (tying them to template metadata is a later enhancement).
  `transform: scale` for any entrance pops. Out of scope: word-bank answering, progressive reveal, FillBlank
  background animations.

### 03 - Progressive reveal ("Whisper mode") [look-ahead]
- **Approach:** a new `ModeConfig` (`see: 'progressive-story'`, `reveal: 'progressively'`) whose FillBlank rendering
  shows the story-so-far (already-filled words highlighted coral, via `buildRevealParts`) above the current prompt,
  updating after each submission (AC-01, AC-02). Proven with NO change to `engine.ts`/`assemble.ts` - the
  story-so-far view is just `assembleStory` called against a partial collection, which `assemble()` already handles
  non-throwing (AC-03).
- **Owns / exports:** `progressiveReveal.ts` (config) and whatever FillBlank-family UI renders the story-so-far
  preview (a new prop or a sibling wrapper - decide at build time per FillBlank's composition contract).
- **Gotchas:** the live cross-player broadcast of the shared story-so-far (so every player's screen updates as ANY
  player's word lands) is a `group-play`-owned hub concern, out of this story's footprint - flag it for whichever
  wave schedules the mode. Out of scope: the word-by-word carving animation (`reveal-delight/02` layers that on).

### 04 - Blind + word bank [look-ahead]
- **Approach:** a new `ModeConfig` (`answer: 'word-bank'`, otherwise Classic-blind-shaped) that swaps FillBlank's
  free-text input for a tappable list sourced from `template.wordBank`, filtered by the current blank's category
  (AC-01, AC-02). Submission still flows through `engine.ts`'s existing `collectWord`, which already skips the
  safety check for `answer === 'word-bank'` (documented in `engine.ts`'s header) - this story proves that seam, it
  does not add a new one (AC-03, AC-04).
- **Owns / exports:** `blindWordBank.ts` (config) and the word-bank answer surface (new component or a mode-aware
  FillBlank variant, per the parent's composition decision).
- **Gotchas:** family-safe gating applies to which templates' banks are OFFERED (content selection, session/round
  setup time), not a per-tap check (AC-05). Out of scope: owner-curated banks (05 builds on this).

### 05 - Owner-curated word bank [look-ahead]
- **Approach:** builds on 04's rendering/collection unchanged; adds a host-authoring step before round start (one
  small word list per category) and a round-scoped "bank source" that FillBlank's word-bank renderer prefers over
  `template.wordBank` when present (AC-01, AC-02, AC-06). Unlike 04's pre-vetted entries, host-typed words ARE free
  text, so this is the one place in the mode where the safety filter runs server-side before distribution (AC-03).
- **Owns / exports:** the host-authoring screen/step and a pure "prefer host bank, else template bank, else
  unavailable" selection helper in `web/src/engine/`.
- **Gotchas:** distributing the host-authored bank to all players is a `group-play/01`-adjacent hub concern (extends
  the round-start broadcast), out of this story's own footprint. `ModeConfig` itself does not grow a new field for
  "who authored the bank" - that is round setup data, kept separate (AC-06).

### 06 - Versus / Duel mode [look-ahead, engine stretch]
- **Approach:** the one mode in this pass that is NOT pure axis config. Generalizes `engine.ts`'s collection to
  allow multiple `SubmittedWord`s against one blank id, for exactly the blanks flagged as Versus in a round (AC-02);
  extends the reveal to present all competing answers together plus a lightweight, room-wide vote step (AC-04).
  Builds a small, reusable **vote-collection primitive** (`web/src/engine/vote.ts`: create/cast/tally) with no
  opinion on what the options are, so `reveal-delight/03` (Golden Guardian) can consume the exact same module rather
  than the two features inventing separate vote-counting logic (AC-05). Deliberately light-touch on tone: no score,
  no leaderboard, no "lost" framing (AC-06, mirrors `reveal-delight/03`'s same guard).
- **Owns / exports:** the `engine.ts` generalization, `vote.ts` (shared primitive - **exported for
  `reveal-delight/03` to import**), the duel-blank assignment addition to `distribute.ts`, and the multi-answer
  reveal + vote UI.
- **Gotchas:** this is the ONE story in the feature permitted to edit `engine.ts` - regression-cover that
  single-answer-per-blank behavior is unchanged for every non-Versus blank/mode before merging. Coordinate build
  order with `reveal-delight/03` so `vote.ts` is built once, not twice. Vote-tally broadcast rides the existing
  `group-play/03` reveal-transition hub pattern (a new event alongside `RosterChanged`), out of this story's own
  footprint but called out for the scheduling wave. Out of scope: multiple simultaneous duel blanks, Versus combined
  with word-bank/progressive axes, any cumulative scoring.

## Cross-cutting concerns

- **One engine, many thin modes** is the whole point of this feature - it is where the architectural bet is paid
  off or lost. Every later mode (progressive, word-bank, owner-curated) must be a new config object here, days of
  work not weeks. Word collection and template assembly belong to the **engine**, never to a mode (playbook
  Principle 2). **Versus (06) is the documented exception that proves the rule**: it is the one mode that stretches
  the engine, and it does so by generalizing the engine itself (collection model + a shared vote primitive) rather
  than forking a Versus-only path - any future mode that also needs many-answers-per-blank or a vote step reuses
  the same generalization, so the bet holds even under the stretch.
- **Inter-feature ordering (prerequisites):** `template-model/01` (the template the engine plays), `child-safety/01`
  (free-text filter), `design-system/01` (theme + Button + Chips). This feature must complete before `the-reveal/01`
  (which renders the collected words), `single-player/01`, and `group-play` (all consume the engine + FillBlank).
  The look-ahead stories (03-06) additionally depend on `group-play` landing (they are group-shaped: a word bank, a
  shared progressive story, and a duel/vote all need a live room) and, for 06, on `reveal-delight/03`'s build order
  for the shared vote primitive.
- **Child safety:** free-text answers pass the filter regardless of mode - because the call sits on the engine's
  collection path, no mode can bypass it. Word-bank modes (04, 05) are the one place a mode legitimately skips the
  free-text filter (pre-vetted content), documented explicitly in each story rather than left implicit.
- **No i18n** (plain prompt/UI strings), **big tap targets**, **no em dashes**.
