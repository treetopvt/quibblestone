# Feature: Billing & Entitlements

## Summary
The entitlement seam - a single, thin check evaluated at session-creation time
that answers "is this capability unlocked?" - plus the plumbing on top of it:
an ungated donate-first tip jar, Stripe integration, a gated purchase flow for
packs/subscription, and purchaser-facing restore/manage. Everything defaults
to unlocked; gating a capability later is a config flip, not a refactor.

## README reference
README section 3 (Monetization - "a thin entitlement check on top of the core
engine, decided at session-creation time, not per-request"; free tier
generous; paid tier is a family-plan subscription and/or add-on packs, same
billing plumbing; avoid ads) and section 7 (Epic Map - Phase 2, "Billing &
Entitlements (L) - Stripe, entitlement store, session-creation gating"). The
Azure Functions carve-out note for Stripe webhooks: section 4. CLAUDE.md
section 6 (Monetization seam - "build the account/entitlement hooks in early
even if the UI is minimal").

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #70 | Entitlement model + session-creation gate | Not Started |
| 02 | #71 | Tip jar ("Buy the Guardians a coffee") | Not Started |
| 03 | #72 | Stripe integration + entitlement store | Not Started |
| 04 | #73 | Gated purchase flow | Not Started |
| 05 | #74 | Restore / manage entitlements | Not Started |

## Dependencies
- accounts-identity (a purchase needs the lightweight purchaser account from
  accounts-identity/02; restore, story 05, is the read side of
  accounts-identity/03).
- session-engine (the gate in story 01 hooks the moment a room is created).
- child-safety (any purchase-adjacent surface stays out of the kid play-flow
  and collects no data on minors).
- design-system (theme tokens + Button/AppBar contracts for the tip jar and
  purchase UI, kept visually consistent and friendly).

## Design notes
- **The seam is the whole point of Phase 2 (story 01).** A single service/hook
  answers one question - "is capability X unlocked for this session?" - and it
  is asked exactly once, at session-creation, never per-request or
  per-submission. Every other story in this feature, and every future paid
  feature (add-on packs, AI illustration/voice/on-demand), is a *consumer* of
  this seam, not a new gate. If a future story is tempted to add its own
  per-request check, that is a smell - it belongs behind this seam instead.
- **Default-unlocked is not a placeholder, it is the design.** Shipping the
  seam with every capability defaulted to free/unlocked means introducing it
  changes zero observed behavior on day one. Turning on a real paywall later
  is flipping a stored value, not shipping new gating code. This is what lets
  the seam land ahead of any actual paywall, per CLAUDE.md section 6.
- **Capability keys, not feature flags scattered per-story.** One small,
  growing catalog of string keys (e.g. `library.full`, `play.remote`,
  `play.largeGroup`, `ai.illustration`, `ai.voice`, `ai.onDemand`,
  `pack.<id>`) is the vocabulary every gated feature speaks. New paid features
  add a key to the catalog; they do not invent their own check.
- **Donate-first, not paywall-first.** The tip jar (story 02) is the product's
  day-one monetization rung: no gate, no entitlement, just goodwill, shipped
  before anything is ever locked. This matches README section 3's "avoid ads"
  stance and the emotional brief (README section 10) - money should feel like
  a thank-you, not a toll booth.
- **One billing plumbing, two purchase shapes.** README section 3 explicitly
  wants the same Stripe integration to support both an add-on pack (one-time)
  and a family-plan subscription (recurring). Story 03 builds the shared
  Stripe + entitlement-store plumbing once; story 04's gated purchase flow is
  the first concrete purchase UI on top of it, and it must not assume a single
  purchase shape is the only one ever supported.
- **Everything server-authoritative.** Stripe secret keys live in Azure Key
  Vault, never in a `VITE_*` variable (those ship to the browser, CLAUDE.md
  section 4 / section 6). Entitlement state is granted and read server-side;
  the client only ever asks "what do I have" and never asserts "I paid for
  this" directly into gameplay.
- **Entitlements persist in Azure Table Storage** (README section 4: "Table
  Storage for templates and entitlements"), keyed by the purchaser account
  from accounts-identity/02. No new datastore.
- **Webhook-first confirmation, with a documented seam to Functions.** README
  section 4 names Stripe webhooks as "the natural first candidates" for a
  future Azure Functions carve-out (event-triggered, scale-to-zero). Story 03
  ships an in-app webhook handler inside the single ASP.NET Core app (matching
  the "one app to start" architecture) but keeps the handler isolated enough
  that lifting it into a Function later is a move, not a rewrite.
- **Kid-safe, friendly, no dark patterns.** Every purchase-adjacent surface
  (tip jar, paywall, restore) stays out of the kid play-flow (join, lobby,
  word entry, reveal never show a price or a lock icon), uses the warm
  stone-tablet/Guardian visual language rather than aggressive upsell styling,
  and never nags. No ads, ever (README section 3).

## Parked - Phase 3+
- Gating any AI feature (`ai.illustration`, `ai.voice`, `ai.onDemand`) itself -
  those features do not exist yet; this feature only reserves their
  capability keys in the catalog so gating them later is a config flip.
- Add-on Pack Catalog UI/browsing experience (README section 7, Phase 3) -
  story 04 buys *one* pack via a direct link/flow; a browsable catalog page is
  a later, separate feature.
- Refunds, proration, plan upgrades/downgrades, dunning/failed-payment
  retries, and any Stripe Customer Portal-style self-service beyond the
  minimal restore/manage view in story 05.
- Regional pricing, tax handling (e.g. Stripe Tax), and multi-currency.
- Gift purchases / redeeming a code bought by someone else.

## Decisions
- 2026-07-01: Scoped story 01 as the load-bearing story of both new features -
  every later paid capability, in this feature and beyond, must be written as
  a consumer of its check rather than inventing a parallel gate. Recorded
  during the look-ahead planning pass ahead of Slice 1 shipping, so the seam
  is precise before any coding agent touches it.
- 2026-07-01: Tip jar (story 02) ordered before Stripe integration (story 03)
  in the Stories list to reflect the product's donate-first stance, even
  though story 03's plumbing is a technical prerequisite for story 02 to
  actually charge a card - see the Wave Plan in implementation.md for the real
  build order.
