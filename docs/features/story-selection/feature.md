# Feature: Story Selection & Freshness

## Summary
How the game decides WHICH story you get: match story length to the moment
(a quick 5-blank tale vs a full 10-blank epic), never deal the same story
twice until the pool runs dry (unless you explicitly replay one), and quietly
record what was served and how players liked it - the minimal telemetry loop
that keeps the library feeling fresh and tells content creators which tales
actually land.

## README reference
README section 2 (the content library is the long-term moat - this feature is
the feedback loop that tunes it), section 8 ("additive on a thing that already
works" - selection today is a uniform random pick at two sites; this feature
upgrades the pick, never the engine), and section 9 (Storage is provisioned
but unused - the serve log and feedback counts are its first consumer).
Root README section 12 parks "analytics" as demand-driven: stories 04-05 here
are deliberately NOT that - they are a toy-grade serve log and a thumbs count,
the smallest data that keeps content fresh and curatable.

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #91 | Length classes + the one selection pipeline | Not Started |
| 02 | #92 | Quick story option (solo + group) | Not Started |
| 03 | #93 | Freshness rotation: no repeats until the pool runs dry | Not Started |
| 04 | #94 | Story delivery metrics (the anonymous serve log) | Not Started |
| 05 | #95 | Like / dislike a tale (content feedback) | Not Started |
| 06 | TBD | Favorite a story and replay it (device-local) | Not Started |

## Dependencies
- template-model (the seed library + tags this feature selects over; the
  server's TemplateCatalog mirror already carries BlankCount, which is the
  length signal).
- single-player (the solo template pick in `web/src/pages/Solo.tsx` this
  feature upgrades).
- group-play (the server-side pick in `GameHub.StartRound` this feature
  upgrades; the round lifecycle stories 04-05 hook into).
- child-safety (the family-safe gate stays the FIRST filter in the pipeline;
  nothing here may run before or around it).
- the-reveal (story 05 adds its thumbs to the Reveal / Round Complete surface).
- replay-remix (explicit replay must BYPASS freshness - see story 03; keep the
  two features' hub-method seams coordinated).
- infra: the provisioned-but-unused Storage account (README section 9) is the
  sink for stories 04-05.

## Design notes
- **One selection pipeline, two mirrored sites.** Selection already happens in
  two places kept in behavioral lockstep by hand: the web's pure
  `selectTemplates` gate (`web/src/content/familySafe.ts`, used by Solo) and
  the server's `FamilySafeContentSelector` + `Random.Shared` pick
  (`GameHub.StartRound`). This feature keeps that shape and extends the
  pipeline in both places to: family-safe gate -> length filter -> freshness
  filter -> uniform random. Each stage is a pure function (data in, data out),
  unit-tested on the web side as the reference spec, mirrored in C# - the
  exact discipline `distribute.ts` / `familySafe.ts` already established. If a
  story here needs the ENGINE (template shape, assemble, collect) to change,
  that is a leak - stop and flag it.
- **Length is derived, not authored.** A template's length class (quick /
  full) derives from its blank count (web: `getBlanks(t).length`; server: the
  catalog's existing `BlankCount`). No new hand-synced tag, no new drift
  surface. Thresholds live in ONE exported constant per side.
- **Freshness state matches the identity model.** There are no accounts
  (README section 3), so "don't repeat" is scoped to what we can actually
  know: device-local history for solo (localStorage, same posture as
  keepsake-gallery/03), room-lifetime history for group (on the in-memory
  Room record, ephemeral like everything else on it). Cross-device freshness
  is parked until accounts exist.
- **Telemetry is toy-grade and anonymous by construction.** The serve log and
  thumbs counts carry template id, timestamp, mode, length class, player
  count, and an ephemeral opaque session id - never nickname, join code, or
  anything traceable to a person (README section 6: minimal data on minors).
  Azure Table Storage, fire-and-forget, and a failure to log must NEVER block
  or fail a round. This is a mutable toy log, not an audit trail.
- **Like/dislike is template feedback, not reveal celebration.** The
  reveal-delight feature's Reaction row (reveal-delight/01) celebrates THIS
  telling with the room, tap-to-increment, party-style. Story 05 here is a
  different thing: one quiet per-player thumbs up/down on the TEMPLATE,
  recorded for content curation. Different purpose, different mechanic,
  different sink - do not conflate them, and do not let 05 grow emoji.

## Parked - Phase 2+
- Cross-device / cross-session freshness and "played history" tied to a
  purchaser account (needs accounts-identity; device-local + room-scoped is
  the honest scope today).
- A creator-facing analytics dashboard over the serve log and thumbs counts
  (stories 04-05 make the data exist; reading it starts as a dev-only query.
  A dashboard is demand-driven, per root README section 12).
- Feeding thumbs data into ai-content-factory's vet/publish loop as an
  automatic signal (the factory should read this data when it exists, but the
  wiring belongs to that feature).
- Weighted or personalized selection ("more space stories for this room") -
  selection stays uniform random over the filtered pool until real usage says
  otherwise.
- A player-facing "browse and pick a specific story" picker over the WHOLE
  library (today the game deals you a story; picking any one is a different
  product surface, related to story-packs). Note: story 06 (favorite a story)
  ships the lightweight PERSONAL cut of this - a device-local list of stories
  you have already played and starred, revisitable and replayable - but not the
  full catalog browser, which stays parked.

## Decisions
- 2026-07-01: Length class is DERIVED from blank count rather than authored as
  a new tag. Why: TemplateCatalog.cs is already a keep-in-sync-by-hand mirror
  of seedLibrary.ts; every new authored field doubles the drift surface, and
  blank count is already on both sides.
- 2026-07-01: Freshness explicitly yields to explicit replay. "Carve it again"
  (replay-remix/01) and solo "Play again" are the player DECIDING to repeat -
  the freshness filter applies only to the random pick, and replayed rounds do
  not re-stamp the freshness history (see story 03). Recorded here because two
  features touch the same seam.
- 2026-07-01: Telemetry (04) lands before feedback (05) and owns the storage
  plumbing; 05 reuses 04's sink. Why: one Table Storage integration, not two.
- 2026-07-02: Added story 06 (favorite a story) after play surfaced a kid
  wanting to revisit a few loved tales and replay them with new words. Scoped as
  device-local (localStorage, anonymous, account-free - the same posture as
  story 03's solo freshness history) and FREE. It is the trigger for the
  "replaying a favorite" case story 03 AC-04 already reserved (explicit replay
  bypasses freshness and does not re-stamp history), and it is deliberately the
  small personal cut of the parked full-library "browse and pick" picker - not a
  catalog browser. Kept distinct from like/dislike (05, an anonymous creator
  signal) and from keepsake-gallery/03 (revisit a finished RESULT, not replay
  the TEMPLATE).
