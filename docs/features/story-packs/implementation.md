<!--
  Implementation plan for the story-packs feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Story Packs (Guardian's Vault)

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

The pack catalog (story 01) is foundation: the data model + browse UI other stories build on. Gating (story 02) is
a thin layer over it (the entitlement check + paywall UI), and the first themed packs (story 03) is pure content -
it authors pack data against story 01's model and does not touch its code. 01 -> {02, 03} therefore fans out after
the model lands; 02 and 03 are file-disjoint from each other (02 touches gating/paywall code, 03 touches only
content data), so they run concurrently once 01 is done.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Template / Blank / BlankCategory schema (a pack groups existing-shape templates) | `template-model/01`'s mode-agnostic type | `web/src/engine/template.ts` |
| Content library read seam (packs beyond the hand-curated set draw from here) | `IContentLibrary` (**ai-content-factory/03**) | `api/src/Content/IContentLibrary.cs` |
| Styling / theme tokens (stone-tablet motif, gold-lock badge color) | the MUI theme (palette, `theme.palette.gold`, `theme.palette.tablet`, shape/radii) | `web/src/theme.ts` |
| Shared UI contracts | `AppBar`, gold-CTA + outlined-purple `Button`, `BottomActionBar`, `Guardian` | `web/src/components/` |
| Icons (lock badge, pack category icons) | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Real-time | the one SignalR connection hook, if pack selection needs to broadcast to a room (see 01 gotchas) | `web/src/signalr/useGameHub.ts` |
| API hub / REST | the in-app SignalR hub for room-scoped concerns; REST controllers for catalog reads (mirrors `ModerationController`'s thin-controller pattern) | `api/src/Hubs/GameHub.cs`, `api/src/Controllers/` |
| Child safety / family-safe tags | the family-safe gate and template tags (**child-safety/02**, **template-model/01**) | `api/src/Safety/FamilySafeContentSelector.cs` |
| Entitlement check (capability key `pack.<id>`, checked at session-creation) | the billing-entitlements seam (authored in parallel) | `api/src/Billing/` (expected location, mirrors `api/src/Safety/`'s pattern) |
| Config | `import.meta.env` (`VITE_*`) | `web/src/vite-env.d.ts`, `web/.env.development` |

What this feature **exports** that others import:
- The **pack catalog model** (`Pack` type: id, name, theme/age tags, free-or-locked, template ids) and the
  **Guardian's Vault** browse screen - the surface a host uses to add themed content to a session, alongside the
  base library.
- The **pack entitlement check** (a session-creation-time gate over `pack.<id>`) - the pattern any future paid
  content grouping (not just packs) can mirror.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. Foundation first; the catalog model
must land before gating or seed content can build on it.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 pack-catalog-model | TBD | `web/src/engine/pack.ts` (Pack type), `web/src/pages/GuardiansVault.tsx` (browse UI), `api/src/Content/IPackCatalog.cs`, `api/src/Content/TableStoragePackCatalog.cs`, `api/src/Controllers/PackCatalogController.cs` | template-model/01, design-system/01-02, ai-content-factory/03 (read seam, not a hard blocker - see gotchas) | none within this feature (foundation) | 1 | high |
| 02 free-vs-locked-gating | TBD | `web/src/components/PackLockBadge.tsx`, `web/src/pages/PackPaywall.tsx`; edits `web/src/pages/GuardiansVault.tsx` (lock badge + tap-through wiring); `api/src/Controllers/PackCatalogController.cs` edits (entitlement check on session-creation read) | story-packs/01, billing-entitlements (pack.<id> capability seam) | story-packs/03 (disjoint: code vs. content data) | 2 | medium |
| 03 first-themed-packs | TBD | `web/src/content/packs/holiday.ts`, `web/src/content/packs/roadTrip.ts`, `web/src/content/packs/spooky.ts`, an authoring note | story-packs/01 (the model these packs conform to) | story-packs/02 (disjoint: content data vs. code) | 2 | medium |

**Concurrency per wave:** Wave 1 = 1 (the catalog model + browser; foundation for the rest of the feature). Wave 2 =
{02, 03} in parallel (02 owns gating/paywall code and only edits `GuardiansVault.tsx` for the lock badge/tap-through,
03 owns only new content data files under `web/src/content/packs/` - no file overlap).

## Per-story tech notes

### 01 - Pack catalog model + Guardian's Vault browser
- **Approach:** define a `Pack` type (id, name, theme + age tags, `free: boolean`, an ordered list of template ids)
  in `web/src/engine/pack.ts`, deliberately separate from `Template` itself - a pack references templates, it does
  not duplicate or fork them. Server-side, `IPackCatalog` (mirrors the `IContentLibrary` pattern from
  ai-content-factory) persists pack metadata in Table Storage and resolves a pack's template ids against
  `IContentLibrary` for content. The **Guardian's Vault** browse screen renders packs as a grid of glowing
  stone-tablet cards (reusing the theme's `tablet` gradient tokens and card shape - no new visual language), each
  showing the pack name, theme icon, and (this story) whether it is free or locked as a simple badge - the actual
  paywall interaction is story 02's job, this story only needs the badge to render correctly for both states.
- **Owns / exports:** `pack.ts` (the `Pack` type), `GuardiansVault.tsx` (the browse screen), the server-side
  `IPackCatalog` contract and its Table Storage implementation, `PackCatalogController.cs` (a thin REST read seam,
  following the `ModerationController` no-logic-of-its-own pattern).
- **Gotchas:** this story's browse UI must render correctly even if `ai-content-factory` has not shipped yet - a
  pack's templates can resolve entirely from hand-curated content (`story-packs/03`) or the Slice-1 seed library;
  `IContentLibrary` is a read dependency for richer future packs, not a hard blocker for this story to build against
  an empty or hand-curated-only library. Keep pack browsing accessible from `Home.tsx` (a clearly-secondary entry,
  same pattern as the existing Solo entry link) so it does not compete with the primary "Create a game" / "Join a
  game" CTAs.

### 02 - Free-vs-locked gating
- **Approach:** "the same catalog, plus a paywall." A locked pack's card carries a gold-lock badge
  (`PackLockBadge.tsx`, FontAwesome lock icon over the gold CTA color) instead of being hidden - browsing stays
  open to everyone (README section 3's "generous free tier" spirit extends to *seeing* what exists, not just
  playing it). Tapping a locked pack routes to a friendly `PackPaywall.tsx` screen (kid-safe copy, no urgency
  language, no countdown, no partial content teaser beyond what the catalog already showed) with a single clear CTA
  to purchase, wired to the billing-entitlements purchase flow. The actual gate is a **session-creation-time**
  check: when a host is assembling a session's content and selects a locked pack, `PackCatalogController` (or the
  session-creation path that reads pack selections) checks the capability key `pack.<id>` against the purchaser's
  entitlements exactly once, at that decision point - not on every template draw or every blank during play.
- **Owns / exports:** `PackLockBadge.tsx`, `PackPaywall.tsx`; the entitlement-check wiring inside the
  session-creation path.
- **Gotchas:** **no ads, no dark patterns** - this is a non-negotiable per README section 3 and section 6's
  kid-facing posture; keep the paywall to "here's what you get, here's the price, here's the button." Free packs
  must be **always** playable with zero entitlement check - do not accidentally route free-pack selection through
  the same gate as locked packs (that would be a per-request-style check creeping in where README section 3
  explicitly wants a thin, session-creation-time decision). This story depends on `billing-entitlements` exposing a
  checkable capability key; if that feature's seam is not yet in place, this story's gate can be stubbed to "always
  locked" or "always free" behind a clearly-marked seam so the UI/UX still ships and the real check drops in later
  without a rewrite (mirrors README section 3's "build the account hooks in early" discipline).

### 03 - First themed packs (seed content)
- **Approach:** author and vet a small, initial set of hand-curated packs - Holiday, Road-Trip, Spooky are the
  named examples (feature.md), each entirely family-safe. Mirrors `template-model/02`'s seed-library discipline
  exactly: a simple, documented, hand-authorable data format, vetted to the same standard as any player-submitted
  word (README section 6) before it ships. Each pack is a small `web/src/content/packs/<name>.ts` data file
  exporting a `Pack` plus its constituent `Template`s (or template ids resolving against the base library, per
  story 01's model), each family-safe-toggle aware (tagged and gated exactly like base content,
  child-safety/02).
- **Owns / exports:** `web/src/content/packs/holiday.ts`, `roadTrip.ts`, `spooky.ts`, and a short authoring note
  (mirrors `template-model/02`'s authoring doc) covering how to add a new pack as data.
- **Gotchas:** this story can ship entirely independent of `ai-content-factory` - it hand-writes and hand-vets its
  content the same way the Slice-1 seed library did, so it is not blocked on the AI pipeline landing. If
  `ai-content-factory` has shipped by the time this story is built, its published library becomes an *additional*
  source these (or later) packs can draw from - it does not replace the hand-curated authoring path. Keep each
  pack small (mirrors the 10-15-template seed library scale, not an attempt to be exhaustive) - the point is
  proving the pack model and having something genuinely playable, not maximizing volume on day one.

## Cross-cutting concerns

- **A pack is a grouping, never a parallel content type.** If building any story in this feature requires touching
  `template-model`'s `Template`/`Blank`/`BlankCategory` shape, that is the abstraction leaking - flag it and fix
  the schema there, not by forking a pack-specific template shape here.
- **Child safety applies identically to pack content.** Every template inside every pack (free or locked, hand-
  curated or AI-published) is family-safe-toggle aware and vetted before ship (README section 6) - packs do not get
  a lighter moderation bar than base content because they are paid.
- **Entitlement is thin and session-creation-time only** (README section 3). Story 02 is the one place this feature
  checks `pack.<id>` - no other story adds a second, per-request check. If a future story is tempted to re-check
  entitlement mid-round (e.g. "is this player still entitled to this word bank"), that is scope creep against the
  README's explicit monetization-seam discipline.
- **No ads, no dark patterns** anywhere in the Vault browser or the paywall (README section 3 and 6).
- **Inter-feature ordering:** `template-model/01` and `design-system/01-02` must exist before story 01. Story 01
  must land before 02 and 03. `billing-entitlements`'s `pack.<id>` capability seam must exist (or be stubbed per
  02's gotcha) before 02's real gate ships. `ai-content-factory/03` is a soft dependency for richer future packs,
  not a hard blocker for story 03.
- **No i18n** (plain strings), **big tap targets**, **no em dashes**.
