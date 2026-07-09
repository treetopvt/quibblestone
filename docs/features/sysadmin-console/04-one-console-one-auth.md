# Story: One console, one auth

**Feature:** Sys-Admin Console  ·  **Status:** Complete  ·  **Issue:** #198

## Context
ADR 0003 Layer 3 names this feature's first job: "one bundle, one auth." Today there are two
operator authorization schemes coexisting: the real one (magic-link + Key Vault allowlist,
story 01's `OperatorSession`/`"Operator"` policy, guarding `AdminEntitlementsController` and
`ReportedTalesController`) and an interim one (`IOperatorGate`/`InterimSecretOperatorGate`, a
shared-secret header check, guarding only `StripeModeController`) that lives at a link-less
route inside the KID bundle (`/admin/billing-mode`, `web/src/pages/AdminBillingMode.tsx`). ADR
0003 Decision 3's context and the Layer 3 summary call this out explicitly as a hazard: the
interim gate was always meant to be swapped for the real one the moment story 01 shipped
(`billing-entitlements/06`'s header comment on `IOperatorGate.cs` says exactly this - "a
one-file change here, not an endpoint rewrite" - and `billing-entitlements/07`'s
`AdminBillingMode.tsx` header calls its own route wiring "a DELIBERATE OUTLIER"). Story 01 has
been shipped since 2026-07-07; this story is the swap that was always the plan, done now because
ADR 0003 finally schedules it. It closes the `billing-entitlements/07` "relocation" follow-up
recorded in this feature's 2026-07-07 Decisions entry. See [feature.md](./feature.md) and
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) Layer 3.

## Acceptance Criteria
- [x] AC-01: Given `StripeModeController`'s `GET`/`POST /api/admin/stripe-mode` endpoints, when
      this story ships, then both require `[Authorize(Policy = OperatorSession.PolicyName)]` -
      the SAME `"Operator"` policy `AdminEntitlementsController` and `ReportedTalesController`
      already use - and neither reads or checks the `X-Operator-Secret` header any longer.
- [x] AC-02: Given `IOperatorGate`, `InterimSecretOperatorGate` (`api/src/Billing/IOperatorGate.cs`),
      and the `Admin:ModeToggleSecret` configuration key, when this story ships, then all three
      are deleted (the interface, its implementation, its `Program.cs` DI registration, and any
      `appsettings*.json` / Key Vault reference to the config key) - there is no dead code path
      left behind "just in case."
- [x] AC-03: Given the kid-facing web bundle, when this story ships, then
      `web/src/pages/AdminBillingMode.tsx` is deleted, the `/admin/billing-mode` route and its
      import are removed from `web/src/App.tsx`, and nothing in `web/src/` (the kid PWA) still
      references Stripe mode, the operator secret header, or any admin-only string - verified by
      a grep/bundle audit, the same discipline story 01 AC-04 established.
- [x] AC-04: Given the SEPARATE admin bundle (`web/src/admin/`, story 01), when an operator is
      signed in, then a Stripe-mode panel is reachable there - showing the current active mode
      (Test/Live), when it last changed, and a control to flip it - authenticated the same way
      every other admin screen already is (the bearer credential / cookie from
      `operatorClient.ts`'s session, `PurchaserEntitlements.tsx`'s and `ReviewQueue.tsx`'s
      pattern), never a re-entered shared secret.
- [x] AC-05 (keep the asymmetric go-live friction): Given the new Stripe-mode panel, when an
      operator switches mode, then switching always goes through a confirmation naming both the
      current and target mode, and switching TO Live carries a materially stronger warning (real
      cards will be charged) than switching to Test - the exact asymmetry
      `AdminBillingMode.tsx`'s `ConfirmSwitchDialog` established, ported forward, not diluted.
- [x] AC-06 (no PII / operator-only data): Given the Stripe-mode panel, then it displays only the
      active mode and its last-changed timestamp - no player, room, session, or purchaser data of
      any kind, consistent with `AdminBillingMode.tsx`'s AC-07 this story succeeds.
- [x] AC-07 (closes the tracked follow-up): Given this story ships, then it closes the
      `billing-entitlements/07` "relocate the billing-mode toggle" follow-up recorded in this
      feature's 2026-07-07 Decisions entry - the builder adds one line to
      `billing-entitlements/feature.md`'s Decisions log noting the relocation landed (this
      feature's own file does not touch `billing-entitlements/feature.md`; the builder does, as a
      one-line courtesy edit in that feature's own file).

## Out of Scope
- The three-job shell reorganization (Support / Content / Operations) - that is story 05; this
  story only needs the Stripe-mode panel to exist somewhere reachable in the admin bundle (a
  third tab on the current two-tab shell is an acceptable interim placement that story 05 then
  relocates into Operations).
- Scoped authorization (`support`/`content`/`ops` tags) - that is story 05; this story's AC-01 is
  "the real `Operator` policy," not a scoped variant of it.
- The operator action log - that is story 06; this story does not add any logging of the mode
  flip (story 06 adds that call into `StripeModeController` later).
- Any change to `IActiveStripeContext`, `TableStorageActiveStripeModeStore`, or the Stripe mode
  domain model itself (`billing-entitlements/06`) - this story only swaps the authorization
  mechanism guarding the existing endpoints.
- Any change to the webhook or checkout paths (`billing-entitlements/03-04`) - untouched.

## Technical Notes
- **The swap, precisely:** `StripeModeController`'s constructor currently takes
  `IActiveStripeContext`, `IOperatorGate`, `ILogger<StripeModeController>` and calls
  `IsOperatorAsync` (reading `X-Operator-Secret`) at the top of `Get` and `Set`. Remove the
  `IOperatorGate` dependency and the header check entirely; add `[Authorize(Policy =
  OperatorSession.PolicyName)]` to the controller (mirroring `AdminEntitlementsController`'s and
  `ReportedTalesController`'s exact pattern - `using QuibbleStone.Api.Admin;` for
  `OperatorSession`). ASP.NET Core's authorization middleware then returns 401 before the action
  method runs for anyone without a valid operator credential - no manual `IsOperatorAsync` call
  needed inside the action bodies at all.
- **`Program.cs` hazard (ADR 0003's named systemic hotspot):** this story DELETES the
  `IOperatorGate`/`InterimSecretOperatorGate` service registration and the `Admin:ModeToggleSecret`
  config binding in `Program.cs`. Per ADR 0003's cross-feature build order, `Program.cs` stories
  merge one at a time, small and rebased - do not batch this deletion with an unrelated
  registration change in the same PR.
- **Optional file relocation, not required:** `StripeModeController.cs` currently lives in
  `api/src/Controllers/`, while story 01-03's admin controllers live in `api/src/Admin/`. Moving
  it into `api/src/Admin/` for consistency is a reasonable, low-risk cleanup but is NOT required by
  any AC above - do it only if it does not complicate the `Program.cs` diff.
- **Web panel:** add a new component to the admin bundle (e.g. `web/src/admin/StripeModePanel.tsx`
  + a small `stripeModeClient.ts`, mirroring `purchasersClient.ts`'s shape: `VITE_API_BASE_URL`
  base, bearer-credential-aware fetch, graceful fallback on transport failure). Port
  `AdminBillingMode.tsx`'s `ConfirmSwitchDialog` behavior (AC-05) rather than rewriting it from
  scratch - it is proven UX, just re-homed. The panel reuses `web/src/theme.ts` (the shared visual
  language `web/src/admin/main.tsx`'s header already documents as "explicitly allowed" for this
  bundle) - do NOT reintroduce `AdminBillingMode.tsx`'s separate `adminTheme.ts` nesting for this
  panel; that theme was a workaround for living in the kid bundle's route tree and is no longer
  needed once the panel lives natively in the already-separate admin bundle. (`adminTheme.ts`
  itself can stay in the tree unused, or be deleted as a follow-on cleanup - not required by any AC
  here.)
- **Where the panel is placed in the shell (interim):** `web/src/admin/main.tsx` is currently a
  two-tab shell (`review` / `entitlements`, see its header comment). Add a third tab ("Stripe
  mode") for this story rather than waiting on story 05's three-job reorganization - story 05 then
  relocates it into the Operations tab. Keeping this story's shell footprint additive (one more
  tab) keeps 04 and 05 file-disjoint enough to land in the wave order ADR 0003 specifies (04 in
  wave 1, 05 in wave 2).
- **Tests:** existing `StripeModeController` tests that assert 401 on a missing/wrong
  `X-Operator-Secret` header must be rewritten to assert 401 on a missing/invalid operator bearer,
  mirroring `AdminEntitlementsController`'s and `ReportedTalesController`'s existing authorization
  tests (`OperatorAuthorizationTests.cs`'s pattern: a genuine purchaser credential is rejected, an
  allowlisted operator credential succeeds).

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Admin/StripeModeControllerTests.cs (relocated/rewritten from Controllers): GET/POST without an operator credential returns 401; with an allowlisted operator credential returns 200.` |
| AC-02 | `manual: grep for IOperatorGate, InterimSecretOperatorGate, Admin:ModeToggleSecret across api/ and infra/ - zero hits after this story.` |
| AC-03 | `manual + static: bundle/route audit - grep web/src (kid bundle) for AdminBillingMode, billing-mode, X-Operator-Secret - zero hits; the route no longer exists in App.tsx.` |
| AC-04 | `web/src/admin/stripeModeClient.test.ts (mirrors purchasersClient.test.ts): the panel fetches and displays the active mode using the operator bearer credential, not a secret prompt.` |
| AC-05 | `manual: exercise the panel - switching to Live shows the stronger warning copy; switching to Test shows the milder one; both require explicit confirmation.` |
| AC-06 | `manual: code review of the panel - confirm no field beyond mode + timestamp is rendered or fetched.` |
| AC-07 | `manual: confirm the one-line Decisions entry lands in docs/features/billing-entitlements/feature.md when this story's PR merges.` |

## Dependencies
- `sysadmin-console/01` (#135, Complete) - the `"Operator"` policy and admin bundle this story's
  panel is authorized by and placed in.
- `billing-entitlements/06` (#TBD, Complete "interim gate") - the `IActiveStripeContext` domain
  model this story's panel keeps calling; only the authorization mechanism changes.
- none technically blocks this story from starting; it is independent within this feature's own
  Wave Plan (see `implementation.md`).
