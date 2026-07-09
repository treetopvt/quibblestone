<!--
  Feature exploration for the sys-admin surface. Companion to
  docs/adr/0002-accounts-subscriptions-and-admin.md. The owner resolved ADR 0002 decisions A-F on
  2026-07-03: the standing finding still holds - this is NOT a monolithic "admin site," most of what
  the phrase evokes is already owned by other features or Azure - but Decision B greenlit standing up
  a SEPARATE, auth-gated back office now, with three thin first stories (magic-link operator login,
  grant/revoke, report/takedown). Decomposed the same day: implementation.md + full story files now
  exist alongside this feature.md. Use hyphens/colons/parentheses, never em dashes.
-->

# Feature: Sys-Admin Console

## Summary
A separate, auth-gated back office for QuibbleStone: the surface a solo operator (later, a human
moderator) uses to keep the paid product healthy - purchaser/subscription support and moderation
review of public content. The headline finding of the exploration (ADR 0002) still stands: **this is
not one big "admin site."** Most of what the phrase evokes is already served by another feature or by
an Azure surface; only two responsibilities are genuinely new and admin-only. What changed on
2026-07-03: the owner (ADR 0002 Decision B) elected to stand the back office up **now** rather than
mint it on first need, so those two responsibilities became its first stories - and this file was
decomposed the same day into three full story files plus an implementation.md.

> **Decomposed.** ADR 0002 Open Decisions A-F are all resolved (see its Decision section), and this
> feature is fully specified: three story files (`01-03`) plus `implementation.md` (reuse map + Wave
> Plan) exist alongside this feature.md. The entitlement *interface* it consumes is already shipped
> (`IEntitlementService`, captured at `GameHub.CreateRoom`, ai-cost-gate/02 #121 / PR #132), but the
> two seams the paid-tier work extends are still unbuilt: `billing-entitlements/01`'s grant store +
> full catalog (#70) and `accounts-identity/02`'s magic-link + purchaser account (#68, no
> `api/src/Accounts/` yet). See each story's "dependency reality" note. GitHub issues:
> 01 = #135, 02 = #136, 03 = #137.

## README reference
README section 3 (Monetization - the tiered identity model and "only the purchaser gets a
lightweight account"), section 6 (Child Safety & Moderation - "a strong moderation pipeline",
minimal data on minors), and section 7 (Epic Map - Phase 2 "AI Content Factory - back office" and
Phase 2 monetization). CLAUDE.md section 5 (child safety non-negotiable) and section 6 (the
monetization seam). Full rationale + the load-bearing invariant:
[ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Timing | Status |
|---|---|---|---|---|
| [01](./01-operator-login-and-admin-boundary.md) | #135 | Operator login and admin boundary (separate surface) | foundation - build first | Complete |
| [02](./02-operator-grant-revoke-entitlement.md) | #136 | Operator grant / revoke an entitlement by purchaser email | pairs with real charging (`billing-entitlements/03-04`) | Complete |
| [03](./03-report-and-takedown-public-tale.md) | #137 | Report -> auto-hide-after-N -> operator review of a public tale | actionable now (public tales already shipped) | Complete |
| [04](./04-one-console-one-auth.md) | #198 | One console, one auth (relocate the Stripe toggle behind the real Operator policy) | ADR 0003 Layer 3 - independent, build first of the four | Complete |
| [05](./05-jobs-shell-and-scoped-authz.md) | #214 | The jobs shell (Support / Content / Operations) + scoped authz | ADR 0003 Layer 3 - depends on 04 | Complete |
| [06](./06-operator-action-log.md) | #233 | The operator action log (ADR 0003 Decision 3 / Amendment 2) | ADR 0003 Layer 3 - depends on 05 for its view | Not Started |
| [07](./07-support-lookup-and-verbs.md) | #TBD | Support lookup + verbs | ADR 0003 Layer 3 - depends on 06 (dependency-tolerant on other features' seams) | Not Started |

Recorded on purpose, and deliberately NOT stories in this feature (feature.md's job is as much to say
what is NOT this feature as what is):

| Admin need | Already served by | Genuinely admin-only? |
|---|---|---|
| AI content vetting queue | `ai-content-factory/02` (#79) | no - not this feature |
| Library / pack management | `ai-content-factory/03` + `story-packs` | no - not this feature |
| Cost / abuse oversight dashboard | `platform-devops/04` App Insights + Cost Management + the `ai-cost-gate` breaker | no - do not rebuild |

Rebuilding cost dashboards or a second vetting queue here would be the smell. Build order: story 01
(auth boundary) is the foundation; story 03 (takedown) can follow immediately since public tales
already exist; story 02 (grant/revoke) lands alongside real charging, when a stuck paying customer is
actually possible. See `implementation.md` for the DAG-ready Wave Plan.

> **ADR 0003 Layer 3 (2026-07-08).** Stories 04-07 implement ADR 0003's "reshaped operator console":
> one bundle/one auth (04, closing the `billing-entitlements/07` interim-gate follow-up), a jobs-not-
> feature-tabs shell with scoped authz (05), the operator action log (06, ADR 0003 Decision 3 /
> Amendment 2), and the Support job's full lookup + verbs (07). See each story file and the Decisions
> entry below.

## Dependencies
- `billing-entitlements/01` (#70) - the grant store + full capability catalog that story 02
  (grant/revoke) writes to. The `IEntitlementService` interface + its `CreateRoom` capture already
  ship (ai-cost-gate/02 #121 / PR #132); still unbuilt is #70's stored-value side - the
  `EntitlementGrant` store and the catalog beyond `ai.onDemand` (see ADR 0002 "State of the tree").
- `accounts-identity/02` (#68) - the purchaser account that story 02 looks a customer up by.
- `keepsake-gallery` - the public tale link (already shipped) that story 03 moderates.
- `platform-devops/04` (#106, App Insights) + ADR 0001 (Cost Management budget) - the cost/abuse
  oversight this feature deliberately does NOT rebuild.
- child-safety - story 03's takedown honors the same authoritative filter posture; it does not
  reimplement moderation logic.
- `billing-entitlements/06` (#TBD, Complete "interim gate") - the `IActiveStripeContext` domain model
  and the interim `IOperatorGate` scheme story 04 deletes in favor of the real Operator policy.
- `control-plane/01` (ADR 0003 Layer 1, new feature, decomposed the same day) - the runtime settings
  endpoint story 05's Operations-tab settings panel is dependency-tolerant of.
- `accounts-identity/05` and `/09` (ADR 0003 Layer 0, decomposed the same day) - the `AccountId` spine and
  the family device link story 07's account detail panel surfaces, dependency-tolerant until they
  land.
- `keepsake-vault/01-04` (ADR 0003 Layer 2, new feature, decomposed the same day) - the claim code, vault
  counts, and soft-delete/restore seam story 07 consumes, dependency-tolerant until it lands.
- `billing-entitlements/08` (ADR 0003 Layer 2, decomposed the same day) - the grant metadata and
  per-account Stripe resync story 07 triggers, dependency-tolerant until it lands.

## Design notes
- **Still not a monolith - three thin stories, and a firm boundary on what is out.** Standing the
  back office up now (Decision B) does not mean absorbing everything: cost/abuse stays on App
  Insights + budget emails; content vetting stays the content-factory queue; refunds stay Stripe's
  dashboard. This feature is only the operator jobs no other feature owns.
- **AMENDED 2026-07-08 (ADR 0003 Layer 3): "not a monolith" now means jobs, not feature tabs, and
  scoped, not flat.** The console reorganizes around three operator JOBS - Support, Content,
  Operations (story 05) - rather than growing one tab per shipped story; admin endpoints carry a
  scope tag (`support`/`content`/`ops`, story 05) so a future moderator is an allowlist entry with a
  scope list, not a controller rewrite. The single-operator behavior is unchanged today - this is
  structure for later, not a role hierarchy shipped now (still explicitly Parked below).
- **Where it lives: a SEPARATE, auth-gated back office (option A, Decision B), never the kid PWA.**
  Its own bundle/route tree with its own auth - it handles PII-adjacent purchaser data and
  moderation actions and must never share a surface with the anonymous, kid-facing app (blast
  radius; kid-safety-by-construction, CLAUDE.md section 5). Option B (an in-app admin area) was
  rejected.
- **Operator login reuses the magic-link plumbing (Decision A), against a SEPARATE allowlist.**
  Story 01 issues an operator session with the same one-time-token issue/verify plumbing the
  purchaser magic-link uses (`accounts-identity/02`) - but admin authorization is membership in an
  operator allowlist held in config / Key Vault, resolved at verify time. `purchaser == admin` must
  be impossible: a signed-in purchaser reaching an admin endpoint is the bug to prevent. Admin
  endpoints check the operator scope, never mere sign-in.
- **Story 02 (grant/revoke by email) pairs with real charging.** The concrete need: unstick a paying
  customer whose entitlement did not apply, without hand-editing Table Storage. It writes an
  `EntitlementGrant` (the same lease-shaped row `billing-entitlements` defines - `validThrough` +
  `source`, ADR 0002 Decision C) keyed by purchaser identity. Thinnest option A: one or two protected
  endpoints + a minimal internal page reusing the MUI theme, not a full admin app.
- **Story 03 (public-tale moderation) = report -> auto-hide-after-N -> operator review (Decision E).**
  A "report this tale" control on public keepsake tales; reports accumulate, a tale auto-hides at a
  threshold N (a small config value), and the operator confirms or restores it from the back office.
  The threshold stops a single bad actor from unilaterally suppressing a tale; the auto-hide means no
  wait on always-on moderation. It reuses the authoritative child-safety posture, does not
  reimplement filtering, and is actionable now because public tales already shipped.
- **The anonymity invariant applies here too.** No admin surface may join a purchaser identity to a
  player nickname, room, or session. Purchaser support operates on the purchaser plane (email ->
  grant); moderation operates on published *content*, not on the anonymous author. Reviewers guard
  the same firewall ADR 0002 defines for `CreateRoom`.
- **Operator, not audit ceremony.** This is a toy, not a system of record (CLAUDE.md preamble): the
  admin surface is minimal operator convenience, not a compliance/audit console. Resist growing it
  into role hierarchies, audit trails, or dashboards that Azure already provides.
- **AMENDED 2026-07-08 (ADR 0003 Decision 3 / Amendment 2), REVISED 2026-07-08 (adversarial review),
  SHIPPED 2026-07-09 (sysadmin-console/06, #233): a narrow, deliberate exception now exists on the
  money/moderation/settings plane.** Story 06 SHIPPED a minimal append-only action log (operator,
  action, target, timestamp, optional note) for six actions today - grant, revoke, takedown
  confirm/restore, the Stripe mode flip, and (via `control-plane/01`, which already calls the same
  `IOperatorActionLog` seam) a settings override change - plus story 07's support verbs once they
  land. This is **dispute insurance, not compliance ceremony**: no immutability guarantee, no
  legal-hold - but it is built to actually SERVE as dispute insurance: the log row is written
  BEFORE the effectful action proceeds (log-before-act, never best-effort after - an append failure
  aborts the action rather than letting it commit with no trail), and retention is age-based with a
  HARD FLOOR (a compiled `MinRetentionDays`) no operator setting can lower below, so the log cannot be
  silently skipped by a failed append or evicted (by volume or by config) by the party a dispute
  concerns. The write side also validates any email-shaped target and the view side relies on React's
  default text escaping (never `dangerouslySetInnerHTML`). Gameplay and content stay exactly as
  ceremony-free as the bullet above still states; the console still does not grow role hierarchies or
  compliance dashboards - only this one narrow, dispute-insurance log exists, and only for
  money/moderation/settings-affecting operator actions.
- **REVISED 2026-07-08 (adversarial review): the Support surface's cross-plane firewall is structural,
  not a review discipline.** Story 07's account lookup resolves an email (or `AccountId`) only - a
  vault claim code and a public-tale slug are permanently removed as account-lookup inputs, and the
  vault/tale figure it shows is a bare count sourced from a contract that cannot return a byline or
  timestamp. Claim-code recovery is a player-facing capability `keepsake-vault` owns directly on the
  player's own device, never routed through an operator lookup. See story 07 and ADR 0003's "Security
  posture" section ("the support console cannot bridge the planes").

## Parked - later
- Multi-operator / role-based access (a human moderator distinct from the owner) - alpha has one
  operator; RBAC is a Phase 3+ concern if the team grows. **Story 05 (2026-07-08, ADR 0003) wires the
  SCOPE-CHECKING mechanism (`support`/`content`/`ops` as policy/attribute metadata) so a future
  restricted operator is a config entry, not a rework - but it does not ship any role-management UI
  or a restricted operator; that stays parked here.**
- A bespoke cost/spend dashboard - deferred indefinitely; App Insights + Cost Management cover it
  until they demonstrably do not. Story 05's Operations tab still only links out to it.
- Purchaser self-service beyond `billing-entitlements/05` (restore/manage) - the operator surface is
  for the cases self-service cannot handle, not a replacement for it.
- Any audit-trail / immutability ceremony - explicitly out (CLAUDE.md: toy, not a system of record).
  **Story 06 (2026-07-08, ADR 0003 Decision 3 / Amendment 2) is the one narrow, deliberate exception:
  a minimal append-only action log for money/moderation-affecting operator actions only, with no
  immutability guarantee and a pragmatic retention cap - see the amended Design note above. Gameplay
  and content stay exactly this ceremony-free; nothing broader is parked-and-reopened here.**

## Open decisions
The cross-cutting decisions A-F are all resolved in
[ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md)'s Decision section, and ADR 0003's
Decisions 1-5 (2026-07-08) are resolved too. The remaining open items are story-level details, not
blockers:
- **The auto-hide threshold N** (story 03) - the number of reports that hides a tale pending review.
  Pick a small starting value at build time and make it a config constant; tune from real signal.
- **The action-log retention cap** (story 06) - a pragmatic newest-N-rows or newest-M-months cap.
  Pick a small starting constant at build time; flag it as a `control-plane/03` knob-migration
  candidate for later tuning.

## Decisions
- 2026-07-03: Created as an exploration alongside ADR 0002. The owner then resolved ADR 0002 A-F the
  same day: Decision B greenlit a separate, auth-gated back office built now (not a deferred
  umbrella), Decision A set operator login on the reused magic-link plumbing (separate allowlist),
  and Decision E set public-tale moderation as report -> auto-hide-after-N -> operator review. The
  three candidate stories above were the result.
- 2026-07-03: Decomposed the same day into three full story files (`01-operator-login-and-admin-
  boundary.md`, `02-operator-grant-revoke-entitlement.md`, `03-report-and-takedown-public-tale.md`)
  plus `implementation.md` (reuse map + Wave Plan). Story 01 is the foundation wave; 02 and 03 land
  in the same wave since neither blocks the other technically - each depends only on its own
  external seam (#70's grant store for 02, nothing new beyond the already-shipped
  `keepsake-gallery/04` for 03). The `IEntitlementService` interface + its `CreateRoom` capture are
  already shipped (ai-cost-gate/02 #121 / PR #132); what stories 01 and 02 wait on is still unbuilt -
  `accounts-identity/02`'s magic-link + purchaser account (#68, no `api/src/Accounts/`) and
  `billing-entitlements/01`'s grant store + full catalog (#70). Each of 01 and 02 names a thin,
  contract-compatible fallback so this feature is not hard-blocked on either landing first (mirroring
  `ai-cost-gate/02`'s handling of the same seam).
- 2026-07-07: **All three stories shipped via PR #158** (issues #135/#136/#137 closed), with
  follow-up fixes #163/#164/#170/#171/#172. The seams the 2026-07-03 entries called unbuilt landed
  first (#68 via PR #147, #70 via PR #152), so the built stories use the real magic-link plumbing
  and the real grant store - no fallback stand-ins. The one open follow-up: relocate the
  billing-mode toggle (`billing-entitlements/07`) from the kid bundle's `/admin/billing-mode` route
  into the operator console behind the Operator scheme.
- 2026-07-08: **ADR 0003 accepted** (owner decisions 1-5); this feature is named as `sysadmin-console`
  Layer 3 of that ADR's admin platform, decomposed into four new stories (`04-07`). 04 (one console,
  one auth) finally closes the 2026-07-07 follow-up above - the interim `IOperatorGate` shared-secret
  scheme and the kid-bundle `/admin/billing-mode` route are deleted, and `StripeModeController` moves
  behind the real Operator policy. 05 reorganizes the console shell from two feature-tabs into three
  operator jobs (Support / Content / Operations) and adds scope-tagged authorization (`support` /
  `content` / `ops`), unchanged in behavior for today's single operator. 06 adds the minimal
  append-only operator action log ADR 0003 Decision 3 / Amendment 2 calls for - the narrow, explicit
  exception to this feature's own "no audit ceremony" stance, amended (not reversed) in Design notes
  above. 07 builds out the Support job into a full lookup (email / vault claim code / tale slug) plus
  five verbs, written dependency-tolerant against three other features' still-unbuilt Layer 0/2 seams
  (`accounts-identity/05` and `/09`, the new `keepsake-vault` feature, `billing-entitlements/08`) so it
  is not hard-blocked on any of them landing first. Wave order (04 independent, 05 depends on 04, 06
  depends on 05 for its view only, 07 depends on 06): see `implementation.md`'s extended Wave Plan.
