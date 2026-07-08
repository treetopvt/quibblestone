<!--
  Implementation plan for the sysadmin-console feature. Bridges feature.md + stories to orchestration.
  Written now that ADR 0002 Decisions A-F are resolved and the three candidate stories are decomposed into
  full story files. Two of this feature's dependency seams (billing-entitlements/01's IEntitlementService,
  accounts-identity/02's magic-link plumbing) are still unbuilt, even though the IEntitlementService interface it
  consumes already shipped (ai-cost-gate/02 #121 / PR #132) - see each story's "dependency reality" note and the
  Cross-cutting concerns below. Use hyphens/colons/parentheses, never em dashes.

  EXTENDED 2026-07-08 for ADR 0003 Layer 3 (stories 04-07): a second Wave Plan section below covers the new
  stories, using the ADR's own cross-feature wave numbers (1-4) rather than continuing the already-shipped
  01-03 Wave Plan's numbering (that table is historical - 01-03 are Complete). See ADR 0003's "Cross-feature
  build order" table for how these four waves line up against accounts-identity, keepsake-vault, control-plane,
  and billing-entitlements' own concurrent stories.

  REVISED 2026-07-08 (adversarial review): the historical 01-03 Wave Plan section is retitled to make explicit
  it is NOT DAG-parsed and its "Wave 1" is a different number from the canonical table's Wave 1 (04). Stories
  05-07 also gained binding fixes from the review (05's Operator:Scopes config shape, 06's log-before-act
  ordering + retention floor + settings-action coverage + view-encoding AC, 07's structural cross-plane
  firewall + resend/resync rate-limit hardening + restore-friction asymmetry) - see each story file and ADR
  0003's "Security posture" section. See feature.md's Decisions log for the summary entry.
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

**EXTENDED 2026-07-08 for ADR 0003 Layer 3 (stories 04-07):**

| Concern | Reuse | Where |
|---|---|---|
| Operator authorization (existing) | `OperatorSession`/`OperatorAuthenticationHandler`/`IOperatorAllowlist`, the `"Operator"` policy | `api/src/Admin/` (story 01, shipped) |
| Existing admin controllers (existing) | `AdminEntitlementsController` (grant/revoke), `ReportedTalesController` (confirm/restore) | `api/src/Admin/` (stories 02/03, shipped) |
| Interim Stripe-mode gate (story 04 DELETES this) | `IOperatorGate`/`InterimSecretOperatorGate` | `api/src/Billing/IOperatorGate.cs` (billing-entitlements/06) - deleted, not reused |
| Stripe mode domain model (kept, only its authorization changes) | `IActiveStripeContext`, `StripeModeController` | `api/src/Controllers/StripeModeController.cs` (billing-entitlements/06) |
| Admin bundle shell (existing, reorganized by 05) | the two-tab `AdminShell` in `main.tsx`, extended to three tabs | `web/src/admin/main.tsx` (story 01, shipped) |
| Existing admin screens (existing, relocated by 05) | `PurchaserEntitlements.tsx`, `ReviewQueue.tsx` | `web/src/admin/` (stories 02/03, shipped) |
| Bearer-credential-aware client pattern | `operatorClient.ts` / `purchasersClient.ts`'s fetch shape (VITE_API_BASE_URL, graceful failure, bearer + cookie) | `web/src/admin/*Client.ts` |
| Table Storage store pattern (for the new action log, story 06) | `TableStorageActiveStripeModeStore.cs` / the `IEntitlementGrantStore` implementation's partition/row-key conventions | `api/src/Billing/`, `api/src/Entitlements/` |
| Entitlement grant plumbing (reused by story 07's comp/extend verb) | `IEntitlementGrantStore`, `AdminEntitlementsController`'s `BuildLookupAsync` projection | `api/src/Admin/AdminEntitlementsController.cs`, `api/src/Entitlements/` |
| Account lookup by email (reused, extended by accounts-identity/05 for AccountId) | `IAccountStore` | `api/src/Accounts/` |
| Published tale store (reused by story 07's TTL-extend verb) | `IPublishedTaleStore` | `api/src/PublishedTales/` |
| Magic-link email delivery (reused by story 07's resend verb) | the `accounts-identity/04` email seam | `api/src/Accounts/` |
| Public sign-in rate-limit policy (REVISED 2026-07-08 - reused by story 07's resend verb, not bypassed) | `SignInRateLimit`'s per-IP fixed-window pattern, `[EnableRateLimiting(SignInRateLimit.PolicyName)]` | `api/src/Accounts/SignInRateLimit.cs`, `api/src/Controllers/AccountsController.cs` |

New surfaces stories 04-07 introduce:
- `api/src/Admin/OperatorScope.cs` + a scope requirement/attribute (story 05) - the `support`/`content`/`ops`
  metadata every admin endpoint carries; stories 06 and 07's new endpoints consume this from day one.
- `api/src/Admin/IOperatorActionLog.cs` + `TableStorageOperatorActionLog.cs` (story 06) - the single append-only
  write seam stories 02/03/04's controllers call into, and story 07's verbs call into once built.
- `api/src/Admin/AccountSupportController.cs` (story 07) - the Support job's composing endpoint; optionally
  depends on seams from `accounts-identity`, `keepsake-vault`, and `billing-entitlements/08` that may not exist
  yet (dependency-tolerant, null-object/absent-service pattern - see story 07's Technical Notes).
- `web/src/admin/OperationsPanel.tsx`, `SettingsPanel.tsx`, `StripeModePanel.tsx`, `ActionLogView.tsx`,
  `SupportLookup.tsx` (stories 04-07) - the new/reorganized admin-bundle screens.

## Historical Wave Plan (01-03, shipped - reference only, NOT DAG-parsed)

**Kept deliberately OUT of the DAG-parsed section (2026-07-08 adversarial review, ADR 0003's
cross-feature build order table).** Stories 01-03 shipped 2026-07-07; their "Wave 1 / Wave 2" numbers
below are LOCAL to this now-historical table and are NOT the same numbers as ADR 0003's canonical
cross-feature Wave 1-4 used by stories 04-07 below - an orchestrator grouping by "Wave 1" across this
whole file must not conflate this table's Wave 1 (story 01) with the canonical Wave 1 (story 04). This
table is retained for history only; the section below ("Wave Plan (DAG) - canonical") is the one a
DAG-based orchestrator should parse for anything still in flight.

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

## Wave Plan (DAG) - canonical (ADR 0003 wave numbers, stories 04-07)

**EXTENDED 2026-07-08, RE-CONFIRMED 2026-07-08 (adversarial review).** This is the canonical,
DAG-parsed Wave Plan for this feature's currently-buildable stories. It uses ADR 0003's OWN
cross-feature Wave numbers (1-4), since these four stories are one row of that ADR's larger
cross-feature table (alongside `accounts-identity`, `keepsake-vault`, `control-plane`,
`billing-entitlements`, `platform-devops` stories building in the same waves): **04 = Wave 1, 05 =
Wave 2, 06 = Wave 3, 07 = Wave 4.** It does NOT continue the wave numbering of the historical table
above (that table's "Wave 1/2" are local, historical labels for the already-shipped 01-03 - do not
read "Wave 1" across this file as a single sequence; the historical table's Wave 1 is story 01, this
table's Wave 1 is story 04, and they are unrelated numbers that happen to reuse the digit "1").

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 04 one-console-one-auth | #TBD | `api/src/Controllers/StripeModeController.cs` (auth swap); DELETES `api/src/Billing/IOperatorGate.cs`; `Program.cs` (deletes the `IOperatorGate` registration + `Admin:ModeToggleSecret` config); DELETES `web/src/pages/AdminBillingMode.tsx`; `web/src/App.tsx` (removes the route + import); new `web/src/admin/StripeModePanel.tsx` + `stripeModeClient.ts`; `web/src/admin/main.tsx` (adds a third interim tab) | sysadmin-console/01 (shipped) | - (independent within this feature; see cross-feature table for what else is in ADR 0003's Wave 1) | 1 | medium |
| 05 jobs-shell-and-scoped-authz | #TBD | `web/src/admin/main.tsx` (reorganizes to 3 tabs); new `web/src/admin/OperationsPanel.tsx`, `SettingsPanel.tsx`; new `api/src/Admin/OperatorScope.cs`; edits to `OperatorAuthenticationHandler.cs` / `ConfigurationOperatorAllowlist.cs` (scope resolution); `Program.cs` (registers scope policies); one-line scope-attribute additions to `AdminEntitlementsController.cs`, `ReportedTalesController.cs`, `StripeModeController.cs` | 04 | - | 2 | medium |
| 06 operator-action-log | #TBD | new `api/src/Admin/IOperatorActionLog.cs`, `TableStorageOperatorActionLog.cs`, `ActionLogController.cs` (or similar); `Program.cs` (registers the log store); ONE new `AppendAsync` call each in `AdminEntitlementsController.cs`, `ReportedTalesController.cs`, `StripeModeController.cs`; new `web/src/admin/ActionLogView.tsx` + `actionLogClient.ts` (placed in 05's Operations tab); `docs/features/sysadmin-console/feature.md` (Design notes amendment, AC-07) | 05 (for the view only - the write seam itself has no technical dependency on 05, see Cross-cutting concerns and story 06's Technical Notes "the split") | - | 3 | medium |
| 07 support-lookup-and-verbs | #TBD | new `api/src/Admin/AccountSupportController.cs`; new `web/src/admin/SupportLookup.tsx` + `supportClient.ts` | 06; dependency-tolerant on `accounts-identity/05`+`/09`, `keepsake-vault/03-04`, `billing-entitlements/08` (each may be absent at build time - see story 07's Technical Notes for the null-object/absent-service pattern) | - | 4 | high |

**Concurrency per wave:** Wave 1 = 04 alone within this feature (independent of 05-07; ADR 0003's
cross-feature Wave 1 also includes `accounts-identity/05`, `keepsake-vault/01`, `control-plane/01`,
`platform-devops/08` (the durable key ring; the second environment is already shipped as
`platform-devops/07`) - all in DIFFERENT features' folders, not this one's concern to track file-
level). Wave 2 = 05 alone (depends on 04's Stripe panel existing to relocate). Wave 3 = 06 alone
(its VIEW needs 05's Operations tab; its WRITE seam could theoretically land earlier - noted as a
possible split for the orchestrator's Phase 1 to decide, not assumed here). Wave 4 = 07 alone
(consumes 06's log; everything else it touches is dependency-tolerant, so it need not wait on
`accounts-identity`, `keepsake-vault`, or `billing-entitlements/08` landing - it just renders less
until they do).

**The `Program.cs` hazard, restated for this batch:** stories 04, 05, and 06 EACH touch `Program.cs`
(04 deletes a registration; 05 adds scope-policy registrations; 06 adds the action-log store
registration). Per ADR 0003's rule, these are three separate, small, rebased PRs - never batched
into one `Program.cs` diff even though they land in adjacent waves of the same feature.

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

### 04 - One console, one auth
**Approach:** delete `IOperatorGate`/`InterimSecretOperatorGate` and the `Admin:ModeToggleSecret`
config key; swap `StripeModeController`'s guard from a manual `X-Operator-Secret` header check to
`[Authorize(Policy = OperatorSession.PolicyName)]` (the exact attribute `AdminEntitlementsController`
and `ReportedTalesController` already use). Delete `web/src/pages/AdminBillingMode.tsx` and its
route/import in `App.tsx`. Port its `ConfirmSwitchDialog` asymmetric-friction behavior into a new
`web/src/admin/StripeModePanel.tsx`, placed as an interim third tab on the existing two-tab shell
(story 05 relocates it into Operations next wave). **Exports:** the Stripe-mode panel component
story 05 relocates. **Gotcha:** this is a `Program.cs`-touching deletion - land it as its own small,
rebased PR (ADR 0003's hazard). Also: do not resurrect `adminTheme.ts`'s separate theme nesting for
the new panel - it lived in the kid bundle's route tree for a reason that no longer applies once the
panel is natively in the admin bundle.

### 05 - The jobs shell + scoped authz
**Approach:** rework `main.tsx`'s tab set to Support/Content/Operations, relocating
`PurchaserEntitlements` (Support), `ReviewQueue` (Content), and 04's Stripe-mode panel plus a new,
dependency-tolerant `SettingsPanel` (Operations). Add `OperatorScope.cs` (constants + a requirement/
attribute pair) and named policies (or an equivalent attribute-metadata mechanism) that
`OperatorAuthenticationHandler`/`ConfigurationOperatorAllowlist` resolve per-email - today every
allowlisted operator gets every scope, so no existing test changes behavior. Apply the appropriate
scope to each of the three existing admin controllers. **REVISED 2026-07-08 (adversarial review):**
also define and ship the per-entry `Operator:Scopes` config key NOW (index-aligned with
`Operator:AllowedEmails`, same dual array/delimited-scalar read pattern), defaulting an
unconfigured entry to all three scopes - so a future de-scoped operator is a config value, not a
schema change (AC-06). **Exports:** the scope mechanism stories 06 and 07's new endpoints consume
from day one (their new controllers carry a scope tag immediately, never retrofitted), and the
`Operator:Scopes` config format itself, ready for a real restricted entry the moment one is needed.
**Gotcha:** the settings panel's dependency-tolerance (control-plane/01 may not exist yet) must
degrade to a plain message, never an unhandled rejection - mirror the existing session-check's
fail-safe pattern in `main.tsx`.

### 06 - The operator action log
**Approach:** a new `IOperatorActionLog` + `TableStorageOperatorActionLog` (inverted-ticks RowKey
for newest-first listing, mirroring the existing Table Storage store conventions), a `GET
/api/admin/action-log` endpoint under the `ops` scope (story 05), and one new `AppendAsync` call
each in `AdminEntitlementsController` (grant, revoke), `ReportedTalesController` (confirm, restore),
and `StripeModeController` (the mode flip) - each call now made BEFORE its action's effectful write
is attempted, not after (**REVISED 2026-07-08, adversarial review**: log-before-act, AC-01a - an
append failure aborts the request before any effect runs, rather than letting an effect commit with
no trail), still never on an early-return/not-found branch (AC-05 unchanged). Retention is age-based
with a hard-coded minimum-days floor no runtime override can lower (AC-04, replaces the earlier
bare row-count cap). `ActionLogView.tsx` never uses `dangerouslySetInnerHTML`; the write side
validates any email-shaped target before persisting (AC-07). **Exports:** the `IOperatorActionLog`
seam story 07's verbs call into once built, AND `control-plane/01`'s settings controller once it
lands (a free-form action-name string, not a closed enum, so neither caller needs a code change
here). **Gotcha:** the VIEW depends on story 05's Operations tab; the WRITE seam (the interface, the
store, and the six call-site insertions) does not - a builder could split this story to land the
write seam in parallel with 05 if the orchestrator's Phase 1 finds that valuable, since none of
those call sites touch `main.tsx`. Also: this story edits `feature.md`'s Design notes (the
audit-trail amendment, now also naming the settings-change action) - a documentation edit, not a
code footprint conflict with anything else in flight.

### 07 - Support lookup + verbs
**Approach:** a new `AccountSupportController` composing `IAccountStore`, `IEntitlementGrantStore`,
`IPublishedTaleStore`, and `IOperatorActionLog` (all shipped or landing in earlier waves) alongside
OPTIONAL seams from `accounts-identity/05`+`/09`, `keepsake-vault`, and `billing-entitlements/08`
(none shipped yet at the time this story is likely to build) - inject those as nullable/absent-by-
default services so each panel/verb on the web side can report "not available yet" independently
rather than the whole controller failing. **REVISED 2026-07-08 (adversarial review, the firewall is
now structural):** lookup is EMAIL/`AccountId` only - a vault claim code and a public-tale slug are
permanently removed as account-lookup inputs (claim-code recovery moves to a player-facing,
device-held capability `keepsake-vault` owns directly; a slug is acted on as content only, via
extend-TTL/restore, never resolved to an account). The vault/tale figure on the detail panel is
sourced from a narrow, count-only contract this story requests from `keepsake-vault` - never from
`IVaultStore.ListAsync` (which returns bylines) - so the controller's constructor cannot even HOLD a
byline-capable dependency. The resend-magic-link verb reuses `SignInRateLimit`'s policy (not a
direct, unthrottled `IEmailSender` call) plus a new per-target-account window; the resync verb gets
an equivalent per-account debounce. The self-delete restore verb (AC-05) is explicitly a
LOWER-friction, DISTINCT verb from story 03's moderation-takedown restore - never merged. **Exports:**
nothing further - this is this feature's final story in the current decomposition. **Gotcha:** AC-08's
firewall is now a structural claim (what the controller CAN reach), not just a review discipline -
verify it by checking the controller's constructor signature and the response DTO shape, not only by
reading its action-method bodies; a byline-capable dependency injected but "just not called yet" is
still the defect the 2026-07-08 review flagged.

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

**EXTENDED 2026-07-08 for ADR 0003 Layer 3 (stories 04-07):**

- **`Program.cs` is this feature's own recurring hotspot across 04-06.** Story 04 deletes the
  `IOperatorGate` registration; story 05 adds scope-policy registrations; story 06 adds the
  action-log store registration. Each is its OWN small, rebased PR - per ADR 0003's cross-feature
  rule, `Program.cs`-touching stories never batch, even within one feature's own wave sequence.
- **Scoped authz is additive metadata, not a rewrite.** Story 05's `support`/`content`/`ops` tags
  must not change existing behavior for the single, all-scopes-allowlisted operator - every existing
  test for `AdminEntitlementsController`, `ReportedTalesController`, and `StripeModeController` must
  keep passing UNMODIFIED after the scope attributes land. A test that had to change to keep passing
  is a signal the scope mechanism accidentally narrowed something.
- **The action log is dispute insurance, not a system of record.** Story 06 amends this feature's own
  "no audit ceremony" stance narrowly (ADR 0003 Decision 3 / Amendment 2) - for exactly the named
  money/moderation/settings actions, nothing broader. No story in this feature should extend the log
  to gameplay, content generation, or any player-facing action; that would be the "audit ceremony"
  this feature has twice now explicitly rejected (once in the original Design notes, once in the
  Amendment 2 scope-limit).
- **The log is trustworthy dispute insurance, not just present (REVISED 2026-07-08, adversarial
  review).** Two properties make story 06's log actually usable in a dispute rather than theater:
  (a) log-before-act ordering (AC-01a) - a row is written BEFORE the effectful action proceeds, so an
  action cannot commit with zero trail; (b) an age-based retention floor (AC-04) that no runtime
  setting can lower - the party a dispute is about cannot volume- or config-evict the rows that
  concern them. Both are binding on story 06, not aspirational.
- **Dependency-tolerant panels degrade individually, never as a whole page.** Stories 05's settings
  panel and 07's subscription/vault/device sections each check their OWN backing endpoint's
  availability and render a plain "not available yet" state on absence - modeled on
  `ReportedTalesController`'s existing "disabled fallback returns an empty queue" precedent. No
  panel's absence should ever crash, blank, or otherwise degrade a SIBLING panel on the same screen.
- **The anonymity/content firewall is structural on the Support surface (REVISED 2026-07-08,
  adversarial review - story 07), not a review discipline applied to a wider surface.** The prior
  framing ("more identifiers, more verbs, hold it to the same bar") undersold the fix: story 07's
  controller does not merely avoid CALLING a byline-capable dependency, it does not HOLD one - claim
  codes and tale slugs are permanently removed as account-lookup inputs (see story 07's AC-01/AC-08),
  and the vault/tale figure is sourced from a count-only contract, never `IVaultStore.ListAsync`. Hold
  story 07 to this structural bar in review: check the constructor's dependency types and the
  response DTO shape, not only the action-method bodies.
