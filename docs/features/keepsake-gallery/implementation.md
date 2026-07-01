<!--
  Implementation plan for the keepsake-gallery feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Keepsake Gallery

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

The share/growth loop (README section 2) and a keepsake delight (README section 7, Phase 4) in one feature, built
entirely on top of `the-reveal`'s already-assembled, already-filtered story. Stories 01-03 are **web-only** - no
API/hub change - since rendering an image and storing it locally touches neither the engine nor real-time sync.
Story 04 (the shareable tale link) is the one exception: it adds a small, isolated **server** surface (a public
read-only tale route + a stored tale in Table Storage), kept well away from `GameHub.cs` and the round lifecycle.
Story 01 (the image render) is the foundation both 02 (share) and 03 (local history) consume, so it lands first; 02
and 03 are then file-disjoint and can run in parallel, and 04 follows 02 (it extends the same share action).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Assembled story + coral highlighting | `buildRevealParts()` (already computes interleaved text/coral-word parts) | `web/src/pages/revealParts.ts` |
| Reveal screen + attribution slot | the shared Reveal view and its `attribution` prop (**the-reveal/01**) | `web/src/pages/Reveal.tsx` |
| Web Share pattern (feature-detect, no `canShare()` gate, clipboard fallback) | the existing approach (**session-engine/04**, already reused by **the-reveal/01**'s `handleShare`) | `web/src/pages/Lobby.tsx`, `web/src/pages/Reveal.tsx` |
| Styling / theme tokens (stone-tablet gradient, coral, Fredoka/Nunito) | the MUI theme | `web/src/theme.ts` |
| Shared UI contracts | `AppBar`, `Button` (gold/outlined-purple), `BottomActionBar` | `web/src/components/` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Child safety | content already filtered upstream (join + submission); no new filter needed | `api/src/Safety/` (upstream only) |
| Config | `import.meta.env` (`VITE_*`) - not expected to be needed (no server calls in this feature) | `web/src/vite-env.d.ts` |

What this feature **exports** that others might import later: the saved-image render function (story 01) and the
local-gallery storage module `web/src/gallery/localGallery.ts` (story 03) are the natural seams a later
accounts/cloud-sync feature (parked, Phase 2+) would build on.

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. Story 01 is the foundation
every other story's tests and UI depend on (both need a rendered image to share/store), so it runs alone in Wave 1.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 (foundation) save-reveal-as-image | #63 | `web/src/gallery/renderTablet.ts` (or similar - the client-side render function), edits `web/src/pages/Reveal.tsx` (adds the "Save as image" action) | the-reveal/01 | - | 1 | high |
| 02 share-with-watermark | #64 | edits `web/src/gallery/renderTablet.ts` (watermark applied at render time), edits `web/src/pages/Reveal.tsx`'s existing `handleShare` (prefers an image payload, keeps the existing text fallback) | 01, session-engine/04 (pattern reuse) | 03 | 2 | medium |
| 03 tales-weve-carved-history | #65 | `web/src/gallery/localGallery.ts` (storage module), `web/src/pages/Gallery.tsx` (new screen), edits `web/src/pages/Home.tsx` (nav entry point) | 01 | 02 | 2 | medium |
| 04 shareable-tale-link | #66 | new `api/src/PublishedTales/` (thin controller + service), a public read-only tale page/route, edits `web/src/pages/Reveal.tsx`'s `handleShare` (adds the link to the payload) | 01, 02 | - | 3 | high |

**Concurrency per wave:** Wave 1 = 1 (foundation - the render function both 02 and 03 need). Wave 2 = {02, 03} in
parallel: 02 edits the render function's watermark step and `Reveal.tsx`'s existing share handler; 03 adds an
entirely new storage module and a new screen plus one small nav edit to `Home.tsx` - disjoint from 02's files except
both conceptually depend on 01's output, which is why 01 must land first. Wave 3 = 04 (shareable tale link) after 02,
since it extends the same `handleShare` payload 02 builds and adds the server surface the earlier stories
deliberately avoid.

## Per-story tech notes

### 01 - Save the reveal as a stone-tablet image
**Approach:** client-side canvas/DOM-to-image render of the Reveal tablet, reusing `buildRevealParts()`
(`web/src/pages/revealParts.ts`) for the same text/coral-word interleaving the live screen already renders, and the
same `attribution` content already passed into `Reveal` (group play's byline, solo's summary) - never a second byline
format. Target under ~2s render time on a mid-range mobile device, with a loading state if it runs long. Evaluate
bundle-size cost of any DOM-to-image library before adding it (this is a PWA - flag a new heavy dependency rather than
silently landing it); a hand-built canvas render using the same theme tokens (`theme.palette.tablet.gradient`,
`theme.palette.coral.main`) is the safer default if a library's cost is not clearly worth it.
**Owns / exports:** the render function - story 02 extends it with a watermark step; story 03 consumes its output as
the thing it stores. **Gotchas:** no server round-trip (AC-06) - if client-side fidelity proves insufficient, record
that decision here (this story's Technical Notes) rather than silently degrading the image or silently adding a
server call. Out of scope: sharing (02), persistent gallery (03), animation in the saved image, editing/annotating.

### 02 - Share the tale with watermark
**Approach:** extends the render function from 01 with a watermark step ("carved with QuibbleStone", small, legible,
placed so it never obscures story text or coral words) applied as part of the same render pass - one rendering code
path, not a separate post-process. Extends `Reveal.tsx`'s EXISTING `handleShare` (already implements the
feature-detect / `AbortError`-swallow / clipboard-fallback pattern from `session-engine/04`) to prefer an image
payload (`navigator.share({ files: [...] })`, feature-detecting file-share support specifically) while keeping the
existing text-only share as the fallback - never removing or forking that existing path.
**Owns / exports:** the watermark step, the extended `handleShare`. **Gotchas:** do not gate on
`navigator.canShare()` (same caution as `session-engine/04`'s existing note - it can spuriously reject a valid
payload). No new PII surface, no new analytics. Out of scope: share analytics, referral/rewards, per-user watermark
customization, direct social-platform APIs.

### 03 - "Tales we've carved" local history
**Approach:** a small storage module `web/src/gallery/localGallery.ts` behind which ALL raw `indexedDB`/`localStorage`
calls live (never scattered through the gallery screen) - `IndexedDB` for image blobs (localStorage's ~5-10MB,
string-only quota is a poor fit for multiple images), `localStorage` optionally for the ordered metadata list if a
hybrid split is chosen. A concrete cap/eviction policy (a maximum count, e.g. 20-30 tales, or a maximum storage
footprint, oldest-first eviction) must be chosen and the concrete numbers recorded in the story before/during
implementation - do not ship with an unspecified cap. A new `Gallery.tsx` screen (grid/list of saved tales, reusing
existing stone-tablet card styling) plus one small nav-entry edit in `Home.tsx`.
**Owns / exports:** `localGallery.ts` (a future accounts/cloud-sync feature's natural seam) and `Gallery.tsx`.
**Gotchas:** per feature.md's Decisions log, this story stores and displays the rendered IMAGE (story 01's output)
plus small metadata (title, date, byline names) - it never re-runs `assemble()` against stored engine data, which
keeps it disjoint from `the-reveal` and the engine. Clearing browser storage silently empties the gallery - this is
expected, documented behavior for device-local, account-free storage, not a bug to work around. Out of scope: cloud
sync, search/filter, per-item delete (a reasonable small follow-up, not core), export-as-collection.

### 04 - Shareable tale link
**Approach:** the one server-touching story. A thin, dedicated `api/src/PublishedTales/` controller + service exposes
a public read-only `GET /t/<slug>` that renders the carved tablet from a stored, already-filtered `AssembledStory`
(text + byline metadata) with a gold "Play QuibbleStone" CTA; publishing is a host-initiated action that stores the
tale in Azure Table Storage under an unguessable slug with a TTL. `Reveal.tsx`'s existing `handleShare` (extended by
02) adds the link to the share payload. **Owns / exports:** `PublishedTales/` (server) + the public tale page + the
`handleShare` link addition. **Gotchas:** this is the ONE exception to the feature's client-only boundary (feature.md
Decisions) - keep it isolated from `GameHub.cs` and the round lifecycle. Serve `noindex, nofollow`. Never publish
anything a family-safe session would not have shown; only nickname + Guardian appear (no PII). The link is FREE - no
entitlement check (README section 3). Out of scope: public gallery/discovery, comments, analytics, server-side image
render (the shared image stays 01/02's client render).

## Cross-cutting concerns

- **This feature renders and stores; it does not collect or assemble.** Every story consumes an already-assembled,
  already-filtered `AssembledStory` from `the-reveal`. If any story here seems to need new engine logic, new
  free-text collection, or a change to `assemble()`/`collectWord()`, that is scope creep out of this feature's lane -
  flag it rather than build it here.
- **Stories 01-03 make no API/hub change** and are `web/`-only, entirely disjoint from `GameHub.cs` and
  `useGameHub.ts`. **Story 04 is the one exception:** it adds a small, isolated server surface (a public read-only
  tale route + Table Storage), deliberately kept separate from the real-time backbone and the round lifecycle - it
  does not touch `GameHub.cs`/`useGameHub.ts` either, it is its own thin controller.
- **Anonymous by construction, still.** The only identity ever rendered onto an image or stored in the local gallery
  is the in-session nickname + Guardian variant already shown on the roster/reveal - never a real name, device id,
  account, or any other PII, consistent with README section 3's identity model (players are anonymous forever; only
  a purchaser gets an account, and only when they buy - which has no bearing on this anonymous, account-free feature).
- **Avoid ads, use the watermark as the growth mechanic instead.** Story 02's watermark is the ENTIRE monetization-
  adjacent surface of this feature - README section 3 explicitly says "avoid ads," so the watermark is a passive,
  non-intrusive brand touch on organic shares, not a new revenue mechanic and not an ad.
- **Bundle-size discipline.** This is a PWA; a new client-side rendering dependency (story 01) or storage library
  (story 03) should be evaluated for size cost and flagged before landing, not silently added.
- **Inter-feature ordering (prerequisites):** `the-reveal/01` (the screen and data this feature captures),
  `session-engine/04` (the Web Share pattern story 02 mirrors), `child-safety/01` (upstream filtering this feature
  relies on but never re-implements). This feature can be built entirely in parallel with `replay-remix` (disjoint
  files - the only shared touchpoint is both extending `Reveal.tsx`, which should be sequenced by whichever lands
  first at orchestration time rather than assumed here).
- **No i18n** (plain strings), **big tap targets**, **no em dashes**.
