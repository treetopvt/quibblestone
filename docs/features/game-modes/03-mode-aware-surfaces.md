# Story: Mode-aware FillBlank + Reveal

**Feature:** Game Modes Engine  ·  **Status:** Complete  ·  **Issue:** #83

## Context
The engine's three axes (`web/src/engine/mode.ts`) were designed to allow
`answer: 'word-bank'`, `see: 'progressive-story'`, and `reveal: 'progressively'`
- but Slice 1's screens (`FillBlank.tsx`, `Reveal.tsx`) only ever render the
Classic-blind values (`see: 'subject-only'`, `answer: 'free-text'`,
`reveal: 'at-end'`), because that was the only mode that existed. Before any of
the three unbuilt axis values can become a real, playable mode, the two shared
screens need to know how to defer to whatever mode they are given, instead of
hardcoding Classic blind's shape.

This story is the ONE place that touches `FillBlank.tsx` and `Reveal.tsx`. It
does not build any new mode - it builds the SLOTS a mode plugs into, so stories
04, 05, and 06 can each add a mode as a self-contained, file-disjoint plug-in
with no further edits to the shared screens. See [feature.md](./feature.md) and
README section 4 (the three axes).

## Acceptance Criteria
- [x] AC-01: Given `FillBlank`, when its parent supplies an optional
      `seeContext` node, then it is rendered above the prompt card (below the
      existing subject label); when omitted, the layout is pixel-identical to
      today's Classic-blind rendering - `seeContext` is a pure addition, never a
      replacement of the existing `subject` prop.
- [x] AC-02: Given `FillBlank`, when its parent supplies an optional
      `answerSurface` node, then it REPLACES the free-text input + spark-chip
      row entirely (the carved `TextField` and "Need a spark?" row do not
      render); when omitted, the free-text input + spark chips render exactly
      as they do today.
- [x] AC-03: Given `Reveal`, when its parent supplies an optional presentation
      override (a `revealPresentation` slot or an equivalent mode-driven prop -
      see Technical Notes for the exact shape), then it is used to render the
      story body INSTEAD of the default coral-highlight paragraph; when
      omitted, `Reveal` renders exactly as it does today (the default at-end,
      all-at-once coral-highlight body via `buildRevealParts`).
- [x] AC-04: Given `Solo.tsx` and `GroupRound.tsx` (the two existing callers),
      then NEITHER file is edited by this story and BOTH continue to compile
      and render Classic blind identically to before this story landed - proof
      that the new props are purely additive/optional, not a breaking change to
      the composition contract.
- [x] AC-05: Given the new `ModeSurfaces` contract (Technical Notes), then it
      exports a type describing the optional surfaces a mode MAY supply
      (`answerSurface`, `seeContext`, `revealPresentation`) plus a documented
      "no surfaces" default matching Classic blind's current behavior, so
      stories 04/05/06 each colocate their surfaces with their `ModeConfig`
      rather than inventing their own shape.
- [x] AC-06: Given a free-text submission through `FillBlank` regardless of
      which optional slots are supplied, then it still flows through the
      parent's injected `onSubmitWord` (which itself calls `engine.ts`'s
      `collectWord` and the safety check) exactly as today - no slot added by
      this story creates a second path into collection, and no slot ever
      renders a word that has not already passed the safety filter.

## Out of Scope
- Building any concrete mode surface (word-bank tappable list, story-so-far
  view, progressive reveal presentation) - those are stories 04, 05, and 06,
  each in their own file.
- Any change to `engine.ts`, `assemble.ts`, `mode.ts`, or `template.ts` - this
  story is screens-only. If making FillBlank/Reveal mode-aware seems to require
  an engine change, that is an abstraction leak - stop and flag it.
- Editing `Solo.tsx` or `GroupRound.tsx` (AC-04) - they keep passing no
  surfaces and keep getting Classic-blind behavior for free.
- A mode picker / mode-selection UI (which mode a round plays) - that is a
  single-player/group-play concern, noted in feature.md Design notes.
- Real-time broadcast of any shared mode state (e.g. a story-so-far every
  player sees update live) - that is a group-play hub concern, out of scope
  here and in every mode story that touches it.

## Technical Notes
- New pure contract file: `web/src/pages/modeSurfaces.ts` (this story picks and
  owns this location rather than `web/src/engine/modes/surfaces.ts`, since the
  surfaces are React nodes - UI, not engine data - and colocating them with the
  pages that consume them keeps `web/src/engine/` free of React imports, per
  `mode.ts`'s own "no React, no SignalR" header contract). Shape:
  ```ts
  export interface ModeSurfaces {
    /** Replaces FillBlank's free-text input + spark chips when supplied. */
    answerSurface?: ReactNode;
    /** Rendered above FillBlank's prompt card when supplied (e.g. story-so-far). */
    seeContext?: ReactNode;
    /** Replaces Reveal's default coral-highlight body when supplied. */
    revealPresentation?: ReactNode;
  }
  ```
  Export a documented `classicBlindSurfaces: ModeSurfaces = {}` constant (all
  fields omitted) as the explicit "no surfaces" default, so a future reader
  does not have to infer that an empty object is intentional. `ModeConfig`
  itself (`mode.ts`) is NOT touched - surfaces are NOT added as a field on the
  axis config; each mode file (04/05/06) exports its `ModeConfig` and its
  `ModeSurfaces` as two separate values, and the PARENT (Solo/GroupRound,
  future work) is responsible for resolving which surfaces to pass into
  `FillBlank`/`Reveal` for the active mode. This story only defines the
  contract type and ships it unused by Solo/GroupRound (AC-04) - wiring a
  parent to actually SELECT a mode's surfaces is out of scope (see Out of
  Scope).
- `FillBlank.tsx`: add `seeContext?: ReactNode` and `answerSurface?: ReactNode`
  to `FillBlankProps`. Render `seeContext` in a new slot directly under
  `SubjectLabel` (or in its place, if `subject` is also omitted) and above
  `ProgressRow`, so a story-so-far view reads before the progress bar. Wrap the
  existing carved input + spark-chip block in a conditional: render
  `answerSurface` when supplied, else the existing free-text block unchanged.
  The gold "Next word" CTA's disabled/submitting logic currently keys off
  `currentWord` (the free-text form value) - when `answerSurface` is supplied,
  that surface is responsible for driving submission through the SAME
  `onSubmitWord` prop (e.g. by calling a callback FillBlank exposes, or by the
  surface owning its own submit affordance and FillBlank's CTA being hidden for
  that case) - decide the exact split at build time, but the constraint is
  singular: there is still exactly ONE path into `onSubmitWord`, never two.
- `Reveal.tsx`: add an optional presentation override to `RevealProps` (a
  `revealPresentation?: ReactNode` slot rendered inside the stone-tablet scroll
  panel INSTEAD of the `parts.map(...)` coral-highlight block, mirroring
  FillBlank's `answerSurface` pattern.) When omitted, keep today's
  `buildRevealParts(template, assembled)` rendering byte-for-byte (AC-03).
  `buildRevealParts` itself is NOT touched - it stays the shared pure helper
  05 and 06 both reuse read-only.
- Regression parity (AC-04) has no automated render harness yet (per
  `docs/features/_template/NN-story.md`'s test-harness note) - prove it by
  manual side-by-side check of the solo playthrough before/after, plus a
  Vitest unit test on `modeSurfaces.ts`'s default export shape.
- Reuse map: no new engine capability, no new safety seam - `collectWord`
  (`engine.ts`) and `assembleStory` are consumed exactly as today. MUI theme
  tokens only (`web/src/theme.ts`), FontAwesome only
  (`web/src/fontawesome.ts`), TS strict (no `any`, guard instead of `!`), big
  tap targets, no em dashes.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: FillBlank with a `seeContext` node renders it above the prompt card; without it, layout matches today |
| AC-02 | manual: FillBlank with an `answerSurface` node hides the free-text input/spark chips; without it, they render as today |
| AC-03 | manual: Reveal with a `revealPresentation` node renders it instead of the coral body; without it, `buildRevealParts` output renders as today |
| AC-04 | manual: `Solo.tsx` and `GroupRound.tsx` diff is empty for this story; solo + group playthroughs render Classic blind identically before/after |
| AC-05 | `web/src/pages/modeSurfaces.test.ts` - asserts `classicBlindSurfaces` is `{}` and the `ModeSurfaces` fields are all optional |
| AC-06 | manual + existing `web/src/engine/engine.test.ts` coverage - a free-text submission through FillBlank (with or without slots supplied) still resolves through the same `onSubmitWord` -> `collectWord` -> safety-check path |

## Dependencies
- game-modes/01-mode-interface
- game-modes/02-classic-blind
- the-reveal/01-text-reveal
