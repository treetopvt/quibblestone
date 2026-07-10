# Story: Support lookup + verbs

**Feature:** Sys-Admin Console  ·  **Status:** Complete (2026-07-09)  ·  **Issue:** #243

> **Shipped note (2026-07-09):** merged as PR #245 on owner sign-off (flagged cross-plane seam).
> Two AC sections ship behind their dependency-tolerant seams and degrade honestly: AC-02's
> vault/tale COUNT renders "not available yet" via the `UnavailableVaultAccountSummary` sentinel
> until keepsake-vault exposes an account -> count projection (a real `IVaultAccountSummary.
> CountForAccountAsync` -> swap the one DI line, no controller change); subscription "last webhook
> event" is likewise not surfaced (no store for it yet). AC-05 restore + AC-07 resync are live over
> the merged keepsake-vault/04 and billing-entitlements/08 seams. The AC-08 firewall is structural
> (verified by reflection + source-scan tests), not asserted.

## Context
This is the Support job's real payload - ADR 0003 Layer 3's "find a person, fix their problem,"
built out from the plain grant/revoke screen story 05 relocated into the Support tab. An operator
needs to find an ACCOUNT by whatever the person in front of them (on the phone, in an email) can
actually give them, and see its account-plane picture in one place: the account itself, its
entitlement grants, its subscription state, aggregate vault/tale counts, and a linked-device count.
From there, five verbs cover the concrete support scenarios ADR 0003's audit named: a lost magic
link, an expiring shared tale, an accidentally-deleted keepsake, a stuck entitlement, and a
subscription that looks out of sync with Stripe. Every verb writes one row to the action log (story
06) - this is exactly the kind of disputable, money/moderation-adjacent action that log exists for.
Several of the data points and verbs this story surfaces depend on OTHER features' Layer 0/2 work
still being built in parallel (`accounts-identity/05`'s `AccountId`, `keepsake-vault/03-04`'s
soft-delete/restore, `billing-entitlements/08`'s grant metadata and resync) - this story is written
to be **dependency-tolerant**: it can start the moment story 06 lands, and each panel/verb lights up
on its own as its backing seam arrives, rather than the whole story waiting on the slowest one.

**Revised 2026-07-08 (adversarial review) - the cross-plane firewall is now structural, not
asserted.** The prior draft's AC-01 offered lookup by "purchaser email, vault claim code, or
public-tale slug," all resolving to "the matching account" - the review found this is EXACTLY the
bridge ADR 0003's firewall forbids: resolving a vault claim code or a public-tale slug back to an
account email, in the same controller that also has a handle on byline/timestamp-bearing content
stores (`IVaultStore.ListAsync` returns bylines; `PublishedTale` carries a byline), is a one-line
regression away from projecting who-authored-what to an operator. The fix below is not "be careful
in review" - it removes the capability from this controller's shape entirely: claim-code recovery
becomes a device-held, player-facing capability owned by `keepsake-vault` itself (never routed
through an operator lookup), and a tale slug is acted on directly as CONTENT (extend its TTL,
restore it) without ever resolving to, or displaying, an owning account or its byline. The
non-negotiable invariant, unchanged from ADR 0002/0003: this surface NEVER joins an account to a
player nickname, room, or session, and now ALSO never joins a piece of published content back to the
account that produced it. See [feature.md](./feature.md) and
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) Layer 3 and its "Security posture"
section ("the support console cannot bridge the planes").

## Acceptance Criteria
- [x] AC-01 (lookup - REVISED 2026-07-08, email/AccountId ONLY): Given a signed-in operator on the
      Support tab, when they search by purchaser email (or, once `accounts-identity/05` lands, an
      `AccountId`), then the matching account resolves to a summary (`AccountId`, created-at, email)
      - or a clear "no match" state. A vault claim code and a public-tale slug are NOT valid search
      inputs on this screen and never resolve to an account, on this surface or any extension of it
      (see AC-08). The search never requires or displays a player nickname, room code, session id,
      tale byline, or tale timestamp.
- [x] AC-02: Given a resolved account, when its detail panel loads, then it shows: current
      entitlement grants (capability key, source, `validThrough` - the SAME data story 02's
      `AdminEntitlementsController` already returns), subscription state (plan, status,
      `validThrough`, Stripe subscription id, last webhook event - once `billing-entitlements/08`
      exists), an aggregate vault/tale COUNT (a single integer, sourced from a count-only projection
      - once `keepsake-vault` exposes one; see Technical Notes), and a linked-devices count (once
      `accounts-identity/09`'s family device link exists) - each of the latter three sections is
      DEPENDENCY-TOLERANT: it renders its real data when its backing seam responds, and a plain
      "not available yet" placeholder otherwise, never an error or a blank detail panel. No section
      of this panel, present or future, ever renders a tale's byline, a tale's timestamp, or a list
      of the account's individual tales/vaults - counts only.
- [x] AC-03 (resend magic link - REVISED 2026-07-08, shares the public throttle): Given a resolved
      account, when the operator clicks "resend magic link," then a fresh magic-link email is issued
      through the SAME `accounts-identity/04` email-sending seam the purchaser sign-in flow already
      uses (no new email-delivery path), the action is written to the action log (story 06) with the
      account's email as the target, AND the send is bounded on TWO axes, not zero: (a) it is subject
      to the SAME rate-limit policy the public `POST /api/accounts/signin/request` endpoint already
      uses (`SignInRateLimit`, `[EnableRateLimiting]`) rather than calling `IEmailSender` directly and
      bypassing it, and (b) it is ADDITIONALLY capped per TARGET account per window (e.g. no more
      than a handful of resends to the same account within a rolling window), independent of which
      operator IP triggers it - closing the review-flagged email-bomb vector where an operator
      surface calling the sender directly, unthrottled, could flood one inbox regardless of the
      public endpoint's own IP-based limiter.
- [x] AC-04: Given a public tale's slug, when the operator extends its TTL, then the tale's expiry is
      pushed out through the existing `IPublishedTaleStore` write path (`keepsake-gallery/04`, no
      parallel store), the response/UI includes ONLY the slug and the new expiry (never the tale's
      byline or content), and the action is logged with the slug as the target.
- [x] AC-05 (restore, with asymmetric friction - REVISED 2026-07-08): Given a user's OWN
      accidentally-deleted keepsake within its recovery window (once `keepsake-vault/04`'s
      self-delete/restore seam exists), when the operator restores it from the Support tab, then it
      resumes normal serving through that feature's restore path with a single, light confirmation,
      and the action is logged; until `keepsake-vault/04` ships, this verb's control is visibly
      disabled with a "not available yet" state rather than failing or 500-ing. This is a DISTINCT,
      lower-friction verb from the Content tab's existing MODERATION-takedown restore
      (`ReportedTalesController.Restore`, story 03, already shipped) - restoring a moderation
      takedown (overriding a decision made for content-safety reasons) must carry stronger
      confirmation (e.g. a required reason/note) than restoring a user's own accidental delete
      (a courtesy action with no safety implication). This story does not merge the two into one
      generic "restore" control, on the Support tab or anywhere else - they remain two distinct
      verbs behind two distinct friction levels, pairing with `keepsake-vault/04`.
- [x] AC-06: Given a purchaser needing a comp or an entitlement extension, when the operator grants
      or extends a capability from this screen, then it reuses story 02's EXACT grant plumbing
      (`AdminEntitlementsController` / `IEntitlementGrantStore`, `source = Operator`) - no second
      write path - and the action is logged.
- [x] AC-07 (resync, rate-limited/debounced per account - REVISED 2026-07-08): Given a purchaser
      whose subscription state looks out of sync with Stripe (once `billing-entitlements/08`'s
      per-account resync service exists), when the operator triggers a resync for that account, then
      the resync service is invoked at most once per account within a minimum interval (rate-limited
      or debounced per account, e.g. mirroring AC-03's per-target-account pattern) so repeated clicks
      or repeated tickets cannot hammer the Stripe API for one account, and the action is logged;
      until that service ships, this verb's control is visibly disabled with a "not available yet"
      state. `billing-entitlements/08` owns resync's mode-safety (Test/Live isolation on the grant
      store side); this verb owns not spamming the call.
- [x] AC-08 (the firewall, structural - REWRITTEN 2026-07-08 from an assertion into a guarantee):
      Given this controller's shape, then it is STRUCTURALLY incapable of bridging the play/account
      plane to the content plane, not merely reviewed for it:
      - It does NOT resolve a public-tale slug or a vault claim code to an account email, on this
        surface or any extension of it - those identifiers are handled entirely elsewhere (a claim
        code is redeemed by the PLAYER'S OWN DEVICE against `keepsake-vault`'s own recovery endpoint,
        never looked up by an operator to find an account; a tale slug is acted on directly as
        content via AC-04/AC-05, never resolved back to an owning account).
      - It does NOT project a byline nickname, a tale timestamp, or any room/session content, for a
        resolved account or otherwise - the constructor holds no injected dependency whose return
        type CAN carry those fields (see Technical Notes' "narrow the contract, don't just avoid
        calling the wide one"); AC-02's vault/tale figure is a bare count, sourced from a method that
        returns an integer and nothing else.
      - Its account-existence lookup (AC-01) is, unavoidably, a mild "does an account exist for this
        email" oracle - this is ACCEPTABLE because it sits entirely behind operator authentication
        (never public, never reachable by a player), but that acceptance does not extend one inch
        further: the oracle answers "does an account exist," never "does this piece of content
        belong to an account," which is the direction ADR 0003 forbids.
      - Every verb (present or future) writes one row to the action log (story 06); the log itself
        also carries account-plane facts only (email + action-specific target), never content-plane
        fields.

## Out of Scope
- Vault-claim-code resolution to an account, in any form - this is now permanently out of scope for
  this controller (not merely "not yet built"), per AC-01/AC-08. Recovering a vault by claim code is
  a player-facing capability `keepsake-vault` owns directly on the player's own device; the operator
  console never accepts a claim code as a search input.
- Public-tale-slug resolution to an account or its byline, in any form - permanently out of scope,
  per AC-01/AC-08. A slug is a valid identifier ONLY for the direct content-plane verbs in AC-04/AC-05
  (extend TTL, restore), which act on the tale record without ever exposing who authored it.
- Building any of the seams this story consumes (`accounts-identity/05`'s `AccountId` spine,
  `keepsake-vault/01-04`'s count-only projection and soft-delete/restore, `billing-entitlements/08`'s
  grant metadata + resync, `accounts-identity/09`'s family device link) - this story is a THIN
  consumer of each, built dependency-tolerant where a seam has not landed yet, never a parallel
  implementation of any of them.
- Bulk operations, CSV export, or acting on more than one account per verb invocation - one account,
  looked up by email/AccountId, per action, mirroring story 02's existing constraint.
- Any per-kid-profile or per-player data on this screen - the kid-profile boundary (ADR 0003
  Decision 1) means there is no per-kid anything to show; the vault and its counts are FAMILY-level,
  never per-profile.
- Merging the Content tab's moderation-takedown restore (story 03) and this story's self-delete
  restore (AC-05) into one generic "restore" control - they stay two distinct verbs with two distinct
  friction levels (AC-05).
- Role-based access among multiple operators - Parked in feature.md; this screen's authorization is
  story 05's `support` scope, still granted to every allowlisted operator today.
- Any change to `PurchaserEntitlements.tsx`'s existing behavior beyond extending the Support tab it
  now shares a home with - the existing grant/revoke screen (story 02) keeps working exactly as
  before; this story adds the narrower lookup + additional verbs alongside it (or folds it in, the
  builder's call, so long as story 02's ACs remain intact).

## Technical Notes
- **New composing endpoint:** a small `AccountSupportController` (or similarly named) in
  `api/src/Admin/`, behind `[Authorize(Policy = OperatorSession.PolicyName)]` plus story 05's
  `support` scope requirement. It ORCHESTRATES existing/soon-to-exist seams - it does not
  reimplement any of them:
  - `IAccountStore` (accounts-identity) for the email/AccountId lookup ONLY - extended by
    `accounts-identity/05` to expose `AccountId`; until then, fall back to the email-keyed lookup
    story 02 already uses (AccountId simply absent from the summary). No claim-code or slug
    resolution path exists here at all (AC-01/AC-08 - not a dependency-tolerant stub, a permanent
    absence).
  - `IEntitlementGrantStore` (billing-entitlements/01) for grants - same store story 02 already
    reads/writes; reuse `AdminEntitlementsController`'s `BuildLookupAsync`-shaped projection rather
    than re-deriving it.
  - A subscription-state read and a resync trigger (billing-entitlements/08, not yet built) - guard
    the call behind a capability check (e.g. `if (_resync is not null)` with a null-object /
    feature-flagged registration when the service is absent) so the endpoint responds gracefully
    rather than throwing when the seam is missing; the resync trigger is additionally
    rate-limited/debounced per account (AC-07) regardless of whether the seam is present.
  - **Narrow the contract, don't just avoid calling the wide one (AC-08's "one added projection line"
    fix):** for the vault/tale count (AC-02), do NOT inject `keepsake-vault`'s `IVaultStore` (whose
    `ListAsync` returns per-tale bylines) into this controller at all, even if only its `.Count` is
    read today - request/consume a NARROWER, count-only contract instead (e.g. an
    `IVaultAccountSummary.CountForAccountAsync(AccountId) -> int`), so the controller's constructor
    never HOLDS a reference capable of returning a byline in the first place. A future maintainer
    adding "just one more field" to a byline-returning object the controller already has cannot leak
    what the controller structurally cannot reach.
  - A linked-device count (accounts-identity/09's family device link, not yet built) - same
    graceful-absence guard; a count only, never a device identifier or name.
  - `IOperatorActionLog` (story 06) for every verb (AC-03 through AC-07) - the SAME seam story 06
    wires into stories 02/03/04, imported here rather than re-derived; this controller's calls follow
    story 06's log-before-act ordering (AC-01a there) exactly as the other five call sites do.
- **The resend/resync per-account throttle (AC-03/AC-07):** reuse `SignInRateLimit`'s existing
  per-IP `[EnableRateLimiting(SignInRateLimit.PolicyName)]` policy on the resend action so it shares
  the exact mechanism the public endpoint uses, AND add a second, independent partition keyed on the
  TARGET account's normalized email (not the caller's IP) - either a sibling named policy (e.g.
  `SupportResendRateLimit`, mirroring `SignInRateLimit`'s shape but partitioned on the request's
  target-email parameter instead of `RemoteIpAddress`) or an equivalent in-code check against a small
  per-account counter. The risk this axis closes is specific to the admin surface: a single operator
  session (one IP) resending repeatedly to the SAME account, which a purely IP-based limiter does not
  bound. Apply the identical two-axis shape to the resync trigger (AC-07): caller-side bounded by the
  controller's normal rate limits, target-account-side bounded by its own per-account window.
- **Dependency-tolerant implementation shape:** prefer constructor injection of OPTIONAL seams
  (nullable service references, or a small "not configured" sentinel implementation registered when
  the real one is absent - mirroring `ReportedTalesController`'s own note that "the disabled fallback
  returns an empty queue locally, so the console runs with the feature simply off") over a hard
  dependency that would make this whole story unbuildable until four other features finish. Each
  panel/verb on the web side checks its own data/availability independently (a `subscriptionState:
  'available' | 'unavailable' | 'loading'` per section, matching story 05's settings-panel pattern)
  rather than gating the whole page on every seam being present.
- **Web:** extend the Support tab (story 05's `web/src/admin/main.tsx` / a dedicated
  `SupportLookup.tsx`) with a search box accepting an email (or `AccountId` once available) - NOT a
  claim code or a slug - a detail panel with the sections from AC-02, and verb buttons that are
  individually enabled/disabled based on whether their backing endpoint reports itself available -
  reuse `purchasersClient.ts`'s bearer-credential-aware fetch pattern for the new client module (e.g.
  `supportClient.ts`). AC-04/AC-05's tale-targeted verbs (extend TTL, restore) take a slug as a
  DIRECT input to their own action, not as a search key into the account lookup - the UI should not
  imply a slug "finds" an account.
- **Files this story owns:** `api/src/Admin/AccountSupportController.cs` (new),
  `web/src/admin/SupportLookup.tsx` (new or extends the Support tab's existing content), `web/src/
  admin/supportClient.ts` (new). It reads from, but does not modify the internals of,
  `AdminEntitlementsController`'s existing grant/revoke logic (AC-06 reuses it, does not fork it).

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: lookup by email (and by AccountId once available) resolves to the account shape; an unknown email returns a clear not-found state; a request containing a claim-code-shaped or slug-shaped value in the search field is rejected/ignored as a search key, never resolved to an account.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: with a seam present, its section renders real data (the vault/tale section renders ONLY a count, never a per-tale list); with a seam absent (null/sentinel service), its section reports "unavailable" rather than throwing.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: resend-magic-link calls the existing IEmailSender/magic-link seam via the SignInRateLimit-decorated path, writes one action-log row, and a burst of resends to the SAME account within the window is rejected past its cap even from a single operator/IP.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: extend-TTL pushes IPublishedTaleStore's expiry out, the response contains only slug + new expiry (no byline field), and writes one action-log row.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs (once keepsake-vault/04 lands): self-delete restore resumes serving with a single confirmation and logs; before it lands, the control reports itself disabled. manual: confirm the Content tab's moderation-takedown restore (story 03) requires strictly MORE friction (a required reason/note) than this verb's single confirmation.` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs: comp/extend writes through IEntitlementGrantStore identically to story 02's Grant, and logs.` |
| AC-07 | `tests/QuibbleStone.Api.Tests/Admin/AccountSupportControllerTests.cs (once billing-entitlements/08 lands): resync invokes the service once per account per window and logs; a second resync request for the same account within the window is rejected/debounced; before the service lands, the control reports itself disabled.` |
| AC-08 | `manual + static: code review of AccountSupportController.cs and SupportLookup.tsx - confirm no import from api/src/Rooms or the hubs, no injected dependency whose return type carries a byline/timestamp (grep for IVaultStore.ListAsync or PublishedTale.Byline in this controller - zero hits), and no field beyond account/content-plane data anywhere in the response or the UI.` |

## Dependencies
- `sysadmin-console/06` (#233, Complete) - the action log every verb writes to; this story can start the
  moment 06 lands.
- `sysadmin-console/01`'s `SignInRateLimit`-equivalent pattern (`accounts-identity/03`, shipped) -
  the per-IP policy AC-03's resend verb reuses.
- `accounts-identity/05` (ADR 0003 Layer 0, not yet decomposed) - the `AccountId` spine AC-01/AC-02
  surface; dependency-tolerant until it lands (email-keyed lookup works today).
- `keepsake-vault/01-04` (ADR 0003 Layer 2, new feature, not yet decomposed) - the count-only vault
  projection AC-02 needs and the self-delete/restore seam AC-05 needs; dependency-tolerant until they
  land. This story explicitly does NOT depend on, or consume, any claim-code lookup capability from
  this feature (AC-01/AC-08 removed that direction entirely).
- `billing-entitlements/08` (ADR 0003 Layer 2, not yet decomposed) - the subscription-state read and
  per-account resync AC-02/AC-07 need; dependency-tolerant until it lands.
- `sysadmin-console/02` (#136, Complete) - the grant/revoke plumbing AC-06 reuses.
- `sysadmin-console/03` (#137, Complete) - the moderation-takedown restore this story's AC-05
  deliberately keeps distinct from (stronger friction, never merged).
- `keepsake-gallery/04` (#66, Complete) - the `IPublishedTaleStore` write path AC-04 extends.
