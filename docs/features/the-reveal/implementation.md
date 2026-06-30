<!--
  Implementation plan for the the-reveal feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: The Reveal

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

The payoff moment (README section 10 - "deserves the most love"), text-only for Slice 1. One story: render the
deterministic assembly from `template-model` with player words highlighted coral. It is a **web-only** screen
(`Reveal.tsx`) consuming the engine output - it does **not** own the group-play reveal broadcast (that hub message
is owned by `group-play/03`, which transitions the room to this screen). So it is disjoint from `GameHub.cs` and runs
after `game-modes/02`.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Assembled story (deterministic) | `assemble()` + the attributed result (**template-model/01**) | `web/src/engine/assemble.ts` |
| Collected words / mode output | the engine output (**game-modes/02**) | `web/src/engine/engine.ts` |
| Styling / theme tokens (stone-tablet shape, glow) | the MUI theme (**design-system/01**) | `web/src/theme.ts` |
| Shared UI contracts | `AppBar`, gold-CTA + outlined-purple Button, `BottomActionBar` (**design-system/01**) | `web/src/components/` |
| Real-time receive (group reveal arrives as a hub event) | the one SignalR hook - handler added by **group-play/03** | `web/src/signalr/useGameHub.ts` |
| Web Share (Share the tale) | the same approach as **session-engine/04** | `web/src/pages/Lobby.tsx` (reference) |
| Child safety | names/words already filtered upstream (join + submission) | `api/src/Safety/` |

What this feature **exports** that others import:
- The **Reveal screen** (`Reveal.tsx`) - reused by `single-player/01` (solo, local assembly) and `group-play/04`
  (the "Play another round" CTA triggers the replay flow).
- Optionally a shared **`TabletCard`** stone-tablet panel (see gotchas).

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 text-reveal | #34 | `web/src/pages/Reveal.tsx`, optional `web/src/components/TabletCard.tsx` | template-model/01, game-modes/02, child-safety/01, design-system/01 | session-engine tail (disjoint) | 1 | medium |

**Concurrency per wave:** 1 (single story). It can run as soon as `game-modes/02` lands, in parallel with the
remaining `session-engine` stories (no shared files).

## Per-story tech notes

### 01 - Text reveal
- **Approach:** render the **deterministic assembly** from `template-model` inside a glowing stone-tablet scroll
  panel: confetti, "Your tale is carved!" header, a "carved by [names] & crew" byline parsed from room state, and
  the story body (AC-01). Each **filled-in word** is wrapped in a `<span>` styled coral (`#FF6B57`, weight 800,
  coral underline) against Nunito 600 body text so the player words pop (AC-02). In group play, the assembled story
  arrives over SignalR so every player sees the same reveal without refreshing (AC-03). Pinned bottom action bar:
  gold "Play another round" (triggers `group-play/04`'s replay flow) + outlined-purple "Share the tale"
  (Web Share, same approach as `session-engine/04`, graceful fallback) - the scroll area never hides behind the bar
  (AC-06). The narration bar (play/pause, waveform, label) is **rendered but inactive** in Slice 1 - real estate
  reserved so Phase 3 wires TTS with no layout change (AC-07).
- **Owns / exports:** `Reveal.tsx`. If a shared stone-tablet component is extracted, this story owns `TabletCard.tsx`.
- **Gotchas:** the **coral highlight is a content-level style, not chrome** - apply it via `sx`/class, do **not** add
  it to the theme. The stone-tablet shape/glow **does** come from theme shape tokens. Confetti is **CSS-only** (8
  pieces, palette colors, fall+spin `@keyframes` 2.6-3.4s) - no canvas library. Every word shown has already passed
  the filter upstream (AC-04); the byline names passed the filter at join time. Out of scope (parked): TTS,
  word-by-word carving animation, AI illustration, image/keepsake export, the Phase-4 reaction row.

## Cross-cutting concerns

- **Reveal renders, it does not collect or broadcast.** The screen consumes the engine's assembly; the **group-play
  reveal broadcast** (server determines all blanks in -> sends the assembled story to the room group) is owned by
  `group-play/03-collect-words`, which then routes the room to this screen. Keeping that split is what keeps
  `Reveal.tsx` disjoint from `GameHub.cs`.
- **Inter-feature ordering (prerequisites):** `template-model/01` (assembly), `game-modes/02` (the collected words +
  Classic blind), `design-system/01` (theme + Button + BottomActionBar), `child-safety/01`. This story must land
  before `single-player/01` and `group-play/04` (both reuse the Reveal screen).
- **Stone-tablet reuse:** the stone-tablet panel also appears on Home / Join / FillBlank (built earlier via theme
  tokens + local `sx`). For Slice 1 each screen applies the styling from theme tokens; if a shared `TabletCard`
  emerges, this feature is its natural home and earlier screens can adopt it later - avoid creating a shared
  component file that two in-flight stories would both edit.
- **Grows later without re-architecting:** voices, images, and share/keepsake bolt on without changing the text
  reveal. **No i18n** (plain strings). **No em dashes**.
