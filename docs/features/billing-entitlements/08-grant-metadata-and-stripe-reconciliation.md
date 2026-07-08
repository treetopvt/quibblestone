# Story: Grant metadata + Stripe reconciliation

**Feature:** Billing & Entitlements  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) Layer 2 names this feature's half
of "recovery and support data": `EntitlementGrant` today is a bare
`(CapabilityKey, ValidThrough?, Source)` lease (`api/src/Entitlements/EntitlementGrant.cs`) - it
carries no grant id, no product/plan id, and no Stripe subscription id, so there is no way to tell
"which purchase produced this row" or "which live Stripe subscription this lease tracks." Worse,
webhooks are trusted with no reconciliation path: if an event is missed (Stripe redelivers
at-least-once but a sustained outage can still drop one), or an operator edits a subscription
directly in the Stripe dashboard, the grant silently drifts from Stripe's authoritative state and
nothing short of a manual Table Storage edit fixes it.

This story closes both gaps. `EntitlementGrant` gains `GrantId`, `PlanId`, and
`StripeSubscriptionId`; `StripeWebhookHandler` (billing-entitlements/03) populates all three on
every write; and a new per-account resync service becomes the recovery path
`sysadmin-console/07`'s support verb ("per-account Stripe resync") calls. Webhooks remain the
routine source of truth - resync is an operator-triggered recovery action, never a scheduled job
or a silent per-request check (README section 3's "not per-request" holds here too). See
[feature.md](./feature.md) and ADR 0003's Layer 2 + Layer 3 sections.

**Dependency note (accounts-identity/05):** ADR 0003's Layer 0 mints a stable `AccountId` (GUID)
so grants key off a durable id instead of an email-hash. That is `accounts-identity/05`, not this
story. This story's resync piece works either way - see Technical Notes' degraded path - but
building it before `accounts-identity/05` lands means it resolves the Stripe customer by email
(today's only identity) rather than by `AccountId`; prefer scheduling `accounts-identity/05` first
so this story is not built twice.

## Acceptance Criteria
- [ ] AC-01 (grant carries metadata): Given `EntitlementGrant`, then it carries a `GrantId` (a
      fresh GUID stamped at write time - identifies THIS write, not the whole lease history), a
      nullable `PlanId` (the `ProductCatalog` product id that produced the grant, e.g.
      `"family-plan"` or `"pack.spooky"`; null for a grant with no known product, e.g. a legacy row
      or a bespoke operator comp), and a nullable `StripeSubscriptionId` (the Stripe subscription
      id for a subscription-sourced grant; null for a one-time pack or an operator grant).
- [ ] AC-02 (webhook populates it): Given `StripeWebhookHandler` processes a `CheckoutCompleted`,
      `SubscriptionRenewed`, `SubscriptionPastDue`, or `SubscriptionCanceled` event, then every
      `EntitlementGrant` it writes has a freshly-minted `GrantId`, a `PlanId` resolved from the
      checkout/subscription metadata's stamped product id, and (for a subscription) the Stripe
      subscription id carried through from the event - never left null when Stripe supplied one.
- [ ] AC-03 (back-compat read): Given a grant row written by the already-shipped code (no
      `GrantId`/`PlanId`/`StripeSubscriptionId` columns), when it is read, then it deserializes
      without error - `GrantId` defaults to a fresh value rather than throwing, `PlanId` and
      `StripeSubscriptionId` default to null - and the row's lease/capability behavior
      (`IsActiveAt`) is byte-for-byte unchanged from today.
- [ ] AC-04 (per-account resync, operator-triggered): Given an operator invokes the resync
      endpoint with a purchaser email, when it runs, then it looks up that email's Stripe
      customer(s) and subscriptions in the currently ACTIVE Stripe mode
      (billing-entitlements/06) and rewrites each subscription-sourced `EntitlementGrant`
      (capability key, lease end, `Source = Subscription`, `PlanId`, `StripeSubscriptionId`) to
      match Stripe's current, authoritative state - correcting drift left by a missed webhook or a
      dashboard-side edit.
- [ ] AC-05 (one-time grants untouched): Given a purchaser holds a one-time pack grant
      (permanent, `ValidThrough = null`, no Stripe subscription), when a resync runs for that
      purchaser, then the one-time grant is left exactly as it was - Stripe has no ongoing
      "active" state to reconcile a one-time purchase against, so resync only ever rewrites
      subscription-sourced grants.
- [ ] AC-06 (operator-only, idempotent, no automatic run): Given the resync endpoint, then it (a)
      is reachable only behind the `Operator` authorization policy
      (`sysadmin-console/01`'s `[Authorize(Policy = "Operator")]`), never from the kid PWA or any
      player-facing route; (b) is idempotent - invoking it twice in a row against the same Stripe
      state produces the same grants, no duplicate rows; and (c) never runs on a schedule or as a
      side effect of any other request - webhooks remain the routine source of truth, resync is a
      manual recovery action only.
- [ ] AC-07 (anonymity invariant): Given a resync run, then it operates solely on the purchaser
      plane (email in, Stripe customer/subscription lookups, grant rows out) - it never looks up,
      joins, or displays any player nickname, room code, or session id, matching the same boundary
      `sysadmin-console/02`'s grant/revoke endpoints already hold.

## Out of Scope
- The `AccountId` re-keying itself (moving grants/vault/tales off the email-hash partition key
  onto a stable GUID) - that is `accounts-identity/05`; this story's resync works against whichever
  identity scheme is live at build time (see Technical Notes' degraded path).
- The `sysadmin-console/07` support-lookup screen/UI itself - this story ships the resync
  service + the protected endpoint it calls; the console screen that surfaces a "resync from
  Stripe" button is that story's job.
- Bulk / all-purchaser resync, or a scheduled/periodic reconciliation sweep - one purchaser,
  operator-triggered, per invocation.
- Refunds, chargebacks, proration, or any write back to Stripe - this story only reads Stripe and
  rewrites the local grant store; it never mutates a Stripe subscription.
- A grant history / audit trail of every resync (ADR 0003's minimal operator action log is
  `sysadmin-console`'s Decision 3 scope, not this story's) - this story's own idempotent overwrite
  is enough; it does not itself grow an audit table.
- Resolving a purchaser with multiple Stripe customer records for the same email (a rare Stripe
  edge case) beyond a documented "most-recent, non-deleted customer wins" tiebreak - a fuller
  merge policy is a future refinement if it is ever actually hit.

## Technical Notes
- **Files this story edits (ground truth, read first):**
  - `api/src/Entitlements/EntitlementGrant.cs` - add `GrantId` (`Guid`), `PlanId` (`string?`),
    `StripeSubscriptionId` (`string?`) to the record. `IsActiveAt` is unchanged.
  - `api/src/Entitlements/TableStorageEntitlementGrantStore.cs` - add three columns
    (`GrantId`, `PlanId`, `StripeSubscriptionId`) to `PutGrantAsync`'s `TableEntity` and to
    `FromEntity`'s reconstruction. Mirror the existing defensive pattern used for `Source`
    (`FromEntity` already degrades a missing/unparseable `Source` to `OneTime` with a
    `LogWarning` rather than throwing) - a missing `GrantId` column mints a fresh `Guid.NewGuid()`
    on read (AC-03), a missing `PlanId`/`StripeSubscriptionId` reads as null. No new NuGet
    dependency; this is an additive column change to an existing entity.
  - `api/src/Billing/BillingEvent.cs` - add `PlanId` (`string?`) and `StripeSubscriptionId`
    (`string?`) to the record so the domain handler has them to write without any Stripe SDK
    knowledge leaking past the mapper.
  - `api/src/Billing/StripeEventMapper.cs` - the ONE place that must learn to read a new
    `qs_product` metadata key (see below) and the Stripe subscription id off each event shape:
    `Session.Subscription` (a `Subscription` id string on `checkout.session.completed` in
    subscription mode), `Invoice.SubscriptionId` (`invoice.paid`), and `Subscription.Id` directly
    (`customer.subscription.updated` / `.deleted`).
  - `api/src/Billing/IStripeCheckoutService.cs` (`BillingMetadata`) - add a `ProductKey` metadata
    constant (`"qs_product"`) alongside the existing `CapabilitiesKey`/`PurchaserKey`, so the
    product id rides into Stripe the same way capabilities already do.
  - `api/src/Billing/CheckoutModels.cs` (`CheckoutRequest`) - add a `ProductId` field so
    `StripeCheckoutService.BuildSessionOptions` can stamp `qs_product` onto both the session and
    (for a subscription) the subscription metadata, exactly like it already does for
    `CapabilitiesKey`/`PurchaserKey`. Callers (billing-entitlements/02, /04) pass the
    `ProductCatalog` product id they are already resolving a price id from - a one-line addition
    at each call site, not a new lookup.
  - `api/src/Billing/StripeWebhookHandler.cs` - thread `PlanId`/`StripeSubscriptionId` from the
    `BillingEvent` into every `new EntitlementGrant(...)` construction (all three write sites:
    checkout/renewal, past-due, canceled), alongside a freshly-minted `GrantId`.
  - New: an `IStripeReconciliationService` (or similarly named) + its live implementation in
    `api/src/Billing/`, and a new admin endpoint (alongside `sysadmin-console`'s
    `AdminEntitlementsController` pattern from `sysadmin-console/02`, or a new
    `AdminBillingResyncController` - either is fine, but reuse the SAME `[Authorize(Policy =
    "Operator")]` policy, not a new gate).
- **The resync service, concretely.** Given a purchaser email: (1) resolve the Stripe customer via
  `CustomerService.List` filtered by email against the ACTIVE mode's client
  (`IActiveStripeContext`, billing-entitlements/06 - reuse it, do not build a second
  mode-resolution path); Stripe's customer list-by-email is an exact match, so this is a single
  call, not a search/scan. (2) For that customer, `SubscriptionService.List` (filtered by
  customer) to get every subscription. (3) For each subscription, read `qs_capabilities` +
  `qs_product` off its metadata (the same `BillingMetadata` keys the checkout already stamps) and
  its `Status`/`CurrentPeriodEnd`, and reuse `StripeWebhookHandler`'s EXISTING lease math (extract
  the `ResolveLeaseEnd`/past-due-grace logic into a small shared helper both the handler and the
  resync service call, rather than duplicating it) to compute the grant each capability key should
  have right now. (4) `PutGrantAsync` each one (an idempotent upsert - AC-06's idempotency falls
  out of reusing the same upsert-by-capability-key write path story 01 already defined). A
  subscription with no recognizable metadata (should not happen post-this-story, but is possible
  for a pre-existing live subscription created before `qs_product` existed) is skipped with a
  `LogWarning`, never guessed at.
- **Degraded path if built before `accounts-identity/05`.** Without a stable `AccountId`, "resolve
  the Stripe customer for this account" is "resolve the Stripe customer for this email" - exactly
  what AC-04 already specifies, so the story is fully buildable either way. Once `accounts-identity/
  05` lands, the resync endpoint's input becomes the `AccountId` (resolved to its current email
  internally) rather than a raw email - a small, contract-compatible parameter swap, not a rewrite.
  Note this explicitly in the endpoint's header comment so the swap is not forgotten.
- **`sysadmin-console/07` is the consumer, not a blocker.** This story ships the endpoint +
  service; `sysadmin-console/07`'s support lookup screen (ADR 0003 Layer 3, "per-account Stripe
  resync" verb) is the first UI caller. Build this story so it is independently testable via the
  endpoint directly (e.g. `curl` / an integration test) without waiting on that console screen.
- **Reuse, do not reinvent:** `IActiveStripeContext` (billing-entitlements/06) for the active
  mode's Stripe client; `IAccountStore` (accounts-identity/02) to confirm the purchaser account
  exists before writing grants (mirrors `StripeWebhookHandler`'s `CreateOrGetAsync` pattern); the
  `Operator` authorization policy (`sysadmin-console/01`); the existing
  `TableStorageEntitlementGrantStore` / `InMemoryEntitlementGrantStore` pairing (no new store
  type, just new columns).

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Entitlements/EntitlementGrantTests.cs (new/extended): constructing a grant with GrantId/PlanId/StripeSubscriptionId round-trips through TableStorageEntitlementGrantStore (or its test double) unchanged.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Billing/StripeWebhookHandlerTests.cs (extended): a CheckoutCompleted/SubscriptionRenewed/SubscriptionPastDue/SubscriptionCanceled BillingEvent carrying a PlanId + StripeSubscriptionId writes a grant with both populated and a non-empty GrantId.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Entitlements/TableStorageEntitlementGrantStoreTests.cs: reading a TableEntity written without the new columns (simulating a pre-story row) returns a grant with a defaulted GrantId and null PlanId/StripeSubscriptionId, and IsActiveAt behaves identically to before.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/Billing/StripeReconciliationServiceTests.cs (new): given a fake Stripe customer/subscription list for an email, resync overwrites the stored grant's ValidThrough/PlanId/StripeSubscriptionId to match the fake subscription's status/period end.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Billing/StripeReconciliationServiceTests.cs: a purchaser holding only a one-time pack grant is unchanged by a resync run (no Stripe subscription exists to reconcile against).` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Billing/StripeReconciliationEndpointTests.cs (new): a non-operator/unauthenticated request is rejected; invoking resync twice in a row against the same fake Stripe state produces no duplicate grant rows.` |
| AC-07 | `manual: code review + endpoint audit - confirm no player/room/session field is ever queried, joined, or returned by the resync endpoint, mirroring sysadmin-console/02's AC-04 audit.` |

## Dependencies
- `billing-entitlements/01` (#70) - the `EntitlementGrant` record + grant store this story extends.
- `billing-entitlements/03` (#72) - `StripeWebhookHandler` / `StripeEventMapper` / `BillingEvent`,
  which this story edits to populate the new fields.
- `billing-entitlements/06` (TBD) - `IActiveStripeContext`, reused for the resync service's
  active-mode Stripe client.
- `accounts-identity/02` (#68) - `IAccountStore`, the purchaser account this story looks up by
  email.
- `accounts-identity/05` (soft, per ADR 0003 Layer 0) - the `AccountId` spine; not a hard
  blocker (see Technical Notes' degraded path), but preferred to land first so the resync input
  is not built twice.
- `sysadmin-console/01` (#135) - the `Operator` authorization policy this story's endpoint sits
  behind.
- `sysadmin-console/07` (ADR 0003 Layer 3, not yet decomposed) - the future console consumer of
  this story's endpoint; not a blocker to build or ship this story.
