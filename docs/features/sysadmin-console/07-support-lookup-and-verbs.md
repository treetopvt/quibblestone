# Story: Support lookup + verbs

**Feature:** Sys-Admin Console  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
This is the Support job's real payload - ADR 0003 Layer 3's "find a person, fix their problem,"
built out from the plain grant/revoke screen story 05 relocated into the Support tab. An operator
needs to find an account by whatever the person in front of them (on the phone, in an email) can
actually give them - a purchaser email, a keepsake-vault claim code, or a shared tale's slug - and
see the WHOLE picture in one place: the account itself, its entitlement grants, its subscription
state, its vault/tale counts, and its linked-device count. From there, five verbs cover the concrete
support scenarios ADR 0003's audit named: a lost magic link, an expiring shared tale, an
accidentally-deleted keepsake, a stuck entitlement, and a subscription that looks out of sync with
Stripe. Every verb writes one row to the action log (story 06) - this is exactly the kind of
disputable, money/moderation-adjacent action that log exists for. Several of the data points and
verbs this story surfaces depend on OTHER features' Layer 0/2 work that is still being built in
parallel (`accounts-identity/05`'s `AccountId`, `keepsake-vault/03-04`'s claim codes and soft-delete,
`billing-entitlements/08`'s grant metadata and resync) - this story is written to be
**dependency-tolerant**: it can start the moment story 06 lands, and each panel/verb lights up
on its own as its backing seam arrives, rather than the whole story waiting on the slowest one. The
non-negotiable invariant, unchanged from ADR 0002/0003: this surface NEVER joins an account to a
player nickname, room, or session - it operates entirely on the account/content plane. See
[feature.md](./feature.md) and [ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)
Layer 3.

## Acceptance Criteria
- [ ] AC-01: Given a signed-in operator on the Support tab, when they search by purchaser email,
      vault claim code, or public-tale slug, then the matching account resolves to a summary
      (`AccountId`, created-at, email) - or a clear "no match" state - regardless of which of the
      three identifiers was used to find it; the search never requires or displays a player
      nickname, room code, or session id.
- [ ] AC-02: Given a resolved account, when its detail panel loads, then it shows: current
      entitlement grants (capability key, source, `validThrough` - the SAME data story 02's
      `AdminEntitlementsController` already returns), subscription state (plan, status,
      `validThrough`, Stripe subscription id, last webhook event - once `billing-entitlements/08`
      exists), vault/tale counts (once `keepsake-vault` exists), and a linked-devices count (once
      `accounts-identity/09`'s family device link exists) - each of the latter three sections is
      DEPENDENCY-TOLERANT: it renders its real data when its backing seam responds, and a plain
      "not available yet" placeholder otherwise, never an error or a blank detail panel.
- [ ] AC-03: Given a resolved account, when the operator clicks "resend magic link," then a fresh
      magic-link email is issued through the SAME `accounts-identity/04` email-sending seam the
      purchaser sign-in flow already uses (no new email-delivery path), and the action is written
      to the action log (story 06) with the account's email as the target.
- [ ] AC-04: Given a public tale's slug, when the operator extends its TTL, then the tale's expiry
      is pushed out through the existing `IPublishedTaleStore` write path (`keepsake-gallery/04`,
      no parallel store) and the action is logged with the slug as the target.
- [ ] AC-05: Given a soft-deleted tale within its recovery window (once `keepsake-vault/04`'s
      soft-delete/restore seam exists), when the operator restores it, then it resumes normal
      serving through that feature's restore path and the action is logged; until
      `keepsake-vault/04` ships, this verb's control is visibly disabled with a "not available yet"
      state rather than failing or 500-ing.
- [ ] AC-06: Given a purchaser needing a comp or an entitlement extension, when the operator grants
      or extends a capability from this screen, then it reuses story 02's EXACT grant plumbing
      (`AdminEntitlementsController` / `IEntitlementGrantStore`, `source = Operator`) - no second
      write path - and the action is logged.
- [ ] AC-07: Given a purchaser whose subscription state looks out of sync with Stripe (once
      `billing-entitlements/08`'s per-account resync service exists), when the operator triggers a
      resync for that account, then the resync service is invoked and the action is logged; until
      that service ships, this verb's control is visibly disabled with a "not available yet" state.
- [ ] AC-08 (the invariant, non-negotiable): Given any Support lookup or verb on this screen -
      present or future - then it operates SOLELY on the account/content plane (email, `AccountId`,
      claim code, tale slug, capability keys) and never joins to, displays, or offers navigation
      toward a player nickname, room code, or session id, on this surface or any extension of it -
      the same firewall ADR 0002 defines for `CreateRoom`, reviewed here exactly as fiercely.

## Out of Scope
- Building any of the seams this story consumes (`accounts-identity/05`'s `AccountId` spine,
  `keepsake-vault/01-04`, `billing-entitlements/08`'s grant metadata + resync,
  `accounts-identity/09`'s family device link) - this story is a THIN consumer of each, built
  dependency-tolerant where a seam has not landed yet, never a parallel implementation of any of
  them.
- Bulk operations, CSV export, or acting on more than one account per verb invocation - one account,
  looked up one way, per action, mirroring story 02's existing constraint.
- Any per-kid-profile or per-player data on this screen - the kid-profile boundary (ADR 0003
  Decision 1) means there is no per-kid anything to show; the vault and its counts are FAMILY-level,
  never per-profile.
- Role-based access among multiple operators - Parked in feature.md; this screen's authorization is
  story 05's `support` scope, still granted to every allowlisted operator today.
- Any change to `PurchaserEntitlements.tsx`'s existing behavior beyond extending the Support tab it
  now shares a home with - the existing grant/revoke screen (story 02) keeps working exactly as
  before; this story adds the broader lookup + additional verbs alongside it (or folds it in, the
  builder's call, so long as story 02's ACs remain intact).

## Technical Notes
- **New composing endpoint:** a small `AccountSupportController` (or similarly named) in
  `api/src/Admin/`, behind `[Authorize(Policy = OperatorSession.PolicyName)]` plus story 05's
  `support` scope requirement. It ORCHESTRATES existing/soon-to-exist seams - it does not
  reimplement any of them:
  - `IAccountStore` (accounts-identity) for the email/AccountId lookup - extended by
    `accounts-identity/05` to expose `AccountId`; until then, fall back to the email-keyed lookup
    story 02 already uses (AccountId simply absent from the summary).
  - A vault-claim-code lookup and a tale-slug-to-account lookup - both new, small resolution paths
    this story adds ONLY if the underlying stores (`keepsake-vault`, `keepsake-gallery/04`) already
    expose "who does this claim code / slug belong to"; if not yet exposed, this story's search
    degrades to email-only until that lookup lands (a dependency-tolerant search, matching AC-02's
    posture for the detail panel).
  - `IEntitlementGrantStore` (billing-entitlements/01) for grants - same store story 02 already
    reads/writes; reuse `AdminEntitlementsController`'s `BuildLookupAsync`-shaped projection rather
    than re-deriving it.
  - A subscription-state read and a resync trigger (billing-entitlements/08, not yet built) - guard
    the call behind a capability check (e.g. `if (_resync is not null)` with a null-object /
    feature-flagged registration when the service is absent) so the endpoint responds gracefully
    rather than throwing when the seam is missing.
  - Vault/tale counts (keepsake-vault, not yet built) - same graceful-absence guard.
  - A linked-device count (accounts-identity/09's family device link, not yet built) - same
    graceful-absence guard.
  - `IOperatorActionLog` (story 06) for every verb (AC-03 through AC-07) - the SAME seam story 06
    wires into stories 02/03/04, imported here rather than re-derived.
- **Dependency-tolerant implementation shape:** prefer constructor injection of OPTIONAL
  seams (nullable service references, or a small "not configured" sentinel implementation
  registered when the real one is absent - mirroring `ReportedTalesController`'s own note that "the
  disabled fallback returns an empty queue locally, so the console runs with the feature simply
  off") over a hard dependency that would make this whole story unbuildable until four other
  features finish. Each panel/verb on the web side checks its own data/availability independently
  (a `subscriptionState: 'available' | 'unavailable' | 'loading'` per section, matching story 05's
  settings-panel pattern) rather than gating the whole page on every seam being present.
- **Web:** extend the Support tab (story 05's `web/src/admin/main.tsx` / a dedicated
  `SupportLookup.tsx`) with a single search box accepting email, claim code, or slug (the server
  disambiguates by shape/format), a detail panel with the sections from AC-02, and verb buttons that
  are individually enabled/disabled based on whether their backing endpoint reports itself
  available - reuse `purchasersClient.ts`'s bearer-credential-aware fetch pattern for the new
  client module (e.g. `supportClient.ts`).
- **Files this story owns:** `api/src/Admin/AccountSupportController.cs` (new),
  `web/src/admin/SupportLookup.tsx` (new or extends the Support tab's existing content), `web/src/
  admin/supportClient.ts` (new). It reads from, but does not modify the internals of,
  `AdminEntitlementsController`'s existing grant/revoke logic (AC-06 reuses it, does not fork it).

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: lookup by email, by claim code (once available), and by slug (once available) all resolve to the same account shape; an unknown identifier of any kind returns a clear not-found state.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: with a seam present, its section renders real data; with a seam absent (null/sentinel service), its section reports "unavailable" rather than throwing.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: resend-magic-link calls the existing IEmailSender/magic-link seam and writes one action-log row.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: extend-TTL pushes IPublishedTaleStore's expiry out and writes one action-log row.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs (once keepsake-vault/04 lands): restore resumes serving and logs; before it lands, the control reports itself disabled.` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: comp/extend writes through IEntitlementGrantStore identically to story 02's Grant, and logs.` |
| AC-07 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs (once billing-entitlements/08 lands): resync invokes the service and logs; before it lands, the control reports itself disabled.` |
| AC-08 | `manual: code review of AccountSupportController.cs and SupportLookup.tsx - confirm no import from api/src/Rooms or the hubs, and no field beyond account/content-plane data anywhere in the response or the UI.` |

## Dependencies
- `sysadmin-console/06` (#TBD) - the action log every verb writes to; this story can start the
  moment 06 lands.
- `accounts-identity/05` (ADR 0003 Layer 0, not yet decomposed) - the `AccountId` spine AC-01/AC-02
  surface; dependency-tolerant until it lands (email-keyed lookup works today).
- `keepsake-vault/03` and `/04` (ADR 0003 Layer 2, new feature, not yet decomposed) - the claim-code
  lookup and soft-delete/restore seam AC-01/AC-05 need; dependency-tolerant until they land.
- `billing-entitlements/08` (ADR 0003 Layer 2, not yet decomposed) - the subscription-state read and
  per-account resync AC-02/AC-07 need; dependency-tolerant until it lands.
- `sysadmin-console/02` (#136, Complete) - the grant/revoke plumbing AC-06 reuses.
- `keepsake-gallery/04` (#66, Complete) - the `IPublishedTaleStore` write path AC-04 extends.
