<!--
  SKETCH (vision-level) - README Phase 3 delight tier. feature.md only: no story files or implementation.md yet.
  This folder is decomposed into stories when its phase comes up (docs/features/README.md stub convention).
  Use hyphens/colons/parentheses, never em dashes.
-->

# Feature: AI Voice Narration

## Summary
TTS character voices read the finished tale aloud - pirate, robot, wizard, a warm
Guardian narrator. This is the car-ride killer feature (README section 2: "the
car-ride 'phone reads the story in a pirate voice'"): one device narrating to a
whole car of laughing kids. The Reveal's narration bar, already rendered but
inactive in `the-reveal/01`, comes alive here **with no layout change**. A
premium delight, gated at session-creation; free reveals are silent / host-read.
This file is a **sketch** (vision-level): feature.md only - stories and
implementation.md come when Phase 3 is decomposed.

## README reference
README section 2 ("AI illustrations + character-voice narration ... the delight
features that earn word-of-mouth (esp. the car-ride ...)"), section 7 (Epic Map -
Phase 3, "AI Voice Narration (L) - TTS character voices (the car-ride killer
feature)"), `docs/features/the-reveal/feature.md` (the narration bar hooks
deliberately reserved in Slice 1), section 4 (Blob for generated audio; Key Vault
for the provider key), section 6 (only vetted text is ever spoken).

## Stories (sketch - to be filed when Phase 3 is decomposed)
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | TBD | Narrate the assembled tale with TTS | Not Started |
| 02 | TBD | Character voice picker - player chooses AND changes the narrator's voice (**filed ahead:** [`02-character-voice-picker.md`](./02-character-voice-picker.md)) | Not Started |
| 03 | TBD | Wire the reserved narration bar (play/pause, waveform) | Not Started |
| 04 | TBD | Safety: only vetted, family-safe text is spoken | Not Started |
| 05 | TBD | Gate behind the ai.voice entitlement | Not Started |

> Note: this feature is still a **sketch** (no `implementation.md`; the rest of the
> stories are decomposed when Phase 3 comes up). Story 02 is the one exception - it
> was filed as a full story file ahead of that pass because play surfaced a concrete
> want: the narrator should be swappable on demand (a woman's / man's / silly / scary /
> robot / kid's voice, plus persona voices), the way a storyteller does characters.

## Dependencies
- the-reveal (the assembled tale text, and the narration bar hooks
  `the-reveal/01` already reserved - this feature wires them up, it does not
  add new real estate).
- billing-entitlements (the `ai.voice` capability key, checked at
  session-creation - the seam already reserves it).
- child-safety (only already-filtered text is ever sent to TTS).
- infra (Blob Storage for generated audio; Key Vault for the provider key).

## Design notes
- **No layout change - that was the point of `the-reveal/01`'s foresight.** The
  narration bar (play/pause FAB, waveform, label) is already on the Reveal
  screen, inactive. This feature wires the hooks; Phase 3 adds no new layout.
- **Car-ride use case is the design center.** The HOST's phone narrates to the
  group (README section 1); design for one device playing aloud to a car, not
  per-player audio.
- **Latency + caching.** Generate audio async and cache per `(tale, voice)` in
  Blob; a short "conjuring the voice..." state is fine, but never block the
  reveal's laugh.
- **Character voices ARE the delight**, not neutral TTS: a curated set that spans
  both GENDER-flavored voices (a woman's, a man's, a kid's voice) AND
  CHARACTER/effect voices (silly, scary, robot, plus personas - pirate, wizard, a
  warm Guardian narrator). The exact roster is a live design brainstorm (story 02
  ships a starting set + the data-driven seam to grow it), family-safe by
  construction. Model voices as DATA (id, label, glyph, provider params,
  family-safe flag) so adding one is config, not a code fork - the "one engine,
  many thin modes" bet (CLAUDE.md section 2) applied to voices.
- **The player can CHANGE the voice, not just pick one once** (story 02): swapping
  the narrator re-narrates the SAME already-assembled tale on demand (the round is
  never replayed), the way a storyteller switches characters mid-story. This
  changeability is the core of the delight, so `(tale, voice)` audio is cached
  (Blob) to keep swaps instant.
- **Safety.** Only already-filtered coral words + vetted template text are ever
  spoken (README section 6); no unfiltered text reaches TTS. The family-safe
  toggle may limit which voice styles are offered.
- **Accessibility bonus.** Read-aloud also helps pre-readers and low-vision
  players join in - a genuine inclusive side effect, though not the primary sell.
- **Reduced-motion.** The waveform animation respects `prefers-reduced-motion`.
- **Entitlement at session-creation** via the billing-entitlements seam
  (`ai.voice`); free/base reveals are silent or host-read. No ads, kid-safe.

## Parked - Phase 4+
- Real-time or per-blank voices (narrate as each word carves in).
- Custom or cloned voices - explicitly rejected on child-safety and privacy
  grounds (no voiceprints of minors, ever).
- Musical / sung narration.
- Per-player device audio sync (a whole car hearing it on their own phones).

## Decisions
- 2026-07-01: Sketched as a vision-level feature.md only (Phase 3) during the
  look-ahead pass; stories + implementation.md deferred to phase decomposition
  per the stub convention. Recorded two load-bearing constraints so a future
  builder starts correctly: (1) this feature MUST wire the narration bar that
  `the-reveal/01` already reserved rather than introduce new layout, and (2)
  generated audio uses the provisioned Blob resource with per-`(tale, voice)`
  caching, and only already-filtered text is ever spoken.
- 2026-07-02: Filed story 02 (character voice picker) as a full story file ahead
  of the rest of the phase decomposition, because play surfaced a concrete want:
  the narrator should be player-CHANGEABLE (swap the voice, re-hear the same tale),
  and the voice set should span gender-flavored voices (woman / man / kid) as well
  as character/effect voices (silly / scary / robot / pirate / wizard / Guardian).
  Recorded that the voice roster is a live brainstorm shipped as DATA (adding a
  voice is config), that changeability re-narrates without replaying the round, and
  that safety (family-safe toggle filters the roster) and entitlement (`ai.voice`
  at session-creation, gate owned by story 05) are decided with the session, never
  per playback. The other stories (01, 03, 04, 05) remain sketch until Phase 3;
  no `implementation.md` is created yet (the feature is still a sketch overall).
