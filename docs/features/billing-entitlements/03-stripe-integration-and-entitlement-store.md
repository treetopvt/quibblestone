# Story: Stripe integration + entitlement store

**Feature:** Billing & Entitlements  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
The shared server-side plumbing that both the tip jar (story 02) and the
gated purchase flow (story 04) sit on top of: Stripe keys pulled from Azure
Key Vault, a payment-confirmation path via webhook, and the write side of the
entitlement store that billing-entitlements/01's session-creation gate reads
from. Same plumbing supports a one-time pack purchase and a recurring
subscription (README section 3). See [feature.md](./feature.md) and README
section 4 (the Azure Functions carve-out note for Stripe webhooks).

## Acceptance Criteria
- [ ] AC-01: Given the API needs to call Stripe, when it does, then the
      Stripe secret key (and any webhook signing secret) is read from Azure
      Key Vault at startup/request time - it is never present in a
      `VITE_*` variable, a committed config file, or a log line.
- [ ] AC-02: Given a Stripe Checkout Session is created (for a tip, a pack, or
      a subscription), when the flow is invoked, then it supports both a
      one-time payment mode (tip jar, add-on pack) and a recurring
      subscription mode (family plan) through the same underlying service -
      not two parallel integrations.
- [ ] AC-03: Given Stripe confirms a payment (checkout completed / invoice
      paid), when that confirmation arrives, then it is received via a
      webhook endpoint, the webhook signature is verified before any
      processing, and the resulting entitlement (if any - the tip jar grants
      none, per story 02 AC-02) is written to the entitlement store from
      billing-entitlements/01.
- [ ] AC-04: Given the webhook handler is implemented in-app (inside the
      single ASP.NET Core app, per README section 4's "one app to start"), it
      is written as an isolated, single-purpose endpoint/service (its own
      controller or minimal-API route + a dedicated handler class) so lifting
      it into an Azure Function later - the natural first carve-out README
      section 4 names - is a move of that class, not a rewrite.
- [ ] AC-05: Given a webhook event is received twice (Stripe's documented
      at-least-once delivery), when the handler processes it, then the
      resulting entitlement grant is idempotent - processing the same event
      id twice does not double-grant or corrupt the entitlement store.
- [ ] AC-06: Given a purchase completes for a signed-in purchaser
      (accounts-identity/02), when the entitlement is granted, then it is
      written keyed to that purchaser's identity in Azure Table Storage
      (README section 4), matching the read shape billing-entitlements/01's
      session-creation gate expects.
- [ ] AC-07: Given a payment fails or is abandoned mid-checkout, when that
      happens, then no entitlement is granted and the purchaser sees no false
      "unlocked" state anywhere in the app.

## Out of Scope
- The tip jar's own UI and its "grant nothing" business rule (story 02) and
  the gated purchase flow's own UI (story 04) - this story is the shared
  plumbing under both, not either UI.
- Actually moving the webhook handler into an Azure Function - AC-04 only
  requires the handler be *isolated enough* to move later; the move itself is
  out of scope until a real operational reason appears (README section 4's
  stated trigger for carving out Functions).
- Refunds, chargebacks, disputes, and subscription cancellation/proration
  handling - parked in feature.md (Phase 3+).
- Regional pricing, tax (e.g. Stripe Tax), and multi-currency - parked in
  feature.md.
- Building the entitlement *catalog* or the session-creation gate itself -
  that is billing-entitlements/01; this story only writes into the store that
  story defines.

## Technical Notes
- New `api/src/Billing/` folder (mirrors the existing per-concern layout).
  A `StripeCheckoutService` (creates Checkout Sessions for both modes per
  AC-02) and a `StripeWebhookHandler` (verifies signature, resolves the event
  to a purchaser + capability grant, calls into
  `IEntitlementService`/the Table Storage write path from
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
- The Functions carve-out note (AC-04) is a code-organization discipline, not
  new infrastructure: no Azure Function project is created by this story.
  `infra/main.bicep`'s five-resource footprint (README section 9) is
  unchanged.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: config/secret audit - confirm the Stripe key is sourced from Key Vault-backed configuration, grep the repo and build output for the literal key value.` |
| AC-02 | `api/tests/Billing/StripeCheckoutServiceTests.cs (to be created, Stripe test-mode or a mocked client): both a payment-mode and a subscription-mode session are created via the same service.` |
| AC-03 | `api/tests/Billing/StripeWebhookHandlerTests.cs: a valid signed test event grants the expected entitlement; an invalid signature is rejected before any processing.` |
| AC-04 | `manual: code review - confirm the handler is a single, self-contained class/route with no cross-cutting dependencies that would block extraction into a Function.` |
| AC-05 | `api/tests/Billing/StripeWebhookHandlerTests.cs: replaying the same event id a second time does not change the entitlement store's state.` |
| AC-06 | `api/tests/Billing/StripeWebhookHandlerTests.cs (integration-style, Table Storage emulator or fake): the granted entitlement is readable via billing-entitlements/01's session-creation gate for that purchaser.` |
| AC-07 | `manual: simulate a failed/abandoned Stripe test-mode checkout - confirm no entitlement row is written and the app shows no false-unlocked state.` |

## Dependencies
- billing-entitlements/01 (the entitlement store/catalog this story writes
  into).
- accounts-identity/02 (the purchaser identity entitlements are keyed to).
- infra (Key Vault and Table Storage already provisioned per README section
  9).
