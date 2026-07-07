<!--
  Implementation plan for the single-player feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Single-Player Experience

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

Solo is the no-friction entry point and the funnel into group play (README section 1). It is almost entirely
**composition**: reuse the engine (`game-modes`), the FillBlank screen, and the Reveal screen, wired into a local
(no-room, no-SignalR) flow. Proving the engine works solo first de-risks group play. One story.

*(2026-07-07: "one story" was true when this plan was written; a second story - 02 solo mode
picker, issue #98 - was added later and shipped via PR #97. Its row is in the Wave Plan below;
the original prose is kept as written.)*

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Engine (collect + assemble, single filler) | the mode interface + Classic blind (**game-modes/01, /02**) | `web/src/engine/engine.ts`, `web/src/engine/modes/classicBlind.ts` |
| Filler screen | the FillBlank view (**game-modes/02**) - render as a component, do not edit it | `web/src/pages/FillBlank.tsx` |
| Reveal screen | the Reveal view + "Share the tale" + "Play another round" (**the-reveal/01**) | `web/src/pages/Reveal.tsx` |
| Templates + seed content | schema + seed library (**template-model/01, /02**) | `web/src/engine/template.ts`, `web/src/content/seedLibrary.ts` |
| Home entry point | the Home screen (**session-engine/01**) - add a solo affordance | `web/src/pages/Home.tsx` |
| Child safety + family-safe | the free-text filter (**child-safety/01**) at the engine boundary; family-safe content gate (**child-safety/02**) | `api/src/Safety/` |
| Styling / theme + Button | the MUI theme + shared Button (**design-system/01**) | `web/src/theme.ts`, `web/src/components/` |

What this feature **exports:** the solo flow/controller (`Solo.tsx`) and a solo entry on Home. Nothing else imports
it (it is a leaf).

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 solo-play | #29 | `web/src/pages/Solo.tsx` (local engine flow); edits `web/src/pages/Home.tsx` (solo entry) | template-model/01, game-modes/02, the-reveal/01, child-safety/01, child-safety/02, session-engine/01 (Home.tsx), design-system/01 | group-play chain (disjoint) | 1 | medium |
| 02 solo-mode-picker | #98 | edits `web/src/pages/Solo.tsx` (mode picker at setup; resolves the picked mode's `ModeConfig` + `ModeSurfaces` into the shared FillBlank / Reveal slots) - added 2026-07-07 to record the shipped story (PR #97) | 01 solo-play, game-modes/03-06 | - | 2 | medium |

**Concurrency per wave:** 1 (single story). It runs **in parallel with the `group-play` chain** - solo owns
`Solo.tsx` + an edit to `Home.tsx`, while group-play owns `GameHub.cs` + Waiting / Round Complete; no shared file.
The one serialization point is `Home.tsx`: it is owned by `session-engine/01`, so this story lands after se/01.

## Per-story tech notes

### 01 - Solo play (Classic blind end to end)
- **Approach:** a local, single-client flow - **no room, no join code, no SignalR** (AC-01) - that reuses the same
  engine as group play with a **single filler**. From a solo entry on Home, pick (or be given) a template, play
  Classic blind by composing the **existing** FillBlank screen for each blank (AC-02), then render the **existing**
  Reveal screen for the completed story (AC-03). The replay loop is one tap via the Reveal screen's "Play another
  round" (AC-06). Solo lands on the Reveal screen with a **personal summary** (story title + my word count) and the
  "Share the tale" + "Play another round" actions, and **skips** the group Round Complete crew recap entirely -
  there is no crew to recap and no per-player attribution to show (AC-07).
- **Owns / exports:** `Solo.tsx` (the flow/controller) and the Home solo affordance.
- **Gotchas:** **reuse, do not re-implement** - FillBlank and Reveal are rendered as components, not edited (editing
  them would collide with `game-modes/02` / `the-reveal/01`). Free-text answers pass the safety filter at the engine
  boundary and only **family-safe** content is used, so solo is safe to hand to a kid (AC-04). Never ask for an
  account or any personal information (AC-05). Out of scope: accounts, image/keepsake export (Phase 3), the group
  Round Complete recap, AI content, modes other than Classic blind, any room/multiplayer behavior.

## Cross-cutting concerns

- **Inter-feature ordering (prerequisites):** `game-modes/02` (engine + FillBlank), `the-reveal/01` (Reveal screen),
  `template-model/01` + `/02` (schema + seed content), `child-safety/01` + `/02` (filter + family-safe), and
  `session-engine/01` (it edits `Home.tsx`). It does **not** depend on the rest of `session-engine` or any of
  `group-play`.
- **`Home.tsx` is the only shared-file touch.** se/01 creates Home; this story adds the solo entry. If both are
  in-flight, either serialize (se/01 first) or fold the solo entry into se/01 - do not run them as parallel builders
  on the same file. (Open question flagged for the user below in the chat summary.)
- **Child safety + no PII:** the same filter the rest of the game uses, called at the solo engine boundary; nothing
  personal is collected.
- **Keep the path to first laugh short** (zero friction is the whole point). **No i18n** (plain strings),
  **big tap targets**, **no em dashes**.
