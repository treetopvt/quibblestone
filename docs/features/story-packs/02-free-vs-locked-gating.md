# Story: Free-vs-locked gating

**Feature:** Story Packs (Guardian's Vault)  ·  **Status:** Not Started  ·  **Issue:** #76

## Context
Story 01 lets a locked pack be seen; this story decides what happens when a
player tries to *use* one. The gate is a single, friendly decision point at
session-creation time, not a nag that follows the player around - and it is
never an ad, a timer, or a partial-content tease. See
[feature.md](./feature.md) and README section 3 (monetization seam) and
section 6 (no ads, kid-facing posture).

## Acceptance Criteria
- [ ] AC-01: Given a free pack, when a host selects it for a session, then it
      is playable with no entitlement check at all - free packs are never
      routed through the gate.
- [ ] AC-02: Given a locked pack, when a host taps it in the Guardian's
      Vault, then they see a friendly paywall screen: what the pack contains,
      the price, and a single clear "Unlock" CTA - no urgency language, no
      countdown, no ads.
- [ ] AC-03: Given a locked pack and a host who already owns it (an existing
      `pack.<id>` entitlement), then selecting it is treated exactly like a
      free pack - no repeated purchase prompt.
- [ ] AC-04: Given a session is being created, when the host's selected
      content includes a locked pack, then the entitlement check for
      `pack.<id>` happens exactly once, at that session-creation decision
      point - not re-checked per blank, per round, or per reveal during play.
- [ ] AC-05: Given the paywall's "Unlock" CTA, then it routes into the
      billing-entitlements purchase flow; on a successful purchase, the pack
      becomes immediately selectable without restarting the session-creation
      flow from scratch.
- [ ] AC-06: Given the kid-facing audience, then the paywall never collects
      PII beyond what the purchaser's lightweight account already requires
      (README section 3) - players joining the session remain fully
      anonymous regardless of which packs are in play.

## Out of Scope
- The purchase/checkout flow itself (billing-entitlements owns Stripe
  integration and the entitlement store; this story only calls into it).
- Subscription-vs-pack pricing strategy (README section 12, an open
  decision - this story's gate works the same regardless of how pricing
  shakes out).
- Any per-request or mid-round entitlement re-check (explicitly against
  README section 3's thin, session-creation-time discipline).
- Family/group entitlement sharing rules beyond "the purchaser's account
  owns the pack" (billing-entitlements territory).

## Technical Notes
- Projects: `web/` and `api/`. The gate lives on the session-creation path
  (wherever a host's content selection - base library, family-safe toggle,
  chosen packs - is finalized into a session), mirroring the family-safe
  toggle's session/host-level decision point (child-safety/02) rather than a
  new pattern.
- `PackLockBadge.tsx` (web) is a small presentational component (FontAwesome
  lock icon over the theme's gold token) - reused by story 01's grid cards
  wherever a lock needs to render. `PackPaywall.tsx` (web) is the friendly
  paywall screen, built entirely from shared design-system contracts
  (`AppBar`, `Button`, theme tokens) - no bespoke visual language.
- Server-side: extend the session-creation path (or `PackCatalogController`,
  depending on where content selection is finalized) to check the
  `pack.<id>` capability key against the entitlement seam
  `billing-entitlements` exposes. If that seam is not yet available when
  this story is built, stub the check behind a clearly-marked seam (e.g. an
  interface returning "not entitled" for every locked pack) so the UI/UX
  ships now and the real check is a drop-in later - do not block this
  story's UI work on billing-entitlements' full build-out.
- No ads, no dark patterns (CLAUDE.md section 5 / README section 3): the
  paywall copy is plain, honest, and kid-appropriate; a single CTA, no
  secondary "are you sure" friction designed to convert.

## Tests
No test harness is wired up yet for this feature's code; note intended tests
here per the platform-devops harness plan.

| AC | Test |
|---|---|
| AC-01 | manual: select a free pack at session creation and confirm no entitlement call is made (network/log inspection) |
| AC-02 | manual: tap a locked pack and confirm the paywall shows contents, price, and a single "Unlock" CTA with no countdown/urgency copy |
| AC-03 | manual: with a stubbed/real `pack.<id>` entitlement present, confirm selecting that pack skips the paywall entirely |
| AC-04 | manual: instrument/inspect the session-creation call and confirm exactly one entitlement check occurs per locked pack selected, none during play |
| AC-05 | manual: complete the purchase flow (or its stub) and confirm the pack becomes selectable without restarting session setup |
| AC-06 | manual: confirm the paywall and purchase flow collect nothing beyond the purchaser account fields billing-entitlements already defines; joining players remain anonymous |

## Dependencies
- story-packs/01-pack-catalog-model
- billing-entitlements (the `pack.<id>` capability-key seam; may be stubbed
  if not yet available, per Technical Notes)
