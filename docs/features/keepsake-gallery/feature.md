# Feature: Keepsake Gallery

## Summary
The share/growth loop and a keepsake delight in one feature: turn a finished
tale into a shareable stone-tablet image, put a light watermark on it so every
share is a growth touch, add a real back-link (a public, read-only "tale" page
with a "Play QuibbleStone" CTA) so a shared tale converts a viewer into a new
player, and keep a small device-local gallery of past tales so a family can
revisit their funniest ones.

## README reference
README section 2 (Market Positioning - the differentiator "gets us noticed";
word-of-mouth is the growth lever this feature exists to serve) and section 7
(Epic Map - Phase 4, "social sharing & saved-story keepsakes"). Builds on
`the-reveal` (the screen this feature exports an image of) and
`session-engine/04` (the existing Web Share / copy approach this feature
mirrors for sharing the image).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #63 | Save the reveal as a stone-tablet image | Complete |
| 02 | #64 | Share the tale with watermark | In Progress |
| 03 | #65 | "Tales we've carved" local history | Not Started |
| 04 | #66 | Shareable tale link (the back-link growth loop) | Not Started |

## Dependencies
- the-reveal (the assembled story + stone-tablet rendering this feature
  captures as an image).
- session-engine (the `Player`/`RoomState` shape used for the byline names on
  the image, and the Web Share approach story 02 mirrors).
- child-safety (no unfiltered content, no PII, ever reaches an exported or
  saved image, or a published tale page).
- infra (story 04's public tale page stores the published tale in Azure Table
  Storage - README section 9; stories 01-03 need no backend).

## Design notes
- This feature does **not** touch the engine. It is entirely presentation
  (rendering the already-assembled, already-filtered story to an image) and
  device-local storage (remembering which images were rendered). If any story
  here needs a change to `assemble()`, `collectWord()`, or the reveal's
  rendering data (`AssembledStory`, `Template`), that is scope creep into
  `the-reveal`, not this feature. (Stories 01-03 are also entirely client-side;
  story 04, the shareable tale link, is the one deliberate exception that adds a
  small server surface - see its own bullet and the Decisions log below.)
- Story 01's default approach is a **client-side** canvas/DOM-to-image render
  of the existing Reveal tablet (no server round-trip, no new backend
  surface). A server-side render (e.g. a headless render service) is noted as
  the alternative if client-side fidelity is not good enough once tried -
  this is a build-time decision, not something to pre-build both paths for.
  Do not add a new heavy client dependency for this without flagging it in
  the story's Decisions/Technical Notes first (bundle size matters on a PWA).
- Story 02 mirrors `session-engine/04`'s Web-Share-first, clipboard-fallback
  approach (feature-detect `navigator.share`, do not gate on
  `navigator.canShare()` for a payload that is a plain image/text). The
  watermark is the growth mechanic: every image that leaves the app carries
  "carved with QuibbleStone" so a share is also a soft advertisement, without
  ever resorting to actual ads (README section 3 - "avoid ads").
- Story 03 gives a permanent home to an idea that was explicitly parked
  earlier: `session-engine/feature.md`'s "Parked - Phase 2+" already lists
  "Tales we've carved local history (design pack Expansion 5)" - this feature
  is where that idea now lives and gets specified. It stays device-local
  (`localStorage`/`IndexedDB`), anonymous, and account-free, consistent with
  README section 3's identity model (players never get an account; only a
  purchaser does, and only when they buy).
- Story 04 (shareable tale link) is the ONE part of this feature that adds a
  small SERVER surface - a public, read-only tale page plus a stored published
  tale - because a real back-link cannot be a purely client-side artifact.
  Stories 01-03 stay client-only; only 04 touches the server, kept isolated and
  behind strict guardrails: family-safe content only, no PII (nickname +
  Guardian only), an unguessable + noindex link, host-initiated (opt-in)
  publishing, and a TTL so published tales expire. Sharing a link is FREE - it
  is the growth loop, not a gated feature (README section 3).
- All four stories stay anonymous by construction: the only identity on a
  saved or shared image, or a published tale page, is an in-session nickname +
  Guardian variant, exactly what the roster already displays - never a real
  name, device id, or any other PII.

## Parked - Phase 2+
- Server-side render pipeline for the image (only if client-side canvas/DOM
  fidelity proves insufficient after story 01 ships - see Design notes).
- Cloud-synced keepsake gallery tied to a purchaser account (once accounts
  exist per README section 7 Phase 2, a paying "family plan" purchaser could
  sync their gallery across devices; anonymous players still never get one).
- Reaction counts or comments attached to a saved tale (the Reveal's Phase-4
  reaction row is `the-reveal/feature.md`'s own parked idea; if it ships, a
  saved tale could show its final counts, but that is additive scope for
  later, not this feature).
- Exporting the local gallery as a printable "yearbook" of tales (a nice idea,
  needs its own design pass).
- Search/filter within the local gallery beyond a simple recency list.

## Decisions
- 2026-07-01: Story 03 (local history) is scoped as a consumer of story 01's
  saved image, not a re-render of the live `AssembledStory` on every gallery
  view - once an image exists, the gallery stores/displays that image plus a
  small metadata record (title, date, byline names), never the raw story
  data. Why: keeps story 03 disjoint from `the-reveal`'s rendering data model
  and from the engine entirely - it is a pure "list of saved images" feature.
- 2026-07-01: Story 01 is the sizing dependency for both 02 (needs the image
  to share) and 03 (needs the image to save) - it lands first in the Wave
  Plan, and 02/03 can then run in parallel since they touch different files
  (02 touches the Reveal share action; 03 touches new gallery storage +
  screen).
- 2026-07-01: Added story 04 (shareable tale link) after review flagged that
  the existing share (story 02) only exports a watermarked image - a brand
  impression, but a dead end for adoption. A watermark is a soft advert; a
  back-link is a growth LOOP. Story 04 closes it with a public read-only tale
  page + a "Play QuibbleStone" CTA. Recorded that this deliberately introduces
  the feature's only server surface (the exception to an otherwise client-only
  feature), kept isolated and behind family-safe / no-PII / unguessable /
  noindex / opt-in / TTL guardrails, and that the link is FREE (never gated) -
  gating the growth loop would defeat its purpose.
