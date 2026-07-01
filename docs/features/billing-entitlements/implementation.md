<!--
  Implementation plan for the billing-entitlements feature. Bridges feature.md + stories to orchestration.
  Look-ahead pass: no story is built yet (all Not Started, Issue TBD). Written now so the feature is
  orchestration-ready the moment its phase comes up. Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Billing & Entitlements

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Service registration pattern (singleton DI) | the existing `RoomRegistry` / `IContentSafetyFilter` registrations | `api/src/Program.cs` |
| Session-creation call site (the ONLY place the gate runs) | the hub's room-create method + the solo entry point | `api/src/Hubs/GameHub.cs`, `web/src/pages/Solo.tsx` |
| Purchaser identity | the account record + store from accounts-identity/02 | `api/src/Accounts/` (new, that feature) |
| Sign-in / purchaser credential | the purchaser-scoped credential from accounts-identity/03 | `api/src/Controllers/AccountsController.cs` (new, that feature) |
| Child safety (any free-text field, e.g. a tip message) | the single server-side safety filter | `api/src/Safety/IContentSafetyFilter.cs`, `ContentSafetyFilter.cs` |
| Styling / theme tokens (gold CTA, purple secondary, stone-tablet) | the MUI theme | `web/src/theme.ts` |
| Shared UI contracts | the single AppBar + Button family | `web/src/components/AppBar.tsx`, `web/src/components/index.ts` |
| Guardian mascot (cosmetic thank-you, paywall illustration) | the existing `Guardian` component (6 variants) | `web/src/components/Guardian.tsx` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Config (non-secret) | `import.meta.env` (`VITE_*`) | `web/src/vite-env.d.ts`, `web/.env.development` |
| Secrets (Stripe secret key, webhook signing secret) | Azure Key Vault | `infra/main.bicep` (`keyVault` resource) |
| Durable storage (entitlement grants, processed webhook events) | Azure Table Storage | `infra/main.bicep` (`storage` resource) |
| Home screen pattern for a low-key, reassuring affordance | the existing "No account needed" reassurance row | `web/src/pages/Home.tsx` (reference for tone/placement) |

New surfaces this feature introduces (not yet reuse targets, become them once built):
- `api/src/Entitlements/` (`CapabilityKey`, `IEntitlementService`, storage-backed implementation) - story 01. The
  contract every later paid feature (add-on packs, `ai.illustration`, `ai.voice`, `ai.onDemand`) will import.
- `api/src/Billing/` (`StripeCheckoutService`, `StripeWebhookHandler`) - story 03. Shared by stories 02 and 04.
- `web/src/components/TipJar.tsx` (or similar) - story 02.
- `web/src/pages/` purchase/paywall screen and restore/manage view - stories 04 and 05 (likely sit near the
  accounts-identity/03 sign-in screen).

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. This feature has one true
foundation story (01), one shared-plumbing story (03) that two consumer stories (02, 04) both build on, and one
story (05) that is a thin read on top of 01 + accounts-identity/03. Story 01 is a hard prerequisite for everything
else - it defines the catalog and the store shape every other story writes into or reads from.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 entitlement-model-and-gate (foundation) | #70 | `api/src/Entitlements/*`, one call-site edit in `GameHub.cs` / `Solo.tsx` | accounts-identity/01, accounts-identity/02, session-engine | - | 1 | high |
| 03 stripe-integration-and-store | #72 | `api/src/Billing/StripeCheckoutService.cs`, `StripeWebhookHandler.cs`, `Controllers/StripeWebhookController.cs` | 01, accounts-identity/02 | - (build before 02/04 need to actually charge) | 2 | high |
| 02 tip-jar | #71 | `web/src/components/TipJar.tsx` (or `pages/`), one Home entry-point edit | 01 (confirms no-op), 03 (payment call) | 04 (disjoint files) | 3 | medium |
| 04 gated-purchase-flow | #73 | `web/src/pages/` paywall screen, `api/src/Billing/` pack-to-capability map | 01, 03, accounts-identity/02 | 02 (disjoint files) | 3 | medium |
| 05 restore-and-manage | #74 | `web/src/pages/` restore/manage view (near accounts-identity/03's screen), a read-only API endpoint | 01, accounts-identity/03 | - (needs 03/04 landed to have anything real to show, though its empty state is independently testable) | 4 | medium |

**Concurrency per wave:** Wave 1 = 1 (01, the seam - must land first, everything else imports its shape). Wave 2 =
1 (03, the shared Stripe plumbing - technically could start once 01's shape is stable, even before 01 is fully
merged, but treat as serial-after-01 for safety since 03 writes into 01's store). Wave 3 = {02, 04} in parallel
(disjoint web/API files; both consume 03's `StripeCheckoutService` but call it with different parameters - a pack/
subscription price for 04, a one-time no-entitlement price for 02). Wave 4 = 05 (benefits from 03/04 having granted
at least one real entitlement to display, and needs accounts-identity/03's sign-in to exist as its auth guard).

**Sequencing nuance vs. the Stories table order:** feature.md's Stories table lists 02 (tip jar) before 03 (Stripe
plumbing), reflecting the *product's* donate-first narrative. The Wave Plan reorders them for *build* purposes
because 02's payment call has nothing to call until 03's `StripeCheckoutService` exists - see feature.md's
Decisions log for this deliberate distinction between story numbering (product narrative) and wave order (technical
DAG).

## Per-story tech notes

### 01 - Entitlement model + session-creation gate (foundation)
**Approach:** new `api/src/Entitlements/` folder (mirrors `Rooms/`, `Safety/`, `Accounts/`). A `CapabilityKey`
catalog (string-backed, minimum set per AC-01) and `IEntitlementService.EvaluateForSession(purchaserIdentity?) ->
SessionEntitlements`, registered in `Program.cs`. The single call site is session-creation: the room-create hub
method (`GameHub.cs`, session-engine/01) and the solo entry point (`Solo.tsx` / its API equivalent). Storage: an
`EntitlementGrant` row per purchaser + capability key in Azure Table Storage. **Exports:** `IEntitlementService`
and `CapabilityKey` - the contract every consumer story (02, 04, 05) and every future paid feature imports.
**Gotcha:** default-unlocked must be a literal code default, not a flag someone remembers to set - the whole point
is that shipping this story changes zero observed behavior (AC-02, AC-07).

### 03 - Stripe integration + entitlement store
**Approach:** new `api/src/Billing/` folder. `StripeCheckoutService` creates Checkout Sessions in either
`payment` (one-time) or `subscription` mode through one parameterized method (README section 3's "same billing
plumbing"). `StripeWebhookHandler` verifies the Stripe signature, resolves the event to a purchaser + capability
grant, and writes through `IEntitlementService` from story 01 - with idempotency keyed on the Stripe event id so a
re-delivered webhook is a no-op. The webhook is a REST controller mapped in `Program.cs` alongside existing
controllers. Keys come from Azure Key Vault. **Exports:** `StripeCheckoutService` (consumed by 02 and 04).
**Gotcha:** README section 4 names Stripe webhooks as the natural first Azure Functions carve-out - `StripeWebhookHandler`
should be written as a self-contained class with no cross-cutting dependencies, so lifting it out later is a move,
not a rewrite. No Function project is created now.

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
price-id-to-capability-key map lives alongside it. On successful purchase, checkout naturally creates the purchaser
account (accounts-identity/02) if one does not already exist - no separate forced sign-up step. **Owns:** the
paywall screen, the pack-to-capability lookup. **Gotcha:** this story proves the seam end to end but must not build
any live mid-session upgrade path - the unlock is expected to show up on the *next* session-creation only (a direct
consequence of 01 AC-03, not something 04 needs to engineer).

### 05 - Restore / manage entitlements
**Approach:** a read-only view sitting alongside accounts-identity/03's sign-in screen, guarded by the same
purchaser-credential check. Calls a new, small read endpoint that resolves the signed-in purchaser's grants via
`IEntitlementService` (story 01) and renders them with friendly display names (a small capability-key-to-label
map). **Owns:** the restore/manage view + its read endpoint. **Gotcha:** this is read-only - no write path, no plan
management, no cancellation; keep it that way per the story's Out of Scope.

## Cross-cutting concerns

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
