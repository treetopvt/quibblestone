# Feature: Billing & Entitlements

## Summary
The entitlement seam - a single, thin check evaluated at session-creation time
that answers "is this capability unlocked?" - plus the plumbing on top of it:
an ungated donate-first tip jar, Stripe integration, a gated purchase flow for
packs/subscription, and purchaser-facing restore/manage. Everything defaults
to unlocked; gating a capability later is a config flip, not a refactor.

> **State of the tree (2026-07-03).** The `IEntitlementService` interface,
> `SessionEntitlements`, the `ai.onDemand` catalog reservation, and the
> `GameHub.CreateRoom` capture already ship as a thin, default-unlocked
> stand-in (`ai-cost-gate/02`, #121, PR #132) - see
> `api/src/Entitlements/IEntitlementService.cs`. Story 01 (#70) no longer
> BUILDS this seam; it EXTENDS it: the full capability catalog, the
> stored-value evaluation behind the same interface, and the lease-shaped
> grant store. Stories 03-04 fold in ADR 0002's subscription specifics
> (webhook lifecycle, dunning grace, family-plan bundle). See
> [ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md)'s "State of
> the tree" for the authoritative account.

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
| 01 | #70 | Entitlement model + session-creation gate | Complete |
| 02 | #71 | Tip jar ("Buy the Guardians a coffee") | Complete |
| 03 | #72 | Stripe integration + entitlement store | Complete |
| 04 | #73 | Gated purchase flow | Complete |
| 05 | #74 | Restore / manage entitlements | Complete |
| 06 | TBD | Live/test Stripe mode - mode-aware config + toggle endpoint | Complete (interim gate) |
| 07 | TBD | Live/test Stripe mode - operator toggle UI | Complete (interim gate) |

## Dependencies
- accounts-identity (a purchase needs the lightweight purchaser account from
  accounts-identity/02; restore, story 05, is the read side of
  accounts-identity/03).
- session-engine (the gate in story 01 hooks the moment a room is created).
- child-safety (any purchase-adjacent surface stays out of the kid play-flow
  and collects no data on minors).
- design-system (theme tokens + Button/AppBar contracts for the tip jar and
  purchase UI, kept visually consistent and friendly).
- sysadmin-console/01 (#135, operator login + admin boundary) - story 07 (the
  toggle UI) belongs behind real operator auth once it lands; see story 06 and
  07's Technical Notes for the interim gate used until then.

## Design notes
- **The seam is the whole point of Phase 2 (story 01).** A single service/hook
  answers one question - "is capability X unlocked for this session?" - and it
  is asked exactly once, at session-creation, never per-request or
  per-submission. Every other story in this feature, and every future paid
  feature (add-on packs, AI illustration/voice/on-demand), is a *consumer* of
  this seam, not a new gate. If a future story is tempted to add its own
  per-request check, that is a smell - it belongs behind this seam instead.
  The interface itself already ships thin (`ai-cost-gate/02`); story 01 now
  extends it with the full catalog, the stored-value evaluation, and the
  grant store.
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
- **The live/test mode toggle (stories 06-07) is heavier than the current
  config-presence idiom on purpose - the owner explicitly chose to build it as
  a real feature.** Today "on" is one config-presence flip
  (`STRIPE_ENABLED` + Key Vault secrets, see
  `docs/runbooks/enable-stripe-billing.md`) requiring a redeploy to change. The
  toggle instead holds BOTH live and test Stripe credentials at once and lets
  an operator flip which is ACTIVE at runtime, from a persisted flag (Azure
  Table Storage - reuse, no new resource) - because the owner wants ongoing
  operator control to gradually turn paid features on for sale and to exercise
  both flows against the one public site (`quibblestone.com`). This is a
  superset of, not a replacement for, the existing runbook: the runbook still
  covers first-time Stripe dashboard/Key Vault setup for BOTH modes; the
  toggle only changes which already-configured mode is active.

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
- More than two Stripe modes, per-region mode routing, or a mode that varies
  per-request/per-session (stories 06-07: exactly one ACTIVE mode for the
  whole app at a time, operator-selected).
- A scheduled/timed mode switch (e.g. "go live at 9am Saturday") - stories
  06-07 are a manual, confirmation-gated flip only.
- Multi-operator approval workflow for the switch (e.g. two-person sign-off) -
  alpha has one operator (sysadmin-console posture); revisit only if the team
  grows.

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
- 2026-07-03: [ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md)
  (accounts, subscriptions, sys-admin surface) adds small, additive subscription
  scope on top of this feature without reshaping the #70 seam. Decisions C + D
  are resolved:
  - **C - family plan = the full paid-tier bundle; packs sold separately.** The
    subscription grants `library.full`, `play.remote`, `play.largeGroup`, and the
    `ai.*` keys (once those features exist) while active; add-on packs stay a
    separate one-time `pack.<id>` purchase on the same plumbing. The grant is
    lease-shaped: the `EntitlementGrant` row carries `validThrough` + `source`
    (subscription vs one-time), refreshed on each renewal webhook, so the
    session-creation check reads "active?" from one Table Storage read (story 03).
  - **D - dunning grace: ~7 days.** On `past_due`, extend `validThrough` a ~7-day
    grace rather than expiring immediately, so a failed card does not lock a
    family mid-session; then fall back to the generous free tier. Full
    dunning/retry stays parked (below).
  - Subscription webhook lifecycle (created / renewed / past_due / canceled) lands
    in story 03's Stripe handler. The host proves purchaser status at
    `CreateRoom` via a SignalR access token (ADR 0002 Decision F), and story 01's
    gate stores only the resolved capabilities on `Room` - never a purchaser id.
- 2026-07-03: Story-level refresh against shipped reality: story 01 (#70) is
  rewritten to describe EXTENDING the already-shipped `IEntitlementService`
  seam (`ai-cost-gate/02`, #121, PR #132) rather than building it from
  scratch - its ACs, Technical Notes, and Tests now name the exact edit
  (catalog extension + stored-value swap + grant store) instead of a new
  folder. Stories 03-04 gained explicit ACs for the subscription webhook
  lifecycle, the ~7-day dunning grace, and the family-plan bundle mapping
  (folded into the existing stories per Decisions C/D above, rather than a
  new story - the incremental scope is additive to plumbing/mapping each
  story already owns).
- 2026-07-03: All five stories (01-05) shipped, code-reviewed clean, and
  verified - including a live end-to-end pass against Stripe test-mode
  (real Checkout Session, real signed webhook, real grant write; a live bug
  requiring `throwOnApiVersionMismatch: false` on the webhook handler was
  caught and fixed during that pass). Feature flipped to Complete; see each
  story's Tests table for the automated suite and the manual verifications
  performed (multi-device restore walk; UI audit confirming billing entry
  points appear only on Home/Account and are absent from Join/Lobby/
  FillBlank/Reveal).
- 2026-07-03: Owner requested an operator-controlled, RUNTIME live/test Stripe
  mode toggle (no redeploy) - explicitly heavier than the current
  config-presence approach, and explicitly chosen anyway so the owner can
  gradually turn paid features on for sale and exercise both flows on the one
  public site. Added as two new stories (06 server-side mode-aware config +
  toggle endpoint, 07 the operator UI) rather than folding into story 03,
  because 03 is already Complete and shipped/verified - reopening it would
  mix a settled, tested story with new scope. Decomposed into two because the
  server-side mode plumbing (06) is independently valuable/testable without
  any UI, and 07's placement depends on `sysadmin-console/01` landing (see
  06's and 07's Technical Notes "dependency reality" for the interim gate
  used until real operator auth exists).
