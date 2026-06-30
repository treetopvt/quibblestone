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

What this feature **exports** that others import:
- The **mode config** type (the three axes) and the **engine** functions (collect words, assemble) - the contract
  `single-player` and `group-play` drive a round through.
- The **Classic blind** mode config and the **FillBlank** screen/view - reused by both `single-player/01` (solo
  filler) and `group-play` (per-player filler).

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 mode-interface | #27 | `web/src/engine/mode.ts` (the three-axes config type), `web/src/engine/engine.ts` (collect + assemble orchestration), unit tests | template-model/01 | session-engine chain (other feature, disjoint) | 1 | medium |
| 02 classic-blind | #28 | `web/src/engine/modes/classicBlind.ts` (config), `web/src/pages/FillBlank.tsx` (the filler screen) | gm/01, template-model/01, child-safety/01, design-system/01 | session-engine chain | 2 | high |

**Concurrency per wave:** 1 within the feature (02 imports 01's interface + uses the engine -> serialize). The
feature as a whole runs **concurrently with `session-engine`** (engine files + `FillBlank.tsx` are disjoint from
`GameHub.cs` / Home / Join / Lobby).

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

## Cross-cutting concerns

- **One engine, many thin modes** is the whole point of this feature - it is where the architectural bet is paid
  off or lost. Every later mode (progressive, word-bank, owner-curated) must be a new config object here, days of
  work not weeks. Word collection and template assembly belong to the **engine**, never to a mode (playbook
  Principle 2).
- **Inter-feature ordering (prerequisites):** `template-model/01` (the template the engine plays), `child-safety/01`
  (free-text filter), `design-system/01` (theme + Button + Chips). This feature must complete before `the-reveal/01`
  (which renders the collected words), `single-player/01`, and `group-play` (all consume the engine + FillBlank).
- **Child safety:** free-text answers pass the filter regardless of mode - because the call sits on the engine's
  collection path, no mode can bypass it.
- **No i18n** (plain prompt/UI strings), **big tap targets**, **no em dashes**.
