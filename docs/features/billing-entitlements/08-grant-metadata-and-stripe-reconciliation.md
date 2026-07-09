# Story: Grant metadata + Stripe reconciliation

**Feature:** Billing & Entitlements  ·  **Status:** Not Started  ·  **Issue:** #215

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

This story closes both gaps. `EntitlementGrant` gains `GrantId`, `PlanId`, `StripeSubscriptionId`,
and `Mode`; `StripeWebhookHandler` (billing-entitlements/03) populates all four on every write; and
a new per-account resync service becomes the recovery path `sysadmin-console/07`'s support verb
("per-account Stripe resync") calls. Webhooks remain the routine source of truth - resync is an
operator-triggered recovery action, never a scheduled job or a silent per-request check (README
section 3's "not per-request" holds here too). See [feature.md](./feature.md) and ADR 0003's Layer
2 + Layer 3 sections.

**Revised 2026-07-08 after the adversarial review ("Stripe resync cannot corrupt grants," ADR 0003
Security posture).** The original design read Stripe in "whichever mode is currently active" and
picked the matching Stripe customer by an email tiebreak ("most-recent, non-deleted customer
wins"). The review found two concrete problems with that: (1) the grant store is a SINGLE store,
not partitioned by Stripe mode - a resync run while Test mode happens to be active would read
Test-mode Stripe data and overwrite whatever is stored, including a grant a real Live subscription
produced; and (2) an attacker can create their OWN Stripe customer object under a victim's email
address (Stripe does not verify customer emails) - the "most-recent customer wins" tiebreak is
therefore steerable, letting an attacker's self-created customer be picked over the victim's real
one. Both are fixed below (a `Mode` dimension on the grant row + a metadata-matched identity check
that ignores the Stripe customer's bare `Email` field), and a third gap the review also flagged -
the resync endpoint fanning out unthrottled `CustomerService.List` + `SubscriptionService.List`
calls against Stripe - gets a rate limiter. See AC-04, AC-06, and AC-08.

**Dependency note (accounts-identity/05):** ADR 0003's Layer 0 mints a stable `AccountId` (GUID)
so grants key off a durable id instead of an email-hash. That is `accounts-identity/05`, not this
story. This story's resync piece works either way - see Technical Notes' degraded path - but
building it before `accounts-identity/05` lands means the resync's target identity is resolved via
`IAccountStore`'s existing email key rather than an `AccountId`; prefer scheduling `accounts-
identity/05` first so this story's identity-resolution plumbing is written once, not twice.

## Acceptance Criteria
- [ ] AC-01 (grant carries metadata): Given `EntitlementGrant`, then it carries a `GrantId` (a
      fresh GUID stamped at write time - identifies THIS write, not the whole lease history), a
      nullable `PlanId` (the `ProductCatalog` product id that produced the grant, e.g.
      `"family-plan"` or `"pack.spooky"`; null for a grant with no known product, e.g. a legacy row
      or a bespoke operator comp), a nullable `StripeSubscriptionId` (the Stripe subscription id
      for a subscription-sourced grant; null for a one-time pack or an operator grant), and a
      nullable `Mode` (`StripeMode?` - `Live` or `Test`, the Stripe mode that produced this grant;
      null only for a `Source = Operator` comp, which has no Stripe transaction behind it at all).
- [ ] AC-02 (webhook populates it): Given `StripeWebhookHandler` processes a `CheckoutCompleted`,
      `SubscriptionRenewed`, `SubscriptionPastDue`, or `SubscriptionCanceled` event, then every
      `EntitlementGrant` it writes has a freshly-minted `GrantId`, a `PlanId` resolved from the
      checkout/subscription metadata's stamped product id, the Stripe subscription id carried
      through from the event (for a subscription - never left null when Stripe supplied one), and
      `Mode` set to the ACTUAL mode that verified this specific webhook's signature (`Live` or
      `Test` - billing-entitlements/06's dual-secret verification already knows which one matched;
      `Mode` is NEVER inferred from "whichever mode happens to be currently active," since the two
      can differ and a webhook must record its own true provenance).
- [ ] AC-03 (back-compat read): Given a grant row written by the already-shipped code (no
      `GrantId`/`PlanId`/`StripeSubscriptionId`/`Mode` columns), when it is read, then it
      deserializes without error - `GrantId` defaults to a fresh value rather than throwing,
      `PlanId` and `StripeSubscriptionId` default to null, `Mode` defaults to `Test` (every grant
      ever written by the shipped code was produced while Stripe Live has never been active in
      this environment - ADR 0003 Decision 4: "Stripe live waits for Layers 0-1" - so `Test` is the
      factually correct default, not merely a safe guess) - and the row's lease/capability behavior
      (`IsActiveAt`) is byte-for-byte unchanged from today.
- [ ] AC-04 (per-account resync, identity-verified, never email-steerable): Given an operator
      invokes the resync endpoint for a target account (identified by `AccountId` once
      `accounts-identity/05` lands, or by the account's stored canonical email - resolved through
      `IAccountStore`, never a raw ad hoc string typed at call time - before then), when it runs,
      then it (a) lists Stripe customers in the currently ACTIVE Stripe mode whose `Email` matches
      the account's registered email as CANDIDATES ONLY, never a single "winner"; (b) for each
      candidate customer's subscriptions, reconciles ONLY a subscription whose `qs_purchaser`
      metadata (the same value `StripeCheckoutService` stamped at checkout time for THIS account)
      equals the account's identity - a subscription reachable only via a bare `Email` match on the
      Stripe customer object, with no matching `qs_purchaser` metadata, is SKIPPED and logged, never
      trusted (this is what makes the fix effective: an attacker's self-created Stripe customer
      under a victim's email carries no `qs_purchaser` metadata this app ever stamped, so it is
      never picked); and (c) for a matching subscription, writes/overwrites the corresponding
      `EntitlementGrant` (capability key, lease end, `Source = Subscription`, `PlanId`,
      `StripeSubscriptionId`, `Mode = the active mode`) subject to AC-08's mode-safety guard.
- [ ] AC-05 (one-time grants untouched): Given a purchaser holds a one-time pack grant
      (permanent, `ValidThrough = null`, no Stripe subscription), when a resync runs for that
      purchaser, then the one-time grant is left exactly as it was - Stripe has no ongoing
      "active" state to reconcile a one-time purchase against, so resync only ever rewrites
      subscription-sourced grants (and never an operator-comp grant either, for the same reason).
- [ ] AC-06 (operator-only, idempotent, no automatic run, rate-limited): Given the resync endpoint,
      then it (a) is reachable only behind the `Operator` authorization policy
      (`sysadmin-console/01`'s `[Authorize(Policy = "Operator")]`), never from the kid PWA or any
      player-facing route; (b) is idempotent - invoking it twice in a row against the same Stripe
      state produces the same grants, no duplicate rows; (c) never runs on a schedule or as a
      side effect of any other request - webhooks remain the routine source of truth, resync is a
      manual recovery action only; and (d) is rate-limited (a fixed-window limiter, mirroring the
      existing `OperatorLoginRateLimit`/`CloudGalleryRateLimit` pattern) so a scripted or
      accidental loop of resync calls cannot fan out unbounded `CustomerService.List` +
      `SubscriptionService.List` traffic against Stripe's API and disrupt concurrent live webhook
      processing - a request beyond the limit is rejected with 429, the same posture as every other
      throttled endpoint in the app.
- [ ] AC-07 (anonymity invariant): Given a resync run, then it operates solely on the purchaser
      plane (account identity in, Stripe customer/subscription lookups, grant rows out) - it never
      looks up, joins, or displays any player nickname, room code, or session id, matching the same
      boundary `sysadmin-console/02`'s grant/revoke endpoints already hold.
- [ ] AC-08 (mode-safety: a Test-mode resync can never modify a Live-derived grant): Given the
      grant store's per-row `Mode`, when a resync runs, then it compares the CURRENTLY ACTIVE mode
      against each existing subscription-sourced grant row's stored `Mode` BEFORE writing to it -
      a row whose stored `Mode` differs from the active mode (e.g. a `Live`-mode grant encountered
      while `Test` mode is active, or vice versa) is left completely untouched (not overwritten,
      not deleted, not read for any purpose beyond this comparison) and the skip is logged; only a
      row whose stored `Mode` matches the active mode - or that does not yet exist for this
      capability - is written. This holds symmetrically in both directions: a Test-mode resync can
      never overwrite a Live-derived grant, and a Live-mode resync can never overwrite a
      Test-derived one.

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
- A full multi-Stripe-customer MERGE policy for the rare case where an account legitimately owns
  more than one Stripe customer record carrying valid `qs_purchaser` metadata for the same
  identity (e.g. two separate checkouts) - AC-04's per-candidate metadata match already prevents
  the ATTACKER-steerable case (a spoofed customer with no matching metadata is never picked); a
  fuller policy for reconciling two GENUINELY valid customers is a future refinement if it is ever
  actually hit. The earlier "most-recent, non-deleted customer wins" EMAIL-ONLY tiebreak is
  explicitly retired by this revision - it is not carried forward in any form.

## Technical Notes
- **Files this story edits (ground truth, read first):**
  - `api/src/Entitlements/EntitlementGrant.cs` - add `GrantId` (`Guid`), `PlanId` (`string?`),
    `StripeSubscriptionId` (`string?`), and `Mode` (`StripeMode?`, from
    `api/src/Billing/StripeMode.cs` - reuse the existing enum, do not add a second one) to the
    record. `IsActiveAt` is unchanged.
  - `api/src/Entitlements/TableStorageEntitlementGrantStore.cs` - add four columns (`GrantId`,
    `PlanId`, `StripeSubscriptionId`, `Mode`) to `PutGrantAsync`'s `TableEntity` and to
    `FromEntity`'s reconstruction. Mirror the existing defensive pattern used for `Source`
    (`FromEntity` already degrades a missing/unparseable `Source` to `OneTime` with a
    `LogWarning` rather than throwing) - a missing `GrantId` column mints a fresh `Guid.NewGuid()`
    on read (AC-03), a missing `PlanId`/`StripeSubscriptionId` reads as null, and a missing `Mode`
    reads as `StripeMode.Test` (AC-03 - this is the FACTUAL default, not a placeholder: no grant in
    this store predates Stripe Live ever going active). No new NuGet dependency; this is an
    additive column change to an existing entity.
  - `api/src/Billing/BillingEvent.cs` - add `PlanId` (`string?`) and `StripeSubscriptionId`
    (`string?`) to the record so the domain handler has them to write without any Stripe SDK
    knowledge leaking past the mapper. `Mode` does not need to ride on `BillingEvent` itself - the
    webhook handler already knows which secret verified the event at the point it constructs the
    `EntitlementGrant` (billing-entitlements/06), so it stamps `Mode` directly there.
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
    checkout/renewal, past-due, canceled), alongside a freshly-minted `GrantId` and the `Mode` that
    verified this specific event.
  - New: an `IStripeReconciliationService` (or similarly named) + its live implementation in
    `api/src/Billing/`, and a new admin endpoint (alongside `sysadmin-console`'s
    `AdminEntitlementsController` pattern from `sysadmin-console/02`, or a new
    `AdminBillingResyncController` - either is fine, but reuse the SAME `[Authorize(Policy =
    "Operator")]` policy, not a new gate).
  - New: `api/src/Billing/StripeResyncRateLimit.cs` - a fixed-window rate-limit policy for the
    resync endpoint, mirroring `api/src/Admin/OperatorLoginRateLimit.cs` and
    `api/src/CloudGallery/CloudGalleryRateLimit.cs`'s shape (a `PolicyName`, a `PermitLimit`, a
    `Window`, a partition-key function), registered in `Program.cs`'s rate-limiter section and
    opted into via `[EnableRateLimiting(StripeResyncRateLimit.PolicyName)]` on the endpoint action.
    Partition key: a CONSTANT global key, not the caller's IP - the abuse scenario here is
    repeated invocation against Stripe's API (which the operator-only auth already scopes to a
    single trusted actor), not distinct callers, so the whole endpoint shares one small budget
    (e.g. 5 calls per 5 minutes) rather than each IP getting its own.
- **The resync service, concretely (revised 2026-07-08):** Given a target account identity (an
  `AccountId` once `accounts-identity/05` lands, else the account's canonical email resolved via
  `IAccountStore.GetByIdentityAsync` - never a bare string taken directly from the request without
  confirming the account exists): (1) resolve Stripe customers via `CustomerService.List` filtered
  by the account's registered email against the ACTIVE mode's client (`IActiveStripeContext`,
  billing-entitlements/06 - reuse it, do not build a second mode-resolution path); treat the
  result as a CANDIDATE SET, never a single "winner" (this is the fix for the email-steering
  finding - see AC-04). (2) For EACH candidate customer, `SubscriptionService.List` (filtered by
  customer) to get its subscriptions. (3) For each subscription, check its `qs_purchaser` metadata
  equals the target account's identity value EXACTLY (the same value `StripeCheckoutService`
  stamped at checkout time, per `BillingMetadata.PurchaserKey`) - a subscription with no matching
  `qs_purchaser` is SKIPPED (this is what makes an attacker's self-created customer, which never
  went through our checkout and so never carries this metadata, un-pickable). (4) For a matching
  subscription, read `qs_capabilities` + `qs_product` off its metadata and its
  `Status`/`CurrentPeriodEnd`, and reuse `StripeWebhookHandler`'s EXISTING lease math (extract the
  `ResolveLeaseEnd`/past-due-grace logic into a small shared helper both the handler and the
  resync service call, rather than duplicating it) to compute the grant each capability key should
  have right now. (5) BEFORE writing, apply AC-08's mode guard: read the existing grant row (if
  any) for that capability key and compare its stored `Mode` to the active mode; a mismatch means
  SKIP + log, never write. (6) `PutGrantAsync` each surviving one (an idempotent upsert - AC-06's
  idempotency falls out of reusing the same upsert-by-capability-key write path story 01 already
  defined), stamping `Mode = the active mode`. A subscription with no recognizable metadata (should
  not happen post-this-story, but is possible for a pre-existing live subscription created before
  `qs_product`/`qs_purchaser` existed) is skipped with a `LogWarning`, never guessed at.
- **Degraded path if built before `accounts-identity/05`.** Without a stable `AccountId`, the
  resync's target identity is the account's canonical email, resolved through `IAccountStore` (not
  a raw string). The `qs_purchaser`-metadata match in step (3) above works identically either way,
  because it compares against WHATEVER identity value `StripeCheckoutService` stamped at checkout
  time for that account - email today, an `AccountId` once the re-key and its consumers land. Once
  `accounts-identity/05` lands, the resync endpoint's input becomes the `AccountId` rather than a
  raw email - a small, contract-compatible parameter swap, not a rewrite. Note this explicitly in
  the endpoint's header comment so the swap is not forgotten.
- **`sysadmin-console/07` is the consumer, not a blocker.** This story ships the endpoint +
  service; `sysadmin-console/07`'s support lookup screen (ADR 0003 Layer 3, "per-account Stripe
  resync" verb) is the first UI caller. Build this story so it is independently testable via the
  endpoint directly (e.g. `curl` / an integration test) without waiting on that console screen.
- **Reuse, do not reinvent:** `IActiveStripeContext` (billing-entitlements/06) for the active
  mode's Stripe client and the `StripeMode` enum it already defines; `IAccountStore`
  (accounts-identity/02) to confirm the purchaser account exists and to resolve its canonical
  identity before writing grants (mirrors `StripeWebhookHandler`'s `CreateOrGetAsync` pattern); the
  `Operator` authorization policy (`sysadmin-console/01`); the existing rate-limiter registration
  pattern (`OperatorLoginRateLimit`, `CloudGalleryRateLimit`); the existing
  `TableStorageEntitlementGrantStore` / `InMemoryEntitlementGrantStore` pairing (no new store
  type, just new columns).

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Entitlements/EntitlementGrantTests.cs (new/extended): constructing a grant with GrantId/PlanId/StripeSubscriptionId/Mode round-trips through TableStorageEntitlementGrantStore (or its test double) unchanged.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Billing/StripeWebhookHandlerTests.cs (extended): a CheckoutCompleted/SubscriptionRenewed/SubscriptionPastDue/SubscriptionCanceled BillingEvent carrying a PlanId + StripeSubscriptionId writes a grant with both populated, a non-empty GrantId, and Mode set to the mode that verified the event (not the currently-active mode, when the two are made to differ in the test).` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Entitlements/TableStorageEntitlementGrantStoreTests.cs: reading a TableEntity written without the new columns (simulating a pre-story row) returns a grant with a defaulted GrantId, null PlanId/StripeSubscriptionId, Mode defaulted to Test, and IsActiveAt behaves identically to before.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/Billing/StripeReconciliationServiceTests.cs (new): given TWO fake Stripe customers sharing an email - one with matching qs_purchaser metadata, one without (simulating an attacker-created customer) - resync reconciles only the metadata-matching one's subscription and never touches/reads the other's data for a write.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Billing/StripeReconciliationServiceTests.cs: a purchaser holding only a one-time pack grant is unchanged by a resync run (no Stripe subscription exists to reconcile against).` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Billing/StripeReconciliationEndpointTests.cs (new): a non-operator/unauthenticated request is rejected; invoking resync twice in a row against the same fake Stripe state produces no duplicate grant rows; a burst of calls beyond StripeResyncRateLimit's PermitLimit gets a 429.` |
| AC-07 | `manual: code review + endpoint audit - confirm no player/room/session field is ever queried, joined, or returned by the resync endpoint, mirroring sysadmin-console/02's AC-04 audit.` |
| AC-08 | `tests/QuibbleStone.Api.Tests/Billing/StripeReconciliationServiceTests.cs (new): given a stored grant row with Mode = Live, a resync run while Test mode is ACTIVE leaves that row byte-for-byte unchanged (and logs a skip); symmetrically for a Live-mode resync against a Test-derived row.` |

## Dependencies
- `billing-entitlements/01` (#70) - the `EntitlementGrant` record + grant store this story extends.
- `billing-entitlements/03` (#72) - `StripeWebhookHandler` / `StripeEventMapper` / `BillingEvent`,
  which this story edits to populate the new fields.
- `billing-entitlements/06` (TBD) - `IActiveStripeContext` and the `StripeMode` enum, reused for
  the resync service's active-mode Stripe client and for the `Mode` field's type.
- `accounts-identity/02` (#68) - `IAccountStore`, the purchaser account this story looks up by
  email and resolves the target identity through.
- `accounts-identity/05` (soft, per ADR 0003 Layer 0 - prefer landing first) - the `AccountId`
  spine; not a hard blocker (see Technical Notes' degraded path), but preferred to land first so
  the resync's identity-resolution plumbing is written once against the durable id rather than
  once against email and again against `AccountId`.
- `sysadmin-console/01` (#135) - the `Operator` authorization policy this story's endpoint sits
  behind.
- `sysadmin-console/07` (ADR 0003 Layer 3, not yet decomposed) - the future console consumer of
  this story's endpoint; not a blocker to build or ship this story.
