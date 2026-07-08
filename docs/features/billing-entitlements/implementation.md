<!--
  Implementation plan for the billing-entitlements feature. Bridges feature.md + stories to orchestration.
  Refreshed 2026-07-03 against shipped reality: the IEntitlementService interface, SessionEntitlements, the
  ai.onDemand catalog reservation, and the GameHub.CreateRoom capture already ship (ai-cost-gate/02, #121, PR
  #132). Story 01 (#70) now EDITS that shipped folder rather than creating it. Use hyphens/colons/parentheses,
  never em dashes.
-->

# Implementation Plan: Billing & Entitlements

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Entitlement contract (interface + result type) - ALREADY SHIPPED | `IEntitlementService`, `SessionEntitlements` (ai-cost-gate/02, #121, PR #132) | `api/src/Entitlements/IEntitlementService.cs` |
| Reserved AI capability key - ALREADY SHIPPED | `EntitlementCatalog.AiOnDemand` / `AiCapabilities` - story 01 EXTENDS this, does not replace it | `api/src/Entitlements/IEntitlementService.cs` |
| Default-unlocked stand-in - ALREADY SHIPPED, SUPERSEDED by story 01 | `DefaultUnlockedEntitlementService` - story 01's stored-value implementation composes it as the "no grant" baseline rather than re-deriving default-unlocked | `api/src/Entitlements/IEntitlementService.cs` |
| Session-creation call site (the ONLY place the gate runs) - ALREADY WIRED | `GameHub.CreateRoom` already calls `EvaluateForSession` + `Room.CaptureEntitlements` exactly once; story 01 does not touch this call site | `api/src/Hubs/GameHub.cs`, `api/src/Rooms/Room.cs` |
| Service registration pattern (singleton DI) | the existing `RoomRegistry` / `IContentSafetyFilter` registrations, and the existing `IEntitlementService` DI line story 01 swaps | `api/src/Program.cs` |
| Purchaser identity lookup | `IAccountStore` from accounts-identity/02 (upstream dependency, not yet built) | `api/src/Accounts/` (new, that feature) |
| Sign-in / purchaser credential | the purchaser-scoped credential from accounts-identity/03 | `api/src/Controllers/AccountsController.cs` (new, that feature) |
| Existing test coverage to extend, not replace | `EntitlementServiceTests.cs`, `GameHubEntitlementTests.cs` (ai-cost-gate/02) | `tests/QuibbleStone.Api.Tests/` |
| Child safety (any free-text field, e.g. a tip message) | the single server-side safety filter | `api/src/Safety/IContentSafetyFilter.cs`, `ContentSafetyFilter.cs` |
| Styling / theme tokens (gold CTA, purple secondary, stone-tablet) | the MUI theme | `web/src/theme.ts` |
| Shared UI contracts | the single AppBar + Button family | `web/src/components/AppBar.tsx`, `web/src/components/index.ts` |
| Guardian mascot (cosmetic thank-you, paywall illustration) | the existing `Guardian` component (6 variants) | `web/src/components/Guardian.tsx` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Config (non-secret) | `import.meta.env` (`VITE_*`) | `web/src/vite-env.d.ts`, `web/.env.development` |
| Secrets (Stripe secret key, webhook signing secret) | Azure Key Vault | `infra/main.bicep` (`keyVault` resource) |
| Durable storage (entitlement grants, processed webhook events) | Azure Table Storage | `infra/main.bicep` (`storage` resource) |
| Home screen pattern for a low-key, reassuring affordance | the existing "No account needed" reassurance row | `web/src/pages/Home.tsx` (reference for tone/placement) |
| `TableStorage*Store` pairing pattern (real store + in-memory fallback, config-presence idiom) - story 06 mirrors this for the active-mode flag | `TableStorageEntitlementGrantStore` / `InMemoryEntitlementGrantStore` | `api/src/Entitlements/` |
| Stripe checkout/webhook surface story 06 reshapes into mode-aware form | `StripeOptions`, `StripeCheckoutService`, `StripeWebhookController`, `ProductCatalog` | `api/src/Billing/`, `api/src/Controllers/StripeWebhookController.cs` |
| Operator auth boundary story 06/07 sit behind (once it lands; interim gate until then, see story 06 Technical Notes) | `sysadmin-console/01`'s `[Authorize(Policy = "Operator")]` | `docs/features/sysadmin-console/01-operator-login-and-admin-boundary.md` (not yet built) |
| Fixed-window rate-limit policy shape (story 08's resync endpoint reuses this pattern, not a new one) | `OperatorLoginRateLimit`, `CloudGalleryRateLimit` | `api/src/Admin/OperatorLoginRateLimit.cs`, `api/src/CloudGallery/CloudGalleryRateLimit.cs` |

New surfaces this feature introduces (not yet reuse targets, become them once built):
- `EntitlementGrant` + a grant-store type (`IEntitlementGrantStore` or similar) - story 01 ADDS these to the
  ALREADY-SHIPPED `api/src/Entitlements/IEntitlementService.cs` file/folder (ai-cost-gate/02) rather than
  creating a new folder. The contract every later paid feature (add-on packs, `ai.illustration`, `ai.voice`,
  `ai.onDemand`, and the sysadmin-console operator grant/revoke, #136) will import is already `IEntitlementService`
  itself - shipped.
- `api/src/Billing/` (`StripeCheckoutService`, `StripeWebhookHandler`) - story 03. Shared by stories 02 and 04.
- `web/src/components/TipJar.tsx` (or similar) - story 02.
- `web/src/pages/` purchase/paywall screen and restore/manage view - stories 04 and 05 (likely sit near the
  accounts-identity/03 sign-in screen).
- `IActiveStripeModeStore` + `TableStorageActiveStripeModeStore` / `InMemoryActiveStripeModeStore`, and a
  `Controllers/StripeModeController.cs` - story 06. The interim `IOperatorGate` (or equivalent) story 06 builds
  to unblock on `sysadmin-console/01` becomes a reuse target for story 07 and, later, a one-file swap once #135
  ships.
- An operator-facing mode screen (location depends on whether `sysadmin-console/01` has landed - see story 07
  Technical Notes) - story 07.
- `GrantId`/`PlanId`/`StripeSubscriptionId` on `EntitlementGrant` (ADR 0003 Layer 2) - story 08 ADDS these fields
  to the ALREADY-SHIPPED `api/src/Entitlements/EntitlementGrant.cs` / `TableStorageEntitlementGrantStore.cs`
  rather than creating a new store type. A new `IStripeReconciliationService` (or similarly named) + its live
  implementation in `api/src/Billing/`, and a new `Operator`-policy admin endpoint (`sysadmin-console/07`'s future
  support-verb call target) - story 08.

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. This feature has one true
foundation story (01 - now an EXTENSION of the already-shipped `api/src/Entitlements/` folder, not a from-scratch
build), one shared-plumbing story (03) that two consumer stories (02, 04) both build on, and one story (05) that
is a thin read on top of 01 + accounts-identity/03. Story 01 is still a hard prerequisite for everything else - it
defines the FULL catalog and the grant-store shape every other story writes into or reads from - but its own
prerequisite is narrower now: it needs accounts-identity/02's `IAccountStore` (purchaser identity resolution),
not session-engine (the session-creation call site is already wired by the shipped `ai-cost-gate/02`).

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 entitlement-model-and-gate (extends the shipped seam) | #70 | EDITS `api/src/Entitlements/IEntitlementService.cs` (extends `EntitlementCatalog`, adds `EntitlementGrant` + a grant-store type, swaps the `IEntitlementService` DI registration in `Program.cs`) - no edit to `GameHub.cs`'s `CreateRoom` signature/shape | accounts-identity/02 (purchaser identity / `IAccountStore` - upstream) | - | 1 | high |
| 03 stripe-integration-and-store | #72 | `api/src/Billing/StripeCheckoutService.cs`, `StripeWebhookHandler.cs`, `Controllers/StripeWebhookController.cs` (now also the subscription lifecycle: renewal/past_due-grace/canceled) | 01, accounts-identity/02 | - (build before 02/04 need to actually charge) | 2 | high |
| 02 tip-jar | #71 | `web/src/components/TipJar.tsx` (or `pages/`), one Home entry-point edit | 01 (confirms no-op), 03 (payment call) | 04 (disjoint files) | 3 | medium |
| 04 gated-purchase-flow | #73 | `web/src/pages/` paywall screen, `api/src/Billing/` pack/plan-to-capability-bundle map | 01, 03, accounts-identity/02 | 02 (disjoint files) | 3 | medium |
| 05 restore-and-manage | #74 | `web/src/pages/` restore/manage view (near accounts-identity/03's screen), a read-only API endpoint | 01, accounts-identity/03 | - (needs 03/04 landed to have anything real to show, though its empty state is independently testable) | 4 | medium |
| 06 live-test-mode-toggle | TBD | `api/src/Billing/StripeOptions.cs` (reshaped), new `IActiveStripeModeStore` + Table/InMemory impls, new `Controllers/StripeModeController.cs`, edits to `StripeCheckoutService`, `StripeWebhookController`, `ProductCatalog` | 03 (reshapes its output), sysadmin-console/01 (soft - see Technical Notes interim gate) | - | 5 | medium-high |
| 07 operator-mode-toggle-ui | TBD | a new operator-facing screen (location per Technical Notes - `sysadmin-console`'s back office if landed, else a temporary standalone route) | 06 (hard - calls its endpoint), sysadmin-console/01 (soft, same interim-gate posture) | - | 6 | small |
| 08 grant-metadata-and-stripe-reconciliation (ADR 0003 Layer 2) | TBD | EDITS `api/src/Entitlements/EntitlementGrant.cs` + `TableStorageEntitlementGrantStore.cs` (new columns incl. `Mode`), `api/src/Billing/BillingEvent.cs`, `StripeEventMapper.cs`, `IStripeCheckoutService.cs` (`BillingMetadata`), `CheckoutModels.cs`, `StripeWebhookHandler.cs`; NEW `api/src/Billing/IStripeReconciliationService.cs` + implementation + `StripeResyncRateLimit.cs` + a new admin endpoint | 01, 03, 06, accounts-identity/02, sysadmin-console/01; soft: accounts-identity/05 (see story's degraded path) | - | **2 (ADR canonical)** | high |

**Local build-order history (Waves 1-6, stories 01-07, all shipped/Complete).** Wave 1 = 1 (01, extending the shipped
seam - had to land first, everything else imports its shape; the `IEntitlementService` interface itself was already
shipped, so this wave was narrower than a from-scratch build but still gated everything downstream). Wave 2 = 1 (03,
the shared Stripe plumbing - serial-after-01 since it writes into 01's store). Wave 3 = {02, 04} in parallel (disjoint
web/API files; both consumed 03's `StripeCheckoutService` but called it with different parameters). Wave 4 = 05
(benefited from 03/04 having granted at least one real entitlement to display). Wave 5 = 06 (reshaped 03's
`StripeOptions`/`StripeCheckoutService`/`StripeWebhookController` into mode-aware form). Wave 6 = 07 (called 06's
endpoint). These are historical local sequencing labels only - stories 01-07 are all Complete and do not appear in
any future orchestration run, so they carry no cross-feature meaning.

**Story 08's Wave is the ADR 0003 canonical number, not a continuation of the local sequence above.** The 2026-07-08
adversarial review flagged this feature's earlier "Wave 7" label for story 08 as misleading: an orchestrator reading
across features groups by the ADR's own numbering (ADR 0003's "Canonical wave numbers" note), where story 08 sits in
**Wave 2** alongside `accounts-identity/06`, `accounts-identity/07`, `keepsake-vault/02`, `control-plane/02`, and
`sysadmin-console/05`. This is not a contradiction of story 08's real prerequisites: 01, 03, and 06 (its true
in-feature blockers) are already Complete, so by the time ADR 0003 work begins, story 08's only REMAINING dependency
is the soft, cross-feature one on `accounts-identity/05` (ADR Wave 1) - exactly consistent with it running in ADR
Wave 2. Use `2` when scheduling story 08 against other features' work; the file-footprint list above (01, 03, 06,
accounts-identity/02, sysadmin-console/01) documents its already-satisfied in-feature history, not a remaining
same-feature blocker.

**Cross-feature file hazard in Wave 2 (ADR 0003, 2026-07-08):** story 08 edits
`api/src/Entitlements/EntitlementGrant.cs` (the record shape) in the SAME wave that `control-plane/02` (a DIFFERENT
feature) edits `api/src/Entitlements/StoredValueEntitlementService.cs` (a consumer of that same folder, composing the
system-flag scope ahead of an account grant). They are not disjoint at the FOLDER level even though they touch
different files: land story 08's record-shape change (`EntitlementGrant.cs` gaining `GrantId`/`PlanId`/
`StripeSubscriptionId`/`Mode`) either fully BEFORE or fully AFTER `control-plane/02`'s edit to the consumer that reads
that record, not concurrently - a mid-flight rebase of one against a moving record shape underneath the other is the
failure mode to avoid. See `control-plane/implementation.md`'s own Wave Plan for its side of this hazard.

**Cross-feature order:** accounts-identity/02 (magic-link + `IAccountStore`) is upstream of story 01's
purchaser-lookup piece (AC-06) - schedule it first across features. Story 01's catalog-extension and grant-store
pieces (AC-01, AC-05) do not themselves depend on accounts-identity and could be built independently, but the
purchaser-resolution piece cannot complete until `IAccountStore` exists.

**Sequencing nuance vs. the Stories table order:** feature.md's Stories table lists 02 (tip jar) before 03 (Stripe
plumbing), reflecting the *product's* donate-first narrative. The Wave Plan reorders them for *build* purposes
because 02's payment call has nothing to call until 03's `StripeCheckoutService` exists - see feature.md's
Decisions log for this deliberate distinction between story numbering (product narrative) and wave order (technical
DAG).

## Per-story tech notes

### 01 - Entitlement model + session-creation gate (extends the shipped seam)
**Approach:** `IEntitlementService`, `SessionEntitlements`, and the reserved `ai.onDemand` key ALREADY SHIP
(`api/src/Entitlements/IEntitlementService.cs`, ai-cost-gate/02 #121/PR #132), as does the `GameHub.CreateRoom`
call site (`EvaluateForSession` + `Room.CaptureEntitlements`, exactly once, session-creation-time). This story
EDITS that same file: extends `EntitlementCatalog` to the full set (`library.full`, `play.remote`,
`play.largeGroup`, `pack.<id>`), adds an `EntitlementGrant` record + a grant-store type (Table-Storage-backed,
partitioned by a hash of purchaser identity), and replaces the `DefaultUnlockedEntitlementService` DI registration
in `Program.cs` with a new stored-value implementation that COMPOSES the shipped default-unlocked behavior as its
"no grant" baseline. **Exports:** the same `IEntitlementService` contract (unchanged shape) - the contract every
consumer story (02, 04, 05) and every future paid feature (plus the sysadmin-console operator grant/revoke, #136)
imports. **Gotcha:** the "no purchaser / no grant -> default-unlocked" behavior (AC-03) must be provably identical
to today's shipped behavior - compose the existing stand-in rather than re-deriving default-unlocked, so
`EntitlementServiceTests`/`GameHubEntitlementTests` keep passing unmodified in spirit.

### 03 - Stripe integration + entitlement store
**Approach:** new `api/src/Billing/` folder. `StripeCheckoutService` creates Checkout Sessions in either
`payment` (one-time) or `subscription` mode through one parameterized method (README section 3's "same billing
plumbing"). `StripeWebhookHandler` verifies the Stripe signature, resolves the event to a purchaser + capability
grant, and writes through `IEntitlementService`/the grant store from story 01 - with idempotency keyed on the
Stripe event id so a re-delivered webhook is a no-op. It also now owns the subscription's full lifecycle (ADR 0002
Decisions C/D): `invoice.paid` extends `validThrough` on renewal, `past_due` extends it by a ~7-day grace constant
instead of expiring, and `canceled` (or a lapsed grace) lets `validThrough` pass so the next session-creation read
falls back to free. The webhook is a REST controller mapped in `Program.cs` alongside existing controllers. Keys
come from Azure Key Vault. **Exports:** `StripeCheckoutService` (consumed by 02 and 04). **Gotcha:** README section
4 names Stripe webhooks as the natural first Azure Functions carve-out - `StripeWebhookHandler` should be written
as a self-contained class with no cross-cutting dependencies, so lifting it out later is a move, not a rewrite. No
Function project is created now.

### 02 - Tip jar
**Approach:** a `TipJar` component/dialog reachable from Home's settings area (never the kid play-flow), styled
from theme tokens, using the `Guardian` component for the optional cosmetic thank-you. Its payment call goes
through `StripeCheckoutService` (story 03) in one-time mode, but its success handler deliberately never calls
`IEntitlementService`'s grant path - the tip jar is entitlement-neutral by design. **Owns:** `TipJar.tsx` and one
small edit to Home's entry points. **Gotcha:** if scheduled before 03 lands (deviating from the Wave Plan), keep
the payment call behind a small interface so 03 slots in without a rewrite - see the story's Technical Notes.

### 04 - Gated purchase flow
**Approach:** a paywall/purchase screen (new `web/src/pages/` file) reachable only from purchaser-facing areas
(settings, or a between-rounds host prompt - never mid-round), using the same gold-CTA / outlined-purple pattern as
Home. Calls `StripeCheckoutService` (story 03) parameterized by the specific pack/subscription product; a small
price-id-to-capability-KEYS map (a LIST per product - one key for a pack, the whole family-plan bundle for the
subscription, ADR 0002 Decision C) lives alongside it. On successful purchase, checkout naturally creates the
purchaser account (accounts-identity/02) if one does not already exist - no separate forced sign-up step. **Owns:**
the paywall screen, the pack/plan-to-capability-bundle lookup. **Gotcha:** this story proves the seam end to end
but must not build any live mid-session upgrade path - the unlock is expected to show up on the *next*
session-creation only (a direct consequence of 01 AC-03, not something 04 needs to engineer); it also must not
render any dunning/grace messaging - that mechanic lives entirely in story 03's webhook handling.

### 05 - Restore / manage entitlements
**Approach:** a read-only view sitting alongside accounts-identity/03's sign-in screen, guarded by the same
purchaser-credential check. Calls a new, small read endpoint that resolves the signed-in purchaser's grants via
`IEntitlementService` (story 01) and renders them with friendly display names (a small capability-key-to-label
map). **Owns:** the restore/manage view + its read endpoint. **Gotcha:** this is read-only - no write path, no plan
management, no cancellation; keep it that way per the story's Out of Scope.

### 06 - Live/test Stripe mode: mode-aware config + toggle endpoint
**Approach:** reshapes `StripeOptions` into a mode-keyed configuration (Live + Test credential sets held
simultaneously), adds a persisted active-mode flag mirroring the existing `TableStorage*Store` pairing pattern
(real Table Storage store + in-memory fallback, defaulting to Test), and adds a small, swappable `IOperatorGate`
interface behind the toggle endpoint so the interim thin secret-based gate is a one-file swap once
`sysadmin-console/01` ships real operator auth. **Exports:** the resolved "active mode's credentials" view
(`StripeCheckoutService`, `StripeWebhookController`, `ProductCatalog` all read through it rather than branching on
mode themselves) and the `GET`/`POST /api/admin/stripe-mode` endpoints story 07 calls. **Gotcha:** the webhook
verification path (AC-04) must try BOTH mode's signing secrets (not branch on the currently-active mode) since a
mode flip mid-checkout must not orphan an in-flight webhook - this is the one genuinely tricky piece, flag it in
review.

### 07 - Live/test Stripe mode: operator toggle UI
**Approach:** a small screen calling story 06's two endpoints - render the current mode + last-changed timestamp,
and a confirmation dialog (asymmetric friction: switching TO Live warns harder than switching to Test) before
submitting a flip. **Owns:** the screen itself; location depends on whether `sysadmin-console/01` has landed by
build time (its back office if so, else a clearly-marked temporary standalone route per the story's Technical
Notes). **Gotcha:** do not over-invest in polish if built before `sysadmin-console/01` lands - it is meant to
relocate, not become a second design system.

### 08 - Grant metadata + Stripe reconciliation (ADR 0003 Layer 2)
**Approach:** `EntitlementGrant` gains `GrantId` (`Guid`, freshly minted per write), `PlanId` (`string?`, the
`ProductCatalog` product id), `StripeSubscriptionId` (`string?`, populated for subscription grants), and `Mode`
(`StripeMode?`, reusing story 06's enum - the mode that verified the write, `null` only for an operator comp). A new
`qs_product` metadata key (alongside the existing `qs_capabilities`/`qs_purchaser`) rides the same
`CheckoutRequest`/`BillingMetadata`/`StripeEventMapper`/`BillingEvent` pipeline stories 03/04 already built, so
`StripeWebhookHandler` can populate all four fields on every grant write without a second lookup. A new per-account
resync service resolves a purchaser's Stripe customer(s) + subscriptions in the ACTIVE mode (`IActiveStripeContext`,
story 06), CROSS-CHECKS each candidate subscription's `qs_purchaser` metadata against the target account's identity
(never trusting the Stripe customer's bare `Email` field alone - **revised 2026-07-08 after the adversarial review**,
which found the original "most-recent customer wins" email tiebreak steerable by an attacker who creates a Stripe
customer under a victim's email), and rewrites only the matching subscription-sourced grants whose stored `Mode`
equals the active mode (a mismatch is skipped and logged, never overwritten - the review's "mode-aware store" fix,
so a Test-mode resync can never touch a Live-derived grant) - reusing the SAME lease-math helper the webhook handler
already has (extract it to a shared method rather than duplicate it), and the SAME upsert-by-capability-key write
path (idempotency is inherited, not reinvented). The endpoint sits behind a new fixed-window rate limiter
(`StripeResyncRateLimit`, mirroring `OperatorLoginRateLimit`/`CloudGalleryRateLimit`) so a scripted or repeated call
cannot fan out unbounded Stripe API traffic. **Exports:** the extended `EntitlementGrant` shape (consumed by
`sysadmin-console/02`'s existing grant/revoke screen, which should start displaying `PlanId`/`Mode` once available)
and the resync endpoint (`sysadmin-console/07`'s future support-verb call target). **Gotcha:** a pre-existing grant
row (written before this story) has none of the four new columns - `TableStorageEntitlementGrantStore.FromEntity`
must degrade this to a fresh `GrantId`, null `PlanId`/`StripeSubscriptionId`, and `Mode = Test` (factually correct -
no grant in this store predates Stripe Live ever going active) rather than throw, mirroring its existing defensive
handling of a missing `Source`. Resync only ever rewrites SUBSCRIPTION-sourced grants whose stored `Mode` matches
the active mode - a one-time pack has no ongoing Stripe state to reconcile against, an operator comp has no Stripe
mode at all, and a cross-mode row is left untouched by the AC-08 guard; all three must be left byte-for-byte as they
were. **Cross-feature hazard:** this story's `EntitlementGrant.cs` record-shape edit and `control-plane/02`'s edit to
`StoredValueEntitlementService.cs` (a different feature, same Wave 2) both touch `api/src/Entitlements/` - land one
fully before the other, not concurrently (see this file's Wave Plan section).

## Cross-cutting concerns

- **The interface is shipped; the stored-value side is not.** `IEntitlementService`, `SessionEntitlements`, the
  `ai.onDemand` reservation, and the `GameHub.CreateRoom` capture already ship (ai-cost-gate/02, #121, PR #132) as
  a thin, default-unlocked, read-only stand-in. Story 01 extends the SAME interface with the full catalog and a
  real grant store - it does not re-derive the contract every consumer story already imports.
- **Everything is a consumer of story 01's seam.** Stories 02, 04, and 05 - and every future paid feature (add-on
  packs, `ai.illustration`, `ai.voice`, `ai.onDemand`) - read or write through `IEntitlementService`. None of them
  invents a parallel check. If a future builder is tempted to add a per-request gate anywhere, that is a smell to
  flag against feature.md's Design notes.
- **Session-creation-time only, never per-request.** The gate is evaluated once, at room/solo creation. No story in
  this feature (or any future consumer) may call it inside blank submission, round-collection, or reveal code
  paths.
- **Secrets in Key Vault, never `VITE_*`.** Stripe secret key, webhook signing secret, and any future AI provider
  key all follow the same pattern - no exceptions, no "just for dev" shortcuts committed to the repo.
- **Kid-safe, no dark patterns, no ads.** Every purchase-adjacent surface (tip jar, paywall, restore) stays out of
  the active join/lobby/word-entry/reveal flow, uses the warm stone-tablet/Guardian visual language, and never
  nags a family that has not purchased anything. Ads are not an option under any circumstance (README section 3).
- **Free tier does not shrink.** Every story's ACs include an explicit "today's free-tier behavior is unchanged"
  guard - treat any regression to single-player, same-code group play, or base content as a P0.
- **One billing plumbing, many purchase shapes.** New pack types or a future plan tier extend
  `StripeCheckoutService`'s parameters and the price-to-capability map (story 04's pattern) - they do not spawn a
  second Stripe integration.
- **No i18n** (plain strings). **No em dashes.** Big tap targets extend to purchaser-facing screens too - one
  visual language across the whole app, not a separate "checkout look."
- **Stories 06-07's operator gate is deliberately interim, not a shortcut to skip building real operator auth.**
  Both stories name the same swap point (`IOperatorGate` or equivalent) so that when `sysadmin-console/01` (#135)
  ships, migrating onto real operator auth is a small, contract-stable edit - mirroring how `sysadmin-console/01`
  itself and `sysadmin-console/02` each name a thin, contract-compatible stand-in for their own unbuilt upstream
  dependencies. Do not let the interim gate quietly become permanent; revisit promptly once #135 lands.
- **Resync is a recovery action, never a routine path (story 08, ADR 0003).** Webhooks remain the routine source of
  truth for every grant write; the per-account resync service is operator-triggered only - no schedule, no
  automatic run, no per-request call. This is the same "session-creation-time only, never per-request" discipline
  applied to a support tool: an operator explicitly asks "reconcile this one purchaser," nothing more.
- **Resync cannot corrupt grants across Stripe modes or be steered by a spoofed identity (story 08, revised
  2026-07-08 after the adversarial review).** Three binding rules: (a) the grant row is mode-aware (`Mode`) and a
  resync run refuses to write any grant whose stored `Mode` differs from the currently active mode - a Test-mode
  resync can never overwrite a Live-derived grant, or vice versa; (b) reconciliation matches Stripe subscriptions by
  the `qs_purchaser` checkout metadata this app itself stamped, never by the Stripe customer's bare `Email` field -
  an attacker's self-created Stripe customer under a victim's email carries no matching metadata and is never
  picked; (c) the resync endpoint is rate-limited (`StripeResyncRateLimit`, the same fixed-window pattern as
  `OperatorLoginRateLimit`/`CloudGalleryRateLimit`) so it cannot fan out unbounded Stripe API traffic and disrupt
  live webhook processing.
- **Single public environment footgun (stories 06-07).** `quibblestone.com` is the one live site - there is no
  separate staging Stripe-mode surface. AC-05's safe default (Test) and AC-06's confirmation-gating (story 06),
  plus AC-02/AC-03's asymmetric-friction confirmation (story 07), exist specifically so "go live" is always the
  effortful, deliberate direction and "fall back to test" is always cheap and fast.
