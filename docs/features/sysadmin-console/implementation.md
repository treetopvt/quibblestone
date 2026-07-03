<!--
  Implementation plan for the sysadmin-console feature. Bridges feature.md + stories to orchestration.
  Written now that ADR 0002 Decisions A-F are resolved and the three candidate stories are decomposed into
  full story files. Two of this feature's dependency seams (billing-entitlements/01's IEntitlementService,
  accounts-identity/02's magic-link plumbing) are still unbuilt, even though the IEntitlementService interface it
  consumes already shipped (ai-cost-gate/02 #121 / PR #132) - see each story's "dependency reality" note and the
  Cross-cutting concerns below. Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Sys-Admin Console

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Magic-link one-time-token issue/verify plumbing | `accounts-identity/02`'s token issuer/verifier (or story 01's thin contract-compatible stand-in, if #68 has not landed) | `api/src/Accounts/` (that feature) |
| Purchaser identity lookup by email | `IAccountStore` | `api/src/Accounts/IAccountStore.cs` (accounts-identity/02) |
| Entitlement seam (evaluate) | `IEntitlementService` + `SessionEntitlements` - ALREADY SHIPPED (thin, default-unlocked), captured at `GameHub.CreateRoom` | `api/src/Entitlements/IEntitlementService.cs` (ai-cost-gate/02 #121) |
| Entitlement grant store (grant/revoke writes) | the lease-shaped `EntitlementGrant` (`validThrough` + `source`) - NOT yet built | `api/src/Entitlements/` (billing-entitlements/01 #70; or story 02's contract-compatible stand-in) |
| Capability-key catalog | the shared string catalog (`library.full`, `play.remote`, `play.largeGroup`, `ai.*`, `pack.<id>`) | `api/src/Entitlements/CapabilityKey.cs` (billing-entitlements/01) |
| Public tale storage + serving | `IPublishedTaleStore`, `PublishedTalesController`, `SlugGenerator` | `api/src/PublishedTales/` (keepsake-gallery/04) |
| Per-IP anonymous-endpoint rate limiting | `PublishTalesRateLimit`'s fixed-window-per-IP pattern + `ForwardedHeaders` wiring | `api/src/PublishedTales/PublishTalesRateLimit.cs`, `api/src/Program.cs` |
| Child safety (authoritative filter, never reimplemented) | `IContentSafetyFilter` | `api/src/Safety/` |
| Service registration pattern (singleton DI) | the existing `RoomRegistry` / `IContentSafetyFilter` registrations | `api/src/Program.cs` |
| Secrets (operator allowlist, session-signing keys, Stripe/OAuth keys) | Azure Key Vault | `infra/main.bicep` (`keyVault` resource) |
| Durable storage (operator sessions if persisted, entitlement grants, published tales/reports) | Azure Table Storage | `infra/main.bicep` (`storage` resource) |
| Styling / theme tokens (reused in the SEPARATE admin bundle, not shared UI) | the MUI theme | `web/src/theme.ts` |
| Icons | FontAwesome, registered once (in the admin bundle's own registration, mirroring the kid app's) | `web/src/fontawesome.ts` |
| Config (non-secret) | `import.meta.env` (`VITE_*`) - never the operator allowlist or any signing key | `web/src/vite-env.d.ts`, `web/.env.development` |

New surfaces this feature introduces (not yet reuse targets, become them once built):
- `api/src/Admin/` - `IOperatorAllowlist`, the operator authentication handler/middleware, the "Operator"
  authorization policy (story 01) - the contract stories 02 and 03's admin-only endpoints all authorize against.
- A separate admin web bundle/route tree (e.g. `web/admin/`, naming TBD at build time) - story 01's login
  screen, extended by story 02's purchaser-lookup page and story 03's review-queue page. Never imported by,
  or code-split into, `web/src/` (the kid PWA).
- `PublishedTale` extensions for reports + hidden state (story 03) - a new small surface on the existing
  `api/src/PublishedTales/` folder, not a new domain.

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. Story 01 is the hard
foundation - both 02 and 03 authorize every endpoint they add against its operator policy, and both need its
separate bundle/route tree to place their own pages in. 03 has no dependency on 02 (and vice versa), so once 01
lands they can build in parallel; the true serialization risk is each story's OWN external dependency
(accounts-identity/02 for 01, billing-entitlements/01 for 02), not each other.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 operator-login-and-admin-boundary (foundation) | #135 | `api/src/Admin/IOperatorAllowlist.cs`, `OperatorAuthenticationHandler.cs`, the "Operator" auth policy in `Program.cs`; new admin web bundle root (`web/admin/` or equivalent) + its login page | accounts-identity/02 (or a thin stand-in it builds itself) | - | 1 | high |
| 03 report-and-takedown-public-tale | #137 | `api/src/PublishedTales/` report/hidden-state additions + `ReportTalesRateLimit.cs`; new `api/src/Admin/ReportedTalesController.cs`; admin web review-queue page | 01, keepsake-gallery/04 | 02 (disjoint files) | 2 | medium |
| 02 operator-grant-revoke-entitlement | #136 | new `api/src/Admin/AdminEntitlementsController.cs`; admin web purchaser-lookup/grant page | 01, billing-entitlements/01, accounts-identity/02 | 03 (disjoint files) | 2 | medium |

**Concurrency per wave:** Wave 1 = 1 (01, the operator-auth foundation - must land first; both 02 and 03 import
its authorization policy and place pages in its bundle). Wave 2 = {02, 03} in parallel (disjoint API controllers
and disjoint admin pages; 03 additionally depends on `keepsake-gallery/04` which already shipped, so it is
realistically the more "ready-now" of the two, while 02's usefulness is gated on real charging landing - see
feature.md's Candidate stories table). Story numbering (01-02-03) reflects the feature.md build-order narrative
(foundation, then "pairs with real charging," then "actionable now"); the Wave Plan reorders 02 and 03 into the
same wave because neither blocks the other technically - their only true blockers are each one's own external
seam (#70 for 02, nothing new for 03 beyond #66 which is already shipped).

## Per-story tech notes

### 01 - Operator login and admin boundary (foundation)
**Approach:** reuse `accounts-identity/02`'s magic-link one-time-token issue/verify plumbing (or, per that
story's Technical Notes, a thin contract-compatible token issuer/verifier this story builds if #68 has not
landed - issue-by-email, verify-once, short-lived, later subsumed). Add `IOperatorAllowlist` reading emails
from Key-Vault-backed configuration, and an authorization policy (`"Operator"`) that a verified token's email
must satisfy before any admin endpoint is reached. Stand up a separate web bundle/route tree - its own entry
point, never imported by or linked from `web/src/` - carrying just the login screen for now. **Exports:** the
`"Operator"` authorization policy and the admin bundle's route root - every later admin endpoint/page (stories
02, 03) is a consumer of both. **Gotcha:** the load-bearing guard (AC-03) is that a purchaser's own signed-in
session must be structurally rejected by the same policy check, not merely "no visible admin link for a
purchaser" - verify with an explicit negative test, not just a UI audit.

### 03 - Report and takedown of a public tale
**Approach:** extend `api/src/PublishedTales/` (do not fork a parallel store) with a report count/collection
and a hidden/under-review state on `PublishedTale`, a public `POST /api/tales/{slug}/report` endpoint reusing
`PublishTalesRateLimit`'s per-IP fixed-window pattern (a sibling policy, same tunable shape), and a small
`ReportedTalesController` in `api/src/Admin/` behind story 01's `"Operator"` policy for the review queue
(list/confirm/restore). Web: a report control on the existing server-rendered `GET /t/{slug}` page (plain markup
+ fetch, matching that page's no-SPA-import approach) and a review-queue page in the admin bundle. **Exports:**
the hidden/under-review state other future moderation work (if any) would read. **Gotcha:** AC-02 requires the
under-review page to read differently from the existing 404 "expired/unshared" page - do not collapse the two
states, a host who revoked their own tale should not look like a moderated bad actor.

### 02 - Operator grant/revoke an entitlement by purchaser email
**Approach:** a small `AdminEntitlementsController` in `api/src/Admin/` behind story 01's `"Operator"` policy,
calling `IAccountStore` (accounts-identity/02) for the email lookup and `IEntitlementService`'s grant-store
write path (billing-entitlements/01) for grant/revoke, writing the exact lease-shaped `EntitlementGrant`
(`validThrough` + `source = operator`) that story's Stripe webhook also writes to - same store, same shape, a
different `source`. Web: a minimal purchaser-lookup + grant/revoke page in the admin bundle. **Exports:**
nothing new beyond the endpoints themselves - this story is a thin consumer of two other features' seams, not a
producer other stories build on. **Gotcha:** AC-04's anonymity boundary is absolute - resist any temptation to
add a "which sessions did this purchaser's household create" convenience view; that is exactly the join ADR
0002 forbids.

## Cross-cutting concerns

- **The anonymity firewall applies to every admin action.** No admin surface (present or future) may join a
  purchaser identity to a player nickname, room, or session - purchaser support operates on the purchaser plane
  (email -> account -> grant); moderation operates on published *content* (a slug), never on the anonymous
  author. Any story or future addition that proposes "look up which room a purchaser played in" is the bug ADR
  0002 exists to prevent, and should be rejected in review exactly as `CreateRoom` is guarded.
- **`purchaser == admin` must be impossible, structurally.** Every admin endpoint checks the operator
  allowlist/authorization policy from story 01, never mere sign-in status. A purchaser's own credential
  (accounts-identity/03) must be rejected by every admin authorization check, verified by an explicit test, not
  assumed from UI-level obscurity.
- **A separate bundle/route tree, never the kid PWA.** The back office is its own entry point with its own auth.
  No admin-only code, string, or route is ever bundled into, imported by, or reachable from `web/src/` (the
  anonymous, kid-facing app) - verify this with a bundle/import audit whenever a new admin page is added, not
  just a "no visible nav link" check.
- **No PII beyond what each admin action strictly needs.** The operator's own email (story 01), a purchaser's
  email (story 02), and a reported tale's slug (story 03) are the only identifiers this feature ever touches -
  no player nickname, device id, IP-to-person mapping, or any other PII is collected, displayed, or logged
  anywhere on this surface.
- **Key Vault for the operator allowlist and any signing keys, never `VITE_*`.** Consistent with every other
  secret in the app (Stripe keys, OAuth client secrets) - no exceptions, no "just for dev" shortcuts committed
  to the repo.
- **Reuse, never reimplement, the seams this feature consumes.** `IContentSafetyFilter` (child safety),
  `IEntitlementService` (billing-entitlements), `IAccountStore` (accounts-identity), and
  `PublishTalesRateLimit`'s per-IP posture (keepsake-gallery) are each a single source of truth this feature
  reads from or writes through - never a parallel implementation, per feature.md's "what is NOT this feature"
  table (cost/abuse oversight stays on App Insights + Cost Management; content vetting stays the
  content-factory queue; refunds stay Stripe's dashboard).
- **The entitlement interface is shipped; the paid-tier seams it extends are not.** `IEntitlementService` +
  `SessionEntitlements` + the `GameHub.CreateRoom` capture already ship (ai-cost-gate/02 #121 / PR #132) as a
  thin, default-unlocked, read-only stand-in. Still unbuilt: `billing-entitlements/01`'s grant store + full
  catalog (#70) that story 02 WRITES to, and `accounts-identity/02`'s magic-link + purchaser account (#68, no
  `api/src/Accounts/`) that stories 01 and 02 reuse. Each affected story (01 for the token plumbing, 02 for the
  grant store + account lookup) names its own thin, contract-compatible fallback so this feature is not
  hard-blocked on either landing first - but the public shape of each seam is the OTHER feature's contract,
  never re-derived here.
- **Operator, not audit ceremony.** This is a toy, not a system of record (CLAUDE.md preamble) - resist growing
  any of these three stories into role hierarchies, audit trails, or approval workflows. Minimal operator
  convenience is the whole brief.
- **No i18n** (plain strings). **No em dashes.** The admin bundle reuses the MUI theme for visual consistency,
  but it is an adult-facing operator tool, not a second consumer-facing design system - keep its screens
  minimal and functional rather than polishing them to the kid app's delight-tier bar.
