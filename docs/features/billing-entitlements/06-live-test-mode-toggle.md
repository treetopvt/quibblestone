# Story: Live/test Stripe mode - mode-aware config + toggle endpoint

**Feature:** Billing & Entitlements  ·  **Status:** Complete (interim operator gate, pending sysadmin-console/01)  ·  **Issue:** TBD

## Context
Today "on" is a single config-presence flip: one Stripe secret key, one webhook signing
secret, applied by the deploy workflow's "Wire Stripe billing (optional)" step
(`.github/workflows/deploy.yml`, gated on `STRIPE_ENABLED`) and documented in
`docs/runbooks/enable-stripe-billing.md`. Switching between live and test values today means
overwriting the two Key Vault secrets and redeploying (the runbook's explicit two-pass
sequence). The owner wants more than that: as paid features are gradually turned on for sale
over time, they want **operator control, at runtime, over which Stripe mode is active** -
without a redeploy - both to exercise the live vs. test flows deliberately and to be able to
pull back to test mode quickly if something looks wrong. The owner has explicitly acknowledged
this is heavier than the current approach and has chosen to build it as a proper feature.

This story is the **server-side half only**: the app holds both a live and a test credential
set at once, a persisted flag (survives restarts, changeable at runtime) says which one is
ACTIVE, and a protected endpoint lets that flag be flipped. It does not build the operator-facing
screen that calls the endpoint - that is story 07, which sits behind this story's endpoint and
behind real operator auth.

**This story is additive to, not a replacement for, the existing runbook.** First-time Stripe
dashboard setup (products, webhook registration, price ids) still happens once per mode exactly
as the runbook describes; what changes is that BOTH modes' credentials can be configured at once,
and which one is active becomes a runtime read instead of a config-presence check.

See [feature.md](./feature.md) and `docs/runbooks/enable-stripe-billing.md`.

## Acceptance Criteria
- [x] AC-01 (mode-aware config shape): Given the API's Stripe configuration, when both a live and
      a test credential set are supplied (secret key, webhook signing secret, and price ids, per
      mode), then the app holds both simultaneously without either overwriting the other - `Stripe`
      configuration is keyed by mode (e.g. a `Live` and a `Test` sub-section) rather than the
      single flat shape `StripeOptions` uses today.
- [x] AC-02 (persisted, runtime-mutable active mode): Given the app is running, when the active
      mode is read, then it comes from a value that (a) survives an app restart/redeploy and
      (b) can be changed without a restart or redeploy - a plain app setting does not satisfy (b)
      (an App Service setting change recycles the app) and is therefore not sufficient on its own;
      the resolved active mode is cached briefly and re-read cheaply (no more than one storage read
      per request path, per Technical Notes).
- [x] AC-03 (checkout uses the active mode's credentials): Given the active mode is Test, when a
      checkout session or webhook verification is attempted, then it uses ONLY the Test secret key
      / signing secret / price ids - never a mix of live and test values in the same operation -
      and symmetrically for Live.
- [x] AC-04 (webhook verifies against the correct mode): Given Stripe sends a webhook event, when
      the signature is verified, then it is checked against the signing secret for the mode that
      event actually came from (live events are signed with the live signing secret, test events
      with the test signing secret) - regardless of which mode is currently ACTIVE for new
      checkouts - so a webhook for an in-flight checkout started under the previously active mode
      is not spuriously rejected after an operator flips the switch mid-flight.
- [x] AC-05 (safe default): Given a fresh environment or a fresh deploy with no active-mode value
      yet persisted, then the resolved active mode defaults to Test, never Live - an unconfigured
      or freshly provisioned environment must never be capable of taking a real charge by default.
- [x] AC-06 (confirmation-gated, operator-only switch): Given a request to change the active mode,
      when it is made, then it (a) requires an operator-scoped credential (see Technical Notes'
      dependency-reality decision on which gate is used until `sysadmin-console/01` lands) and
      (b) is a distinct, explicit action separate from any read of the current mode - there is no
      path by which reading status also changes it, and no path by which an unauthenticated or
      player-scoped caller can change it.
- [x] AC-07 (auditable-enough for a toy, not ceremony): Given the active mode changes, then the
      change records at minimum a timestamp and the new mode value (e.g. as properties on the same
      persisted flag row) so an operator can see when it last changed - full audit-trail ceremony
      (who, from where, history list) is explicitly out of scope (see Out of Scope; CLAUDE.md:
      this is a toy, not a system of record).
- [x] AC-08 (child-safety-adjacent: no player-facing signal leaks the mode): Given any player-
      facing surface (join, lobby, word entry, reveal, the tip jar, the paywall), then none of them
      renders, logs, or otherwise reveals which Stripe mode is active - this is an operator-only
      concern; the existing kid-safe purchase surfaces (billing-entitlements/02, /04) are
      unchanged by this story.

## Out of Scope
- The operator-facing UI to view/flip the mode - that is story 07, which is the first (and, for
  now, only) caller of this story's toggle endpoint.
- Building real operator authentication - `sysadmin-console/01` (#135) owns that; this story
  either depends on it or uses a documented thin interim gate (see Technical Notes) that is
  explicitly temporary.
- Per-request or per-session mode selection, more than two modes, region-based routing, or a
  scheduled/timed switch - parked in feature.md; exactly one ACTIVE mode for the whole app,
  changed manually.
- Rewriting or replacing the existing `STRIPE_ENABLED` config-presence master switch (billing
  on/off at all) - that stays exactly as documented in the runbook; this story only adds mode
  selection ON TOP of billing already being on. If neither mode is configured, billing is off
  exactly as it is today.
- Automatically re-registering Stripe dashboard webhooks or migrating existing Key Vault secret
  names - the runbook's one-time dashboard/Key Vault setup steps still apply per mode; this story
  changes what the app reads at runtime, not how the operator configures Stripe's dashboard.
- A full audit trail / history of every mode change (AC-07 only asks for "last changed, to what,
  when") - parked per CLAUDE.md's "toy, not a system of record."
- Migrating `ProductCatalog` / `BillingController` / `StripeCheckoutService` off their current
  single-`StripeOptions`-instance constructor pattern more than strictly necessary - prefer the
  smallest edit that makes them mode-aware (see Technical Notes) over a broader refactor.

## Technical Notes
- **Dependency reality - the auth gate (AC-06).** The toggle endpoint belongs behind real operator
  auth, but `sysadmin-console/01` (operator login + admin boundary, #135) is specified but
  **unbuilt**, and it in turn depends on `accounts-identity/02` (magic-link, #68), also unbuilt.
  There is no admin page or operator session anywhere in the app today.
  *(Update 2026-07-07: that dependency reality is stale - both have since shipped
  (sysadmin-console/01 via PR #158, accounts-identity/02 via PR #147), so the real Operator scheme
  and admin surface now exist. The remaining follow-up is relocating the billing-mode toggle into
  the operator console behind that real Operator scheme, retiring the interim gate.)*
  Two options, same as
  `sysadmin-console/01`'s own Technical Notes handled an equivalent unbuilt-dependency problem:
  - **(a) Serialize after `sysadmin-console/01`.** Build this story's endpoint requiring
    `[Authorize(Policy = "Operator")]` from day one, scheduled to land once #135 ships.
  - **(b) A thin, explicitly temporary interim gate**, built now: a single server-side operator
    secret (a Key Vault-backed bearer token or shared secret, e.g. `Admin__ModeToggleSecret`,
    compared with a constant-time equality check), required as a header on the toggle endpoint.
    Never a `VITE_*` var; never derived from any player/purchaser data; not wired to any UI in
    this story (story 07 is the first caller and inherits whichever gate is live at build time).

  **Recommendation: (b).** The owner's stated motivation - gradually turning on paid features and
  wanting to exercise live vs. test now - argues for not hard-blocking on two other unbuilt
  features. Build the endpoint behind the thin interim secret, and **write the check as a small,
  swappable interface** (e.g. `IOperatorGate` with one method, `Task<bool> IsAuthorizedAsync(...)`)
  so that when `sysadmin-console/01` lands, swapping the implementation to check the real
  operator-allowlist session is a one-file change, not an endpoint rewrite. Note explicitly in the
  endpoint's header comment that this is a temporary gate pending #135, mirroring the "temporary,
  contract-stable stand-in" pattern `sysadmin-console/01`'s own Technical Notes already uses for
  its dependency on `accounts-identity/02`.
- **Mode-aware options shape (AC-01).** `StripeOptions` today (`api/src/Billing/StripeOptions.cs`)
  is single-mode: flat `SecretKey`, `WebhookSigningSecret`, `PriceIds`. Do not simply duplicate the
  whole class; introduce a small per-mode credential record (secret key, webhook signing secret,
  price ids) and hold two instances (`Live`, `Test`) plus the mode-independent fields that do not
  vary by mode (`PublishableKey` may in fact need to be per-mode too - Stripe's publishable key
  IS mode-specific; `ClientBaseUrl` and `PastDueGraceDays` are not). Exact field layout and whether
  this becomes a new `StripeModeOptions` type or a reshaped `StripeOptions` is an implementation
  decision - the contract this story owes downstream consumers (`StripeCheckoutService`,
  `ProductCatalog`, `StripeWebhookController`, `BillingController`) is a single resolved
  "active mode's credentials" view, so those classes keep reading "the" secret key /
  signing secret / price ids without themselves branching on mode. Prefer wrapping/resolving once
  (e.g. an `IActiveStripeCredentials` or similar thin resolver) over threading a mode enum through
  every constructor.
- **Persisted flag (AC-02, AC-05, AC-07).** Reuse the existing Azure Table Storage account
  (`infra/main.bicep`'s `storage` resource - already used by `TableStorageEntitlementGrantStore`,
  `TableStorageAccountStore`, `TableStorageProcessedEventStore`, `TableStorageTelemetrySink`) rather
  than provisioning anything new. Mirror the existing `TableStorage*Store` pattern (see
  `api/src/Entitlements/TableStorageEntitlementGrantStore.cs` for the reference shape): a
  single-row table (fixed partition/row key - there is only ever one active-mode value for the
  whole app) with columns for the mode value and a last-changed timestamp (AC-07), a
  `CreateIfNotExists`-once guard, and an in-memory fallback (defaulting to Test, AC-05) for local
  dev / CI where no storage connection string is configured - the same config-presence idiom every
  other store in this repo already follows. Cache the resolved value briefly in memory (a short TTL,
  e.g. seconds, not minutes) so the checkout/webhook hot paths do not take a storage round-trip per
  request (AC-02's "no more than one storage read per request path" - the cache satisfies this
  between reads, not literally once per call).
- **Webhook verification per mode (AC-04).** `StripeWebhookController.Webhook` today verifies
  against one `_options.WebhookSigningSecret`. With two live webhook endpoints registered in
  Stripe's dashboard (one per mode, per the runbook), both will POST to the SAME URL
  (`/api/stripe/webhook`) in this single-app deployment. The controller cannot know in advance
  which mode an incoming event is from - it must attempt verification, not branch on the currently
  ACTIVE mode (a mode flip mid-flight must not orphan an in-progress checkout's webhook, AC-04).
  Try the Live signing secret first (or whichever mode is currently active, as a fast path), and
  on a signature-verification failure, retry against the OTHER mode's signing secret before
  rejecting - `EventUtility.ConstructEvent` throws `StripeException` on a bad signature, so this is
  a straightforward try/catch fallback, not a redesign. Only reject (400) if verification fails
  against BOTH secrets. This is the one piece of this story that is a genuine design decision worth
  flagging in review, not a mechanical edit.
- **The footgun (AC-05, AC-06, AC-08).** This is a single PUBLIC environment
  (`quibblestone.com`). Wrong-mode-active failure modes: Live active during a demo/dev session
  risks a real charge; Test active while a real supporter tries to pay silently declines their real
  card with no obvious "we're in test mode" signal to the operator. AC-05's safe default (Test) and
  AC-06's confirmation-gating exist specifically to make the dangerous direction (flipping TO Live)
  the deliberate one, never the accidental one. Story 07 owns the actual confirmation dialog and any
  "TEST MODE" operator-visible banner; this story's endpoint should still not make it trivially easy
  to flip silently (e.g. do not accept the mode change as a GET or as a side effect of any other
  call).
- New/changed files (indicative, not prescriptive): `api/src/Billing/StripeOptions.cs` (reshaped
  per AC-01), a new `IActiveStripeModeStore` + `TableStorageActiveStripeModeStore` +
  `InMemoryActiveStripeModeStore` (mirrors `IEntitlementGrantStore`'s pairing), a new
  `Controllers/StripeModeController.cs` (or a route added to an existing admin-adjacent controller)
  exposing `GET /api/admin/stripe-mode` (current mode + last-changed) and
  `POST /api/admin/stripe-mode` (the guarded flip), and edits to `StripeWebhookController`,
  `StripeCheckoutService`, and `ProductCatalog` to resolve the active mode's credentials instead of
  a single injected `StripeOptions`. Register the new store and gate via `Program.cs`'s existing
  singleton/DI pattern.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Billing/StripeOptionsTests.cs (new): binding a config section with both Live and Test sub-sections resolves two independent credential sets, neither overwriting the other.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Billing/ActiveStripeModeStoreTests.cs (new): a written mode value is readable after a simulated restart (new store instance over the same backing); a flip is visible on the next read without an app restart.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Billing/ActiveStripeModeStoreTests.cs (Context_resolves_the_active_modes_config): the active mode resolves ONLY its own secret key + price ids (Test by default, Live after a flip) - never a live/test mix.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/Billing/StripeWebhookControllerTests.cs: a Live-signed event verifies while Test is the currently active mode (and vice versa); a signature that matches neither mode is rejected (400).` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Billing/ActiveStripeModeStoreTests.cs: a store with nothing yet persisted (fresh environment) resolves to Test.` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Billing/StripeModeControllerTests.cs (new): an unauthenticated/non-operator request to flip the mode is rejected and does not change the persisted value; reading the current mode is a separate endpoint/action from flipping it.` |
| AC-07 | `tests/QuibbleStone.Api.Tests/Billing/ActiveStripeModeStoreTests.cs: after a flip, the last-changed timestamp and new mode value are both readable.` |
| AC-08 | `manual: code/UI audit - confirm no player-facing page, log line at Information level, or API response outside the new admin-scoped endpoint reveals the active mode.` |

## Dependencies
- `billing-entitlements/03` (#72) - the `StripeOptions`/`StripeCheckoutService`/
  `StripeWebhookController` shape this story reshapes into mode-aware form; already Complete.
- `sysadmin-console/01` (#135) - the real operator-auth boundary this story's toggle endpoint
  should eventually sit behind; not required to start (see Technical Notes' recommended interim
  gate), but the swap to real operator auth should happen promptly once #135 ships.
- infra (Azure Table Storage, already provisioned per README section 9; Key Vault for the two
  credential sets and, if option (b) is used, the interim operator secret).
