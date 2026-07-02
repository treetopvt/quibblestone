<!--
  First filed story in an otherwise-sketch feature: surfaced from real play (the narrator should be
  swappable mid-reveal, and the voice set is the delight). Filed ahead of full Phase-3 decomposition
  because the idea is concrete; the rest of ai-voice-narration stays sketch until its phase (see feature.md).
  Use hyphens/colons/parentheses, never em dashes.
-->

# Story: Character voice picker (the player chooses and changes the narrator's voice)

**Feature:** AI Voice Narration  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** TBD

## Context
A single flat TTS voice is a novelty; a narrator the player can DRESS UP - and swap
on a whim - is the car-ride killer feature (README section 2: "the phone reads the
story in a pirate voice"). Like a real storyteller doing different characters, the
reveal should be readable in a set of distinct voices - a woman's voice, a man's
voice, a silly voice, a scary voice, a robot voice, a kid's voice, and character
personas (pirate, wizard, a warm Guardian narrator) - and the player should be able
to CHANGE the voice and hear the same tale again, no re-play of the round needed.
This story owns the picker UI and the re-narrate-on-change behavior. It sits on top
of story 01 (which generates + plays the narration for a given voice) and lights up
inside the narration bar story 03 wires (the bar `the-reveal/01` already reserved).
The exact voice roster is a design/brainstorm item - this story specifies the
mechanic and a starting set, not a frozen catalog. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given a reveal with narration available (story 01), then a voice picker
      is offered on the Reveal screen (within/adjacent to the narration bar from story
      03) presenting a small, curated set of named voices - each with a clear,
      kid-legible label + FontAwesome glyph, big tap targets - so a player can see and
      choose the narrator.
- [ ] AC-02: Given a starting voice roster, then it spans both GENDER-flavored voices
      (a woman's voice, a man's voice, a kid's voice) and CHARACTER/effect voices (at
      minimum: silly, scary, robot, plus persona voices - pirate, wizard, a warm
      Guardian narrator). The exact final set is a design brainstorm (see feature.md
      Design notes / Decisions) - this AC fixes the SHAPE (both real-ish voices and
      playful character voices), not a locked list, and the set is data-driven (adding
      a voice is config, not a code fork), consistent with the "one engine, many thin
      modes" ethos (CLAUDE.md section 2).
- [ ] AC-03: Given a chosen voice, when the player picks a DIFFERENT voice, then the
      tale re-narrates in the new voice on demand (the same already-assembled, already-
      filtered story text; the round is never replayed and no words are re-collected) -
      changing the narrator is a cheap, instant-feeling swap, the way a storyteller
      switches characters.
- [ ] AC-04: Given the car-ride design center (one HOST device narrating to the group,
      README section 1), then the voice choice applies to the shared narration everyone
      hears; changing it is a host-level control on that device, not per-listener audio
      (per-player device audio stays Parked in feature.md). In solo play the single
      player is that device.
- [ ] AC-05 (latency / caching): Given a `(tale, voice)` pair, then its audio is cached
      (Blob per feature.md) so re-selecting a previously-heard voice is instant and a
      first-time voice shows a short "conjuring the voice..." state - changing voices
      never blocks or stalls the reveal's laugh (feature.md Design notes).
- [ ] AC-06 (child-safety, non-negotiable): Given any offered voice, then every voice
      is family-safe by construction, and only already-filtered text is ever spoken
      (README section 6 - the same vetted `AssembledStory` the reveal shows; the picker
      opens NO new free-text path). Given the family-safe toggle is on, then the offered
      voice set honors it (e.g. a "scary" voice may be softened or withheld) - the
      toggle gates the roster, decided with the session, not per playback request.
- [ ] AC-07 (entitlement): Given voice narration is a premium delight, then the whole
      capability (narration + this picker) is gated by the `ai.voice` entitlement key
      checked ONCE at session-creation (billing-entitlements seam; the gate itself is
      owned by `ai-voice-narration/05`). This story adds NO per-request or per-voice
      entitlement check - if the session has `ai.voice`, the full offered roster is
      available; if not, the picker is not shown at all (the reveal is silent / host-
      read, README section 3). Individual voices are not separately micro-monetized here.

## Out of Scope
- Generating/playing the audio itself and the play/pause transport - that is story 01
  (narrate the tale) and story 03 (wire the reserved narration bar); this story only
  chooses/changes WHICH voice they use.
- The entitlement GATE implementation - `ai-voice-narration/05` owns the `ai.voice`
  session-creation check; this story consumes its result.
- Custom or cloned voices, and any voiceprint of a player - explicitly REJECTED on
  child-safety/privacy grounds (feature.md Parked - no voiceprints of minors, ever).
- Per-blank / real-time per-word voices (narrate as each word carves in) - Parked.
- Musical / sung narration - Parked.
- Per-player device audio sync (a whole car on their own phones) - Parked.
- Finalizing the exact voice catalog - the roster is a live design brainstorm; this
  story ships a starting set and the data-driven seam to grow it.

## Technical Notes
- **Sits on the reserved narration bar.** `the-reveal/01` already renders the narration
  bar (play/pause FAB, waveform, label) inactive; story 03 wires it; this story adds the
  voice-selection affordance in/near it with NO new layout upheaval (feature.md's
  load-bearing "no layout change" constraint).
- **Voice roster is data.** Model voices as a small typed list (id, display label,
  FontAwesome glyph, provider voice/style params, family-safe-only flag) so adding a
  voice is config. This is the "engine is config, not a fork" bet applied to voices.
- **Re-narrate = re-fetch/replay for a new `(tale, voice)` key**, reusing story 01's
  generation + story 03's transport; cache per `(tale, voice)` in Blob (feature.md) so
  swaps are cheap. Generation stays async - never block the reveal (AC-05).
- **Safety + entitlement are decided with the session, not per tap** (AC-06, AC-07):
  the family-safe toggle filters the roster; `ai.voice` (checked at session-creation by
  story 05) gates whether the picker exists at all. Do not sprinkle per-request checks.
- **No PII, no new free text.** Only vetted template text + already-filtered coral words
  are ever sent to TTS; the picker collects nothing about the player.
- FontAwesome only for voice glyphs (registered in `web/src/fontawesome.ts`); all
  styling from `web/src/theme.ts`; big tap targets; no i18n; no em dashes.
- **Provider note:** if/when a TTS provider and any LLM-side voice-styling is chosen,
  the provider key lives in Key Vault (feature.md / README section 4) - never in a
  `VITE_*` var. (Check the `claude-api` skill before wiring any Anthropic-side call.)

## Tests
<!-- No harness runs against unbuilt Phase-3 work; these are the intended tests + the manual check for now. -->
| AC | Test |
|---|---|
| AC-01 | manual: the Reveal narration bar shows a voice picker with labeled, tappable voice options |
| AC-02 | unit: the voice roster is a data list spanning gender-flavored + character/effect voices; adding one is config, not a fork |
| AC-03 | manual: picking a different voice re-narrates the same tale in the new voice without replaying the round |
| AC-04 | manual (group): the voice choice drives the shared host-device narration everyone hears |
| AC-05 | manual: re-selecting a heard voice is instant (cache hit); a first-time voice shows a brief "conjuring" state and never blocks |
| AC-06 | code review + manual: all voices family-safe; family-safe toggle filters the roster; only vetted text is spoken; no new text input |
| AC-07 | code review: `ai.voice` gates the whole picker at session-creation; no per-voice entitlement check; no per-voice micro-charge |

## Dependencies
- ai-voice-narration/01 - Narrate the assembled tale with TTS (generation + playback this picks the voice for)
- ai-voice-narration/03 - Wire the reserved narration bar (the surface the picker lives in)
- ai-voice-narration/05 - Gate behind the `ai.voice` entitlement (the session-creation gate this consumes)
- the-reveal/01-text-reveal (the assembled, already-filtered tale + the reserved narration bar)
- child-safety/02-family-safe-toggle (the toggle that filters the offered voice roster)
- billing-entitlements (the `ai.voice` capability key checked at session-creation)
