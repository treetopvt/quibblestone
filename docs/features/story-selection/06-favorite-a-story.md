# Story: Favorite a story and replay it (device-local)

**Feature:** Story Selection & Freshness  ·  **Status:** Complete  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #108

## Context
A kid finds a few tales they love and wants to play THOSE again, with different
words each time. Today the game deals you a story - there is no way to mark one you
love and come back to it. The freshness stage (`story-selection/03`) already
ANTICIPATES this: its AC-04 talks about "replaying a favorite" bypassing rotation,
but nothing yet lets a player MARK or REVISIT one. This story fills that gap: a
device-local "favorites" list - star a story template, see your favorites, pick one
to play again (fresh blanks, new words). It stays anonymous and account-free (a
favorite is just a template id in local storage), it is FREE, and it feeds the SAME
selection pipeline (never a safety bypass). It is the lightweight personal cut of the
"browse and pick a specific story" picker that feature.md parks. See
[feature.md](./feature.md) and `story-selection/03-freshness-rotation.md`.

## Acceptance Criteria
- [x] AC-01: Given the end of a tale (solo Reveal, group Round Complete), then a
      clear star/favorite control lets me mark THIS story template as a favorite;
      tapping it again unfavorites it, and the control always reflects the current
      state - big tap target, theme-styled, a FontAwesome star (filled vs outline for
      on vs off).
- [x] AC-02: Given I have favorited at least one story, then a "Favorites" list is
      reachable from the app's navigation (e.g. from Home), showing my favorited
      templates by title (with a light length-class / mode hint), most-recently-
      favorited first; an empty state reads friendly ("Star a tale you love to find it
      here"), never a dead end.
- [x] AC-03: Given the Favorites list, when I pick a favorite, then a new game starts
      on that EXACT template with fresh blanks - no template picker, straight into word
      collection - so "put different words in" is the whole loop. In solo this replays
      on my device; in a group room the HOST picks from their favorites to start the
      round (host-initiated, like other host controls).
- [x] AC-04 (freshness interaction): Given I play a favorite, then it is an EXPLICIT
      replay - it BYPASSES the freshness filter and does NOT re-stamp the template's
      freshness history (so replaying a favorite never makes the random pick "forget"
      other unplayed stories). This is exactly the "replaying a favorite" case
      `story-selection/03` AC-04 already reserves - this story is its trigger.
- [x] AC-05 (storage / identity): Given favorites, then they are DEVICE-LOCAL
      (`localStorage`, the same posture as `story-selection/03`'s solo history and
      `keepsake-gallery/03`), anonymous, account-free, and store only template ids
      (plus a cached title for display) - never words, never PII, never a server sync.
      Clearing browser storage simply clears favorites. Cross-device / per-person
      favorites are parked (they need accounts-identity).
- [x] AC-06 (child-safety): Given a favorite is played, then it still passes the
      family-safe gate FIRST, exactly like any other pick - a favorited template that
      is not family-safe is not offered or playable in a family-safe session. The star
      is a shortcut INTO the existing pipeline, never a way around safety, and
      favoriting introduces no free-text surface and collects no PII (README section 6).
- [x] AC-07 (entitlement): Given favoriting and replaying a favorite, then it is FREE -
      it consumes no billing-entitlements capability key and is not gated at
      session-creation (README section 3, the generous free tier). Favorites cover
      templates the player can already play; a favorite that points at a locked
      story-pack template the player does not own still requires ownership to PLAY (the
      star does not bypass pack gating) - it does not, by existing, grant access.

## Out of Scope
- A full "browse and pick ANY story" catalog picker - feature.md parks that as a
  separate product surface (related to story-packs). Favorites is the small personal
  list of stories you have ALREADY played and starred, not a library browser.
- Favoriting a specific FINISHED tale / result - revisiting a completed tale (its
  filled-in words / saved image) is `keepsake-gallery/03` "Tales we've carved". THIS
  story favorites the TEMPLATE to replay with NEW words; keep the two distinct.
- Cross-device or per-person account-synced favorites (needs accounts-identity - parked
  in feature.md, same as cross-device freshness).
- Folders, tags, reordering, or search within favorites - a simple recency list only.
- Sharing a favorites list, or any social/discovery surface over favorites.
- Like/dislike feedback - that is `story-selection/05` (anonymous creator signal, a
  different mechanic and sink); a star is a PRIVATE, device-local shortcut, not a vote.
  Do not conflate the two or route a favorite into the serve/thumbs log.

## Technical Notes
- **Device-local store (AC-05):** a small `localStorage`-backed module (e.g.
  `web/src/content/favorites.ts`), mirroring the posture of `web/src/identity.ts` and
  `story-selection/03`'s solo freshness history - pure add / remove / list over an
  array of `{ templateId, title }`, unit-testable in isolation (Vitest), no server.
- **Surfaces:** a star control on `Reveal` (`the-reveal/01`) and `Round Complete`
  (`group-play/04`), plus a "Favorites" list screen reachable from `Home`
  (`single-player`/`session-engine` own Home). Theme tokens only; FontAwesome star
  (filled/outline) registered in `web/src/fontawesome.ts`.
- **Selection seam (AC-03, AC-04):** "play a favorite" feeds the chosen template id
  directly into the existing selection call site - the web solo pick (`Solo.tsx`, which
  `story-selection/01` upgrades) and the server host pick (`GameHub.StartRound`) - as an
  EXPLICIT template choice that skips the freshness stage and does not re-stamp history
  (the same bypass `story-selection/03` AC-04 defines for "Play again" / "Carve it
  again"). Coordinate with story 01's pipeline shape and story 03's freshness seam; do
  NOT add a second selection path.
- **Family-safe stays first (AC-06):** a favorite still runs through the family-safe
  gate (`selectTemplates` / `FamilySafeContentSelector`) before it is offered or played.
- **No engine change** - this is selection + a device-local list + two star affordances;
  collection and assembly are untouched (a leak if not - flag it, per feature.md).

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: star toggles on Reveal / Round Complete and reflects state; unfavorite works |
| AC-02 | manual: the Favorites list shows starred titles (recency order) and a friendly empty state |
| AC-03 | manual: picking a favorite starts that exact template with fresh blanks, no picker (solo replays; host starts a group round) |
| AC-04 | `web/src/content/*` unit + manual: playing a favorite bypasses freshness and does not re-stamp history (compose with story 03's freshness test) |
| AC-05 | `web/src/content/favorites.test.ts` - add/remove/list persists to localStorage, stores only ids + title, no PII; clearing storage resets |
| AC-06 | code review + manual: a non-family-safe favorite is not offered/played in a family-safe session; no free-text surface added |
| AC-07 | code review: no entitlement/capability check gates favoriting or replaying a favorite; a favorite does not bypass story-pack ownership to play |

## Dependencies
- story-selection/01-length-classes-and-selection-pipeline (the selection pipeline + call sites a favorite feeds)
- story-selection/03-freshness-rotation (the explicit-replay freshness bypass this triggers - AC-04)
- the-reveal/01-text-reveal (the Reveal surface the star lives on)
- group-play/04-round-complete (the Round Complete surface + host round-start for the group case)
- single-player (the solo pick site + Home entry point the Favorites list hangs off)
- child-safety/02-family-safe-toggle (the family-safe gate a favorite still passes)
- design-system/01-mui-theme-and-app-shell (theme, Button, star icon)
