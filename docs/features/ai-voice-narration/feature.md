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
| 02 | TBD | Character voice picker (pirate / robot / wizard / Guardian) | Not Started |
| 03 | TBD | Wire the reserved narration bar (play/pause, waveform) | Not Started |
| 04 | TBD | Safety: only vetted, family-safe text is spoken | Not Started |
| 05 | TBD | Gate behind the ai.voice entitlement | Not Started |

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
- **Character voices ARE the delight**, not neutral TTS: a small curated set
  (pirate, robot, wizard, a warm Guardian narrator), family-safe by construction.
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
