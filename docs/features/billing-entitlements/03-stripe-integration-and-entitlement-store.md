# Story: Stripe integration + entitlement store

**Feature:** Billing & Entitlements  ·  **Status:** Complete  ·  **Issue:** #72

## Context
The shared server-side plumbing that both the tip jar (story 02) and the
gated purchase flow (story 04) sit on top of: Stripe keys pulled from Azure
Key Vault, a payment-confirmation path via webhook, and the write side of the
lease-shaped entitlement store that billing-entitlements/01's session-creation
gate reads from. Same plumbing supports a one-time pack purchase and a
recurring family-plan subscription (README section 3), including that
subscription's full webhook LIFECYCLE (created / renewed / past_due / canceled)
and the ~7-day dunning grace on `past_due` (ADR 0002 Decisions C and D). See
[feature.md](./feature.md), README section 4 (the Azure Functions carve-out
note for Stripe webhooks), and
[ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md).

## Acceptance Criteria
- [x] AC-01: Given the API needs to call Stripe, when it does, then the
      Stripe secret key (and any webhook signing secret) is read from Azure
      Key Vault at startup/request time - it is never present in a
      `VITE_*` variable, a committed config file, or a log line.
- [x] AC-02: Given a Stripe Checkout Session is created (for a tip, a pack, or
      the family-plan subscription), when the flow is invoked, then it
      supports both a one-time payment mode (tip jar, add-on pack) and a
      recurring subscription mode (family plan) through the same underlying
      service - not two parallel integrations.
- [x] AC-03: Given Stripe confirms an initial payment or subscription creation
      (checkout completed), when that confirmation arrives, then it is
      received via a webhook endpoint, the webhook signature is verified
      before any processing, and the resulting entitlement (if any - the tip
      jar grants none, per story 02 AC-02) is written to the entitlement
      store from billing-entitlements/01 as a lease-shaped `EntitlementGrant`
      (capability key(s), `validThrough`, `source`).
- [x] AC-04: Given the webhook handler is implemented in-app (inside the
      single ASP.NET Core app, per README section 4's "one app to start"), it
      is written as an isolated, single-purpose endpoint/service (its own
      controller or minimal-API route + a dedicated handler class) so lifting
      it into an Azure Function later - the natural first carve-out README
      section 4 names - is a move of that class, not a rewrite.
- [x] AC-05: Given a webhook event is received twice (Stripe's documented
      at-least-once delivery), when the handler processes it, then the
      resulting entitlement grant is idempotent - processing the same event
      id twice does not double-grant or corrupt the entitlement store.
- [x] AC-06: Given a purchase completes for a signed-in purchaser
      (accounts-identity/02), when the entitlement is granted, then it is
      written keyed to that purchaser's identity in Azure Table Storage
      (README section 4), matching the lease-shaped `EntitlementGrant`
      read shape billing-entitlements/01's session-creation gate expects
      (`validThrough` + `source`, ADR 0002 Decision C).
- [x] AC-07: Given a payment fails or is abandoned mid-checkout, when that
      happens, then no entitlement is granted and the purchaser sees no false
      "unlocked" state anywhere in the app.
- [x] AC-08: Given the family-plan subscription's lifecycle events arrive via
      webhook, when each is processed, then: an `invoice.paid` (renewal) event
      extends the grant's `validThrough` to the new billing period's end; a
      `past_due` status extends `validThrough` by a ~7-day grace window (ADR
      0002 Decision D) rather than expiring the grant immediately, so a failed
      card does not lock a family mid-session; and a `canceled` event (or an
      unrecovered `past_due` grace lapsing) lets `validThrough` pass, so the
      family falls back to the generous free tier on the *next*
      session-creation - never a live mid-session revoke (billing-entitlements/01
      AC-03's "session-creation-time only" contract).

## Out of Scope
- The tip jar's own UI and its "grant nothing" business rule (story 02) and
  the gated purchase flow's own UI (story 04) - this story is the shared
  plumbing under both, not either UI.
- The specific price/product -> capability-key(s) mapping (a single `pack.<id>`
  vs. the full family-plan bundle) - that lookup table lives alongside this
  story's `StripeCheckoutService` but its content is story 04's to define and
  extend (billing-entitlements/04, ADR 0002 Decision C).
- Actually moving the webhook handler into an Azure Function - AC-04 only
  requires the handler be *isolated enough* to move later; the move itself is
  out of scope until a real operational reason appears (README section 4's
  stated trigger for carving out Functions).
- Full dunning/retry logic beyond the single ~7-day `past_due` grace window
  (AC-08, Decision D), refunds, chargebacks, disputes, and proration handling
  - parked in feature.md (Phase 3+); this story implements only the four
  lifecycle events named in AC-08, not a general subscription-management
  system.
- Regional pricing, tax (e.g. Stripe Tax), and multi-currency - parked in
  feature.md.
- Building the entitlement *catalog* or the session-creation gate itself, or
  the SignalR access-token wiring that resolves a purchaser identity at
  `GameHub.CreateRoom` (ADR 0002 Decision F) - that is billing-entitlements/01
  + accounts-identity's job; this story only writes into the grant store
  billing-entitlements/01 defines, keyed to whatever identity arrives there.

## Technical Notes
- New `api/src/Billing/` folder (mirrors the existing per-concern layout).
  A `StripeCheckoutService` (creates Checkout Sessions for both modes per
  AC-02) and a `StripeWebhookHandler` (verifies signature, resolves the event
  to a purchaser + capability grant, calls into
  `IEntitlementService`/the grant-store write path from
  billing-entitlements/01). Register via `Program.cs` following the existing
  singleton/DI pattern; the Stripe secret key and webhook signing secret are
  pulled from configuration backed by Key Vault (see
  `infra/main.bicep`'s `keyVault` resource) - do not hardcode or read from
  `appsettings.json` in plaintext.
- The webhook endpoint is a REST controller (`api/src/Controllers/`,
  e.g. `StripeWebhookController`) mapped alongside the existing controllers in
  `Program.cs` (`app.MapControllers()`) - it is request/response, not a hub
  method, since Stripe calls it directly over HTTP.
- Idempotency (AC-05): persist the processed Stripe event id (Stripe includes
  one per event) as part of the entitlement-grant write, and check-before-write
  so a re-delivered webhook is a no-op on the second attempt. This can live as
  a column/property on the same Table Storage entity written by
  billing-entitlements/01's `EntitlementGrant`, or a small companion
  "processed events" table - keep it simple, this is a toy (CLAUDE.md section
  10), not a full outbox pattern.
- Both Checkout Session modes (`mode: "payment"` for one-time,
  `mode: "subscription"` for recurring) should flow through one
  `StripeCheckoutService` method parameterized by what is being purchased,
  not two separate services - this is the "same billing plumbing" README
  section 3 asks for.
- **Subscription lifecycle (AC-08, ADR 0002 Decisions C/D):** the webhook
  handler mutates the SAME `EntitlementGrant` row shape billing-entitlements/01
  defines (`validThrough` + `source = subscription`):
  - `invoice.paid` (renewal) -> extend `validThrough` to the new period end.
  - the subscription's status transitioning to `past_due` -> extend
    `validThrough` by a small config constant (~7 days) rather than expiring
    the grant - a lapsed card should not lock a family mid-car-ride.
  - `customer.subscription.deleted` (canceled), or an unrecovered `past_due`
    grace window lapsing -> let `validThrough` pass (or set it to now) so the
    grant reads as expired the next time billing-entitlements/01's
    session-creation check reads it. This is a read-time consequence, never a
    live mid-session push to an open room.
- The purchaser identity a grant is keyed to is the SAME value ADR 0002
  Decision F resolves at `GameHub.CreateRoom` (the SignalR access-token-on-connect
  proof) and billing-entitlements/01's `EvaluateForSession` reads - this story
  does not add a second identity-resolution path.
- The Functions carve-out note (AC-04) is a code-organization discipline, not
  new infrastructure: no Azure Function project is created by this story.
  `infra/main.bicep`'s five-resource footprint (README section 9) is
  unchanged.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: verified - config/secret audit confirms the Stripe key is sourced from Key Vault-backed configuration; repo and build output contain no literal key value.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Billing/StripeCheckoutServiceTests.cs::Payment_mode_builds_a_payment_session_with_metadata` and `::Subscription_mode_builds_a_subscription_session_and_stamps_subscription_metadata` - both modes created via the same `StripeCheckoutService`. Also verified LIVE against Stripe test-mode: a real Checkout Session was created end to end.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Billing/StripeWebhookHandlerTests.cs::Checkout_one_time_grants_a_permanent_capability_readable_via_the_gate`, plus `tests/QuibbleStone.Api.Tests/Billing/StripeWebhookControllerTests.cs::Tampered_signature_is_rejected_and_grants_nothing`. Also verified LIVE: a real signed Stripe test-mode webhook was processed and the grant written.` |
| AC-04 | `manual: verified - the handler (StripeWebhookHandler + StripeWebhookController in api/src/Billing/) is a single, self-contained class/route with no cross-cutting dependencies blocking later extraction into a Function.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Billing/StripeWebhookHandlerTests.cs::Replaying_the_same_event_id_is_idempotent` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Billing/StripeWebhookHandlerTests.cs::Checkout_one_time_grants_a_permanent_capability_readable_via_the_gate` and `::Checkout_without_a_purchaser_grants_nothing`.` |
| AC-07 | `manual: verified - a failed/abandoned Stripe test-mode checkout writes no entitlement row and the app shows no false-unlocked state.` |
| AC-08 | `tests/QuibbleStone.Api.Tests/Billing/StripeWebhookHandlerTests.cs::Renewal_extends_the_lease_to_the_new_period_end`, `::PastDue_extends_by_grace_and_never_shortens`, `::Cancel_lapses_the_lease_at_next_read`.` |

## Dependencies
- billing-entitlements/01 (#70) - the grant store shape (`EntitlementGrant`,
  `validThrough` + `source`) this story's webhook writes into.
- accounts-identity/02 (#68) - the purchaser identity entitlements are keyed to.
- infra (Key Vault and Table Storage already provisioned per README section
  9).
