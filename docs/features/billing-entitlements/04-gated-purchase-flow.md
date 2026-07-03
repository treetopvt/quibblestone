# Story: Gated purchase flow

**Feature:** Billing & Entitlements  ·  **Status:** Not Started  ·  **Issue:** #73

## Context
The first real, gated purchase: an add-on pack (or the family plan) that,
once bought, actually unlocks something. This story is the first place a
capability key from billing-entitlements/01's catalog flips from
"free-by-default" to "requires an entitlement," and it proves the whole seam
end to end - purchase, grant, and the *next* session-creation reflecting the
unlock. Free tier stays exactly as generous as today; this is additive. See
[feature.md](./feature.md) and README section 3 ("Add-on packs: themed
content ... as an alternative or supplement to subscription. Same billing
plumbing").

## Acceptance Criteria
- [ ] AC-01: Given a purchaser buys an add-on pack (identified by a single
      `pack.<id>` capability key from billing-entitlements/01's catalog) OR
      the family-plan subscription (ADR 0002 Decision C: the FULL paid-tier
      bundle - `library.full`, `play.remote`, `play.largeGroup`, and the
      `ai.*` keys once those features exist), when the purchase succeeds via
      story 03's Stripe plumbing, then the corresponding capability key(s) -
      one key for a pack, the whole bundle for the family plan - are granted
      to that purchaser's entitlement record as a lease-shaped
      `EntitlementGrant` (`source = one-time` for a pack, `source =
      subscription` for the family plan).
- [ ] AC-02: Given a purchaser has just been granted an entitlement, when they
      (or a player using their device/session) creates a **new** session
      (room or solo round) afterward, then billing-entitlements/01's
      session-creation gate reflects the unlock for that new session - the
      currently-running session is not expected to change mid-round (this is
      the "not per-request" contract, not a live-upgrade feature).
- [ ] AC-03: Given a family that has purchased nothing, when they play, then
      every Slice-1 free-tier capability (`library.full`'s free subset,
      `play.remote`, `play.largeGroup` up to the free-tier limits defined at
      launch, single-player, same-code group play, base content) remains
      exactly as available as it is today - this story narrows nothing that
      currently works.
- [ ] AC-04: Given the purchase flow's paywall screen, when it is shown, then
      it presents pricing plainly (no countdown timers, no "X people just
      bought this" social-pressure copy, no pre-checked upsell add-ons), uses
      the stone-tablet/Guardian visual language and gold (`#FFB22E`) CTA
      styling consistent with the rest of the app, and offers a clear way to
      back out without friction.
- [ ] AC-05: Given the paywall/purchase surface, when it is reachable in the
      app, then it is only reachable from purchaser-facing areas (Home's
      settings/account area, or a "get more" prompt shown to the room's host
      between rounds - never inside the active word-entry or reveal screens a
      child is using) - no paywall interrupts an in-progress round.
- [ ] AC-06: Given a purchase is attempted without a signed-in purchaser
      account yet, when checkout is initiated, then the lightweight purchaser
      account (accounts-identity/02) is created as a natural side effect of
      completing the purchase - the purchaser is never forced through a
      separate "create an account first" step before they can even see
      pricing.

## Out of Scope
- Building a browsable Add-On Pack Catalog UI (multiple packs, search/filter,
  merchandising) - parked in feature.md (Phase 3+); this story ships one
  concrete purchase path (a direct "unlock this pack" / "go family plan"
  flow), not a storefront.
- The pack *content* itself (what templates/library items a given
  `pack.<id>` actually contains) - that is a template-model/content-authoring
  concern; this story only wires the purchase-to-entitlement mechanics.
- Live, mid-session upgrades (a round in progress suddenly gaining new
  capabilities) - explicitly ruled out by AC-02; the seam is
  session-creation-time by design.
- Refunds, plan changes, cancellation flows - parked in feature.md.
- Gating any AI capability (`ai.illustration`, `ai.voice`, `ai.onDemand`) -
  those features do not exist yet; this story may reference their keys in the
  catalog but does not turn on real gating for them.
- Rendering dunning/grace-period messaging (e.g. a "your payment failed, you
  have N days" banner) - story 03 handles the ~7-day `past_due` grace
  mechanically by extending the grant's `validThrough` (ADR 0002 Decision D);
  a purchaser-facing dunning UI, if ever wanted, is a later, deliberate
  addition, not part of this story.

## Technical Notes
- Web: a purchase/paywall surface (new `web/src/pages/` component, e.g. a
  "Get more" screen) styled entirely from `web/src/theme.ts` and the shared
  `Button`/`AppBar` contracts (`web/src/components/`) - reuse the gold CTA /
  outlined-purple secondary pattern already established on Home
  (`web/src/pages/Home.tsx`) rather than inventing new button treatments.
  FontAwesome icons only.
- The purchase call goes through `StripeCheckoutService`
  (billing-entitlements/03) parameterized with the specific `pack.<id>` or
  the subscription product - this story does not add a second Stripe
  integration path.
- On the API side, the "which capability key(s) does this Stripe
  price/product id map to" lookup is a small, explicit table (config or a
  simple in-code map) living alongside `StripeCheckoutService` - its VALUE is
  a small LIST of capability keys, not always exactly one: a pack product
  maps to its single `pack.<id>` key, the family-plan product maps to the
  whole bundle (`library.full`, `play.remote`, `play.largeGroup`, `ai.*` once
  those exist - ADR 0002 Decision C). Keep the table easy to extend when a new
  pack is authored, per the "config flip, not a refactor" spirit of
  billing-entitlements/01.
- AC-02's "reflected on the next session-creation, not mid-round" behavior is
  a direct consequence of billing-entitlements/01 AC-03 - this story does not
  need to build any live-update mechanism; it only needs to *not* try to.
- AC-06 means this story's checkout-initiation code path calls into
  accounts-identity/02's account-creation-on-purchase behavior rather than
  gating checkout behind a separate sign-in screen - see
  accounts-identity/02's Technical Notes on account creation being embedded
  in the purchase flow.
- Keep the paywall copy and layout reviewed against feature.md's "kid-safe,
  friendly, no dark patterns" design note before considering this story done
  - this is as much a product-tone check as a functional one.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests/Billing/GatedPurchaseTests.cs (to be created): a completed test-mode purchase for a pack id grants that pack's single capability key; a completed family-plan subscription purchase grants the whole bundle (library.full, play.remote, play.largeGroup).` |
| AC-02 | `api/tests/Entitlements/EntitlementServiceTests.cs: EvaluateForSession called after a grant reflects the new capability; a fake "already-open session" object is unaffected.` |
| AC-03 | `tests/*.spec.ts (Playwright smoke) + existing session-engine/game-modes/group-play/single-player suites, re-run as regression: zero change to free-tier behavior.` |
| AC-04 | `manual: UX/copy review of the paywall screen against the no-dark-patterns checklist in feature.md.` |
| AC-05 | `manual: UI audit - confirm the paywall entry point is absent from active Join/Lobby/FillBlank/Reveal screens; only reachable from settings or a between-rounds host prompt.` |
| AC-06 | `manual: complete a purchase from a fresh browser profile with no prior account - confirm a purchaser account exists afterward without a separate sign-up step blocking checkout.` |

## Dependencies
- billing-entitlements/01 (the seam and catalog this story's grant targets).
- billing-entitlements/03 (the Stripe plumbing this story's checkout uses).
- accounts-identity/02 (the purchaser account created on purchase).
- design-system (theme tokens, Button/AppBar contracts).
