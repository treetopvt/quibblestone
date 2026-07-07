# Feature: Game Modes Engine

## Summary
The single most important architectural piece: one engine that every game
variation is a thin configuration of. A mode differs only on three axes - what
the player sees, how they answer, and when the reveal happens. Slice 1 built the
abstraction plus the first concrete mode, Classic blind. This pass makes the two
shared screens (FillBlank, Reveal) mode-aware and ships one new mode per
remaining unbuilt axis value - Word Bank (answer), Progressive Story (see), and
Progressive Reveal (reveal) - proving each axis in isolation, still expressed as
configuration on the same engine.

## README reference
README section 4 ("one engine, many thin modes" - the three axes) and section 7
(Epic Map - Phase 1, Game Modes Engine). Mode list: section 5 (Classic blind is
"first mode built"; Blind + word bank, Progressive reveal are named next
variations - reframed here per-axis, see Decisions below).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #27 | Mode interface (the three axes) | Complete |
| 02 | #28 | Classic blind mode | Complete |
| 03 | #83 | Mode-aware FillBlank + Reveal (foundation) | Complete |
| 04 | #53 | Word Bank mode (answer axis) | Complete |
| 05 | #84 | Progressive Story mode (see axis) | Complete |
| 06 | #52 | Progressive Reveal mode (reveal axis) | Complete |
| 07 | #128 | Jumble the word bank (fresh options on demand) | Complete |

## Dependencies
- template-model (a mode plays a template).
- child-safety (free-text answers are filtered regardless of mode).
- design-system (Classic blind screen uses the theme, buttons, and AppBar).
- the-reveal (03/05/06 all reuse `buildRevealParts` read-only for word
  highlighting - the-reveal/01 must exist first).
- The mode-picker / mode-selection UI that chooses a mode for a live round, and
  any real-time broadcast of shared mode state (a group-visible story-so-far,
  a synchronized paced reveal), are single-player/group-play concerns - out of
  scope for every story in this feature, called out story by story.

## Design notes
- The three axes (README section 4):
  1. What the player sees: nothing / subject only / progressive story
  2. How they answer: free text / word bank
  3. When the reveal happens: at the end / progressively
- Word collection and template assembly belong to the **engine**, not the mode.
  A mode only configures the axes. If adding a mode means touching assembly or
  collection, the abstraction has leaked - fix the abstraction.
- **Foundation-first, one axis per mode.** Story 03 is the single foundation
  that makes `FillBlank.tsx` and `Reveal.tsx` mode-aware via three OPTIONAL,
  purely-additive slots (`seeContext`, `answerSurface`, `revealPresentation`
  a.k.a. `ModeSurfaces`, see `03-mode-aware-surfaces.md`). It is the ONE story
  permitted to edit those two files. Every mode after it (04, 05, 06) is a new
  `ModeConfig` value plus exactly ONE plug-in surface component, each in its
  OWN file - none of them touch `FillBlank.tsx`, `Reveal.tsx`, or the engine.
  That file-disjointness is deliberate: it is what lets 04/05/06 build in
  parallel with no coordination once 03 lands. If building a mode ever forces
  a change to 03's files or to `web/src/engine/`, that is an abstraction leak
  - flag it, do not patch around it.
- **One axis per mode, proven cleanly.** Rather than combining axes in a
  single look-ahead pass, this slice ships exactly one mode per unbuilt axis
  value: Word Bank (04) proves `answer: 'word-bank'` alone (see/reveal stay
  Classic-blind-shaped); Progressive Story (05) proves `see:
  'progressive-story'` alone (`reveal` stays `'at-end'`); Progressive Reveal
  (06) proves `reveal: 'progressively'` alone (`see` stays `'subject-only'`).
  Combining two or three axes in one mode (e.g. a word-bank progressive story)
  is explicitly deferred until each axis is independently proven - see each
  story's Out of Scope.
- This is what keeps every later mode (and every later combination of axes)
  days of work instead of weeks - the architecture bet paid off exactly as
  designed once 03's slots exist.

## Parked - Phase 2+/3
- Owner-curated word bank (README section 5's "the round's host supplies the
  word bank everyone draws from") - builds on Word Bank (04)'s rendering with
  a host-authoring step and a round-scoped bank source, but that authoring
  step is inherently group-shaped (there is no "other players" to curate for
  solo) and needs a live roster + round-start broadcast to distribute the
  host's words. Heavier than a thin axis-proving story; parked until
  group-play's round-start chain is in place. (Was Issue #54 in the prior
  look-ahead pass.)
- Versus / Duel mode (two-plus players answering the SAME blank, then a
  room-wide vote on the funniest answer) - the one mode that is honestly a
  **stretch** of the engine, not thin config: the three axes do not express
  "how many players answer one blank" or "a vote happens after the reveal," so
  building it means generalizing `engine.ts`'s collection model
  (many-answers-per-blank) and adding a reusable vote-collection primitive
  shared with `reveal-delight/03` (Golden Guardian). That is real engine work,
  not a same-shape-as-Classic-blind config story - parked until this slice's
  three thin, disjoint modes have landed and proven the foundation, so the
  engine generalization is undertaken deliberately rather than folded into a
  "just another mode" pass. (Was Issue #55 in the prior look-ahead pass.)
- More game modes beyond the five named in README section 5 (the axes are
  designed to keep adding modes cheap; new ones are proposed and slotted in as
  they come up, not designed speculatively here).
- Per-player mode selection within a single round (README section 5 assumes one
  mode per round, chosen at round start).
- AI-personalized spark chips / word banks generated per PLAYER (template-model
  Phase 2 territory, not a mode concern). Note: the by-CATEGORY "jumble" (story
  07) is the nearer, non-personalized cousin of this - it swaps in a fresh set of
  options (deterministically from the curated pool for free, or AI-generated on
  theme as a gated delight), the SAME set for whoever is on that blank; the
  parked idea here is the further, per-player personalization step.

## Decisions
- 2026-07-01: **Re-planned this feature** as a tight, foundation-first, 3-mode
  slice - superseding the prior 2026-07-01 look-ahead pass's 6-mode plan
  (stories 03-06: Progressive reveal, Blind + word bank, Owner-curated word
  bank, Versus/Duel). The old plan front-loaded group-play-shaped modes and one
  genuine engine stretch (Versus) before the shared screens were even
  mode-aware. The new plan adds one foundation story (03, makes FillBlank +
  Reveal mode-aware via optional slots) and then ships exactly one thin mode
  per remaining unbuilt axis value - Word Bank (04, answer axis), Progressive
  Story (05, see axis), Progressive Reveal (06, reveal axis) - each file-
  disjoint from the others and from the foundation, so 04/05/06 can build in
  parallel. Owner-curated word bank and Versus/Duel are parked (see "Parked -
  Phase 2+/3" above) rather than dropped: both remain README section 5
  commitments, just correctly sequenced behind this slice and, for Versus,
  behind a deliberate engine-generalization decision rather than folded into a
  same-shape-as-Classic-blind pass.
- 2026-07-01: Old story slugs `03-progressive-reveal.md`, `04-blind-word-
  bank.md`, `05-owner-curated-word-bank.md`, `06-versus-duel.md` are removed;
  the new `03-mode-aware-surfaces.md`, `04-word-bank.md`,
  `05-progressive-story.md`, `06-progressive-reveal.md` replace them under
  different slugs (03 is a new foundation story with no prior equivalent; 04
  keeps Issue #53 since it is the same "answer axis, word bank" idea reframed
  to the new footprint; 06 keeps Issue #52 since it is the same "reveal axis,
  progressive" idea narrowed to reveal-only, no longer bundled with the see
  axis).
