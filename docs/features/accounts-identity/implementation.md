<!--
  Implementation plan for the accounts-identity feature. Bridges feature.md + stories to orchestration.
  Refreshed 2026-07-03 against shipped reality: ADR 0002 Decision A resolved the identity provider to
  magic-link email (no OAuth), and ai-cost-gate/02 (#121, PR #132) already ships the entitlement-capture
  seam (Room.Entitlements) story 01 used to have to NAME as a placeholder.
  Refreshed again 2026-07-08 for ADR 0003 Layer 0 (stories 05-09): the AccountId spine, ADR 0002
  Decision F finally wired, the free family account, kid seat presets, and the family device link
  (with its kid-device flag). Refreshed AGAIN 2026-07-08 to fold in the adversarial-review
  resolutions (finding #1's redesigned teen-plus gate, finding #2's structural identity-discard
  requirement for story 06's per-connection singleton, and the corrected canonical ADR wave numbers
  and Entitlements hazard chain) - see the Decisions entry at the bottom of this file. Use
  hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Accounts & Identity

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Room / player model (do NOT modify to add accounts) | the existing anonymous room record | `api/src/Rooms/Room.cs`, `api/src/Rooms/RoomRegistry.cs` |
| Entitlement capture (already shipped - point at it, do not re-name a placeholder) | `Room.Entitlements` / `Room.CaptureEntitlements` (a captured `SessionEntitlements` capability set, never a purchaser id - ai-cost-gate/02, #121, PR #132) | `api/src/Rooms/Room.cs`, `api/src/Entitlements/IEntitlementService.cs` |
| Service registration pattern (singleton DI) | the existing `RoomRegistry` / `IContentSafetyFilter` registrations | `api/src/Program.cs` |
| Child safety (nickname filtering, unaffected by accounts) | the single server-side safety filter | `api/src/Safety/IContentSafetyFilter.cs`, `ContentSafetyFilter.cs` |
| Styling / theme tokens | the MUI theme (palette, typography, radii, spacing) | `web/src/theme.ts` |
| Shared UI contracts | the single AppBar + Button family | `web/src/components/AppBar.tsx`, `web/src/components/index.ts` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Config | `import.meta.env` (`VITE_*`) - never a secret | `web/src/vite-env.d.ts`, `web/.env.development` |
| Secrets (magic-link token-signing key, later an email-delivery provider key) | Azure Key Vault | `infra/main.bicep` (`keyVault` resource) |
| Durable storage for the new `Account` record | Azure Table Storage | `infra/main.bicep` (`storage` resource) |

New surfaces this feature introduces (not yet reuse targets, become them once built):
- `api/src/Accounts/Account.cs`, `IAccountStore.cs`, `AccountStore.cs`, plus the magic-link one-time-token
  issuer/verifier (story 02) - the account record + store + token plumbing, consumed by story 03's sign-in
  endpoint, by billing-entitlements/01's (#70) purchaser-lookup, and by `sysadmin-console/01`'s operator login
  (which reuses the SAME token issuer/verifier against a separate allowlist).
- `web/src/pages/Account.tsx` (or similar; naming TBD at build time) - the purchaser-only sign-in/restore entry
  point (story 03), reachable only from a settings-style affordance, never from the kid play-flow.

### ADR 0003 Layer 0 additions (stories 05-09) - reuse map extension

| Concern | Reuse | Where |
|---|---|---|
| The account record + store (re-keyed, not replaced) | `Account` / `IAccountStore` (story 05 adds `AccountId`; do not build a second account type) | `api/src/Accounts/Account.cs`, `IAccountStore.cs`, `TableStorageAccountStore.cs`, `InMemoryAccountStore.cs` |
| Email normalization / hashing (now an INDEX key, not a primary key) | `AccountIdentity.Normalize` / `.KeyFor` | `api/src/Accounts/AccountIdentity.cs` |
| Magic-link issue/verify (story 07 reuses for sign-up, never a second implementation) | `IMagicLinkTokenService` / `MagicLinkTokenService` | `api/src/Accounts/IMagicLinkTokenService.cs`, `MagicLinkTokenService.cs` |
| Email copy selection (story 07 adds a `FamilySignUp` purpose) | `MagicLinkPurpose` enum + `IEmailSender` | `api/src/Accounts/IEmailSender.cs`, `AcsEmailSender.cs`, `NoOpEmailSender.cs` |
| The purchaser session credential (story 06 supplies it as the hub `accessTokenFactory` value; story 09 mirrors its resolve-and-discard shape for the device token) | `PurchaserCredentialService` | `api/src/Accounts/PurchaserCredentialService.cs` |
| The per-connection resolved-identity singleton (story 06 introduces it - cold-builder-critical: SignalR builds a fresh `GameHub` per invocation, so this CANNOT be a hub field, it must be a `Program.cs`-registered singleton; story 09 extends its `AdultUnlocked` computation, never forks a second store) | a new `IConnectionEntitlementStore` / `ConnectionEntitlementStore` (name TBD at build time) | new: `api/src/Accounts/ConnectionEntitlementStore.cs`; registered in `api/src/Program.cs` |
| The entitlement seam (unchanged contract; story 05 changes what feeds it, story 06 changes what CALLS it with a real value) | `IEntitlementService` / `StoredValueEntitlementService` / `EntitlementCatalog` | `api/src/Entitlements/IEntitlementService.cs`, `StoredValueEntitlementService.cs` |
| The grant store (re-keyed by story 05, read unchanged by 06/07) | `IEntitlementGrantStore` / `TableStorageEntitlementGrantStore` | `api/src/Entitlements/IEntitlementGrantStore.cs`, `TableStorageEntitlementGrantStore.cs` |
| The cloud gallery store (re-keyed by story 05, untouched otherwise) | `ICloudGalleryStore` / `TableStorageCloudGalleryStore` / `CloudTale.OwnerKey` | `api/src/CloudGallery/` |
| The room's two capture-once, ORTHOGONAL booleans (story 06 populates `SessionEntitlements` from a resolved identity; story 09 populates the REAL value of the `AdultUnlocked` bool story 06 reserves - a signed-in adult or an adult-confirmed device link - never folded into `SessionEntitlements`) | `Room.Entitlements` / `Room.CaptureEntitlements`, `Room.AdultUnlocked` / `Room.CaptureAdultUnlocked` | `api/src/Rooms/Room.cs` |
| The family-safe content gate (story 09's `AdultUnlocked`-derived effective `familySafe` value is computed at the `GameHub.StartRound` CALL SITE, before the selector runs - the selector's own filtering logic is never reimplemented; REDESIGNED 2026-07-08, see story 09) | `FamilySafeContentSelector` / `TemplateCatalog` | `api/src/Hubs/GameHub.cs` (field `_familySafe`), the child-safety feature |
| The nickname + Guardian variant fields every join/create screen already shares (story 08's preset picker sits ABOVE these, never forks them) | `PlayerIdentityFields` | `web/src/components/PlayerIdentityFields.tsx`, used by `web/src/pages/Join.tsx`, `HostSetup.tsx` |
| The live, in-memory purchaser sign-in state (story 06's `accessTokenFactory`, story 08's device check) | `usePurchaserSession` / `PurchaserSessionProvider` | `web/src/account/PurchaserSession.tsx` |
| The one SignalR connection (story 06 adds `accessTokenFactory`; story 09 extends it to also consider a stored device token) | `useGameHub` | `web/src/signalr/useGameHub.ts` |
| Room-code-style short code generation (story 09's link-code minter matches this shape, not the magic-link token's) | the join-code alphabet/generator | `api/src/Rooms/RoomRegistry.cs` (precedent only - story 09 builds its OWN small minter, scoped to link codes) |

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. This feature is a short,
mostly-serial chain (account exists before you can sign into it) with no meaningful fan-out.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 anonymous-player-forever | #67 | header-comment hardening on `api/src/Rooms/Room.cs` pointing at the already-shipped `Room.Entitlements`/`Room.CaptureEntitlements`; no new files | session-engine/02, child-safety/01 | - (do first, it is a near-zero-diff contract pass) | 1 | low |
| 02 lightweight-purchaser-account | #68 | `api/src/Accounts/Account.cs`, `IAccountStore.cs`, `AccountStore.cs`, the magic-link one-time-token issuer/verifier; `Program.cs` (one DI line) | 01, infra (Table Storage) | - | 2 | medium |
| 03 sign-in-and-restore | #69 | `api/src/Controllers/AccountsController.cs` (or similar); `web/src/pages/Account.tsx` | 02 | - | 3 | medium |
| 04 magic-link-email-delivery | #167 | new `api/src/Accounts/IEmailSender.cs` + a real sender (e.g. `AcsEmailSender.cs`) + `NoOpEmailSender.cs`; `Program.cs` (one config-presence block); inject into `AccountsController` + `OperatorLoginController` (api/src/Admin/); `appsettings.json` (`Email` section); `infra/main.bicep` (ACS Email resource + verified domain if ACS; a KV secret only for a keyed provider) + `.github/workflows/deploy.yml` (app-setting wiring) | 02, 03, sysadmin-console/01 | - | 4 | medium |
| 05 stable-account-id-spine (ADR 0003 Layer 0, foundation) | #TBD | `api/src/Accounts/Account.cs`, `AccountIdentity.cs`, `IAccountStore.cs`, `InMemoryAccountStore.cs`, `TableStorageAccountStore.cs`; `api/src/Entitlements/EntitlementGrant.cs`, `IEntitlementGrantStore.cs`, `InMemoryEntitlementGrantStore.cs`, `TableStorageEntitlementGrantStore.cs`, `StoredValueEntitlementService.cs`; `api/src/CloudGallery/CloudTale.cs`, `ICloudGalleryStore.cs`, `InMemoryCloudGalleryStore.cs`, `TableStorageCloudGalleryStore.cs`, `CloudGalleryController.cs` (ownerKey computation only) | 02, 03, 04, billing-entitlements/01, keepsake-gallery/05 (all Complete - this story re-keys their stores in place) | - | 5 | high |
| 06 purchaser-proof-at-create-room (ADR 0002 Decision F) | #TBD | `api/src/Hubs/GameHub.cs` (connect-time credential resolve + `CreateRoom`'s `purchaserIdentity` argument), `web/src/signalr/useGameHub.ts` (`accessTokenFactory`) | 05 | 07 | 6 | medium |
| 07 free-family-account | #TBD | `api/src/Controllers/AccountsController.cs` (sign-up purpose/path), `api/src/Accounts/IEmailSender.cs` (`MagicLinkPurpose.FamilySignUp`) + `AcsEmailSender.cs`/`NoOpEmailSender.cs` copy, `web/src/pages/Account.tsx` (reframe + create-account affordance) | 05 | 06 | 6 | medium |
| 08 kid-seat-presets | #TBD | new `api/src/Accounts/SeatPreset.cs` + `ISeatPresetStore.cs` + Table/in-memory implementations; `api/src/Controllers/AccountsController.cs` (preset endpoints); `web/src/pages/Account.tsx` (presets manager); `web/src/components/PlayerIdentityFields.tsx` (or a thin wrapper); `web/src/pages/Join.tsx`, `HostSetup.tsx` | 07 | - (serialize before 09 - shared footprint, see below) | 7 | medium |
| 09 family-device-link (+ the kid-device flag) | #TBD | new `api/src/Accounts/FamilyDeviceToken.cs` + `IFamilyDeviceTokenStore.cs`/implementations + the link-code minter; `api/src/Controllers/AccountsController.cs` (generate/redeem/list/revoke + the kid-device toggle); `api/src/Hubs/GameHub.cs` (extend 06's resolver; `Room.CaptureFamilySafeForced` + `StartRound` enforcement); `web/src/pages/Account.tsx` (linked-devices UI); a new small web redeem screen/route; `web/src/signalr/useGameHub.ts` (device-token fallback) | 06, 07 | - | 8 | medium |

**Concurrency per wave:** Wave 1 = 1 (01, low-effort contract pass - unblocks nothing else technically but should
land first so its guarantee is verifiable before 02 is built). Wave 2 = 1 (02, the account record + store). Wave 3
= 1 (03, the sign-in surface + endpoint, consumes 02's store). Wave 4 = 1 (04, the email-delivery transport - it
consumes 02's issuer + the request endpoints and makes 03's purchaser sign-in and sysadmin-console/01's operator
login actually completable in a deployed/Production environment). No wave has genuine parallelism through wave 4:
this is a short serial chain because each story's output is the next story's input, not a fan-out.

**ADR 0003 Layer 0 (waves 5-8):** Wave 5 = 1 (05, the foundation - re-keys three existing stores, so it is
deliberately solo and high-effort; nothing else in this arc starts before it lands). Wave 6 = {06, 07} in parallel
- disjoint footprints (06 touches only `GameHub.cs` + `useGameHub.ts`; 07 touches only `AccountsController.cs` +
the email-copy seam + `Account.tsx`'s reframe), both depending only on 05. Wave 7 = 1 (08 - depends on 07; its
Account-page and `AccountsController.cs` footprint OVERLAPS what 09 will touch, so it is sized alone in its slot
rather than paired with 09, even though 09 does not strictly block 08 shipping - see AC-06 of story 08's degraded-
but-shippable path). Wave 8 = 1 (09 - depends on 06 AND 07; deliberately serialized AFTER 08 because both stories
edit `web/src/pages/Account.tsx` and `api/src/Controllers/AccountsController.cs` - landing 09 second means it is
the one that resolves any merge overlap against 08's already-landed preset endpoints/UI).

**Cross-feature order:** story 02 (magic-link + `IAccountStore`) is upstream of `billing-entitlements/01`'s (#70)
purchaser-lookup piece (that story's AC-06) - 02 must land before #70's stored-value evaluation can resolve a real
purchaser identity, though #70's catalog-extension and grant-store work do not themselves depend on this feature.
Story 02's magic-link token issuer/verifier is also upstream of `sysadmin-console/01`'s (#135) operator login,
which reuses the SAME plumbing against a separate allowlist.

**Cross-feature order (ADR 0003, 2026-07-08):** per ADR 0003's "Cross-feature build order" table, this feature's
05 lands in the PROGRAM's Wave 1 alongside `keepsake-vault/01`, `control-plane/01`, `sysadmin-console/04`, and
`platform-devops/07-08` - all of these except platform-devops's second-environment story register services in
`Program.cs`, so land them as SEPARATE, small, serially-rebased PRs rather than batching. This feature's 06 and 07
land in the PROGRAM's Wave 2 alongside `control-plane/02` - **06 and `control-plane/02` BOTH touch
`api/src/Entitlements/` (06 changes what `CreateRoom` passes into `EvaluateForSession`; `control-plane/02` adds the
system-scope check ahead of the account-grant check inside the SAME evaluation path) - serialize those two across
features, not just within this one.** This feature's 08 and 09 land in the PROGRAM's Wave 3 alongside
`keepsake-vault/03-04` and `control-plane/03` (the knob migration - schedule it, per ADR 0003, "when the tree is
quiet," since it touches many files across every feature).

## Per-story tech notes

### 01 - Anonymous player, forever
**Approach:** a documentation-and-verification pass, not new code - and narrower than originally scoped, because
the seam it used to have to NAME is already shipped code. Extend the header comment on `Room.cs` (matching the
verbose-header-comment convention already used on `RoomRegistry.cs`) to state explicitly that the room/player
record is PII-free by design, and to point at `Room.Entitlements` (a nullable `SessionEntitlements`, set exactly
once via `Room.CaptureEntitlements` - ai-cost-gate/02, #121, PR #132) BY NAME as the one session-level entitlement
seam - it carries capability keys only, never a purchaser id, upholding ADR 0002's load-bearing invariant. Do not
invent a second placeholder field. **Exports:** the verified contract (capability-only `Room.Entitlements`, no
account field anywhere on `Room`/player) that story 02 and billing-entitlements/01 both build against. **Gotcha:**
resist the urge to add real fields in this story - AC-05 explicitly checks that `Room.cs`/`RoomRegistry.cs` do not
change again once story 02 lands (and `Room.CaptureEntitlements` already exists without touching player data).

### 02 - Lightweight purchaser account
**Approach:** new `api/src/Accounts/` folder mirroring the existing per-concern layout (`Rooms/`, `Safety/`). An
`Account` record (email address + created-at, nothing else - ADR 0002 Decision A: magic-link, no OAuth) and an
`IAccountStore`/`AccountStore` backed by Azure Table Storage, registered as a singleton in `Program.cs` next to the
existing `RoomRegistry` and `IContentSafetyFilter` registrations (same pattern: one interface, one implementation,
one DI line, verbose header comment explaining why singleton). This story also builds the magic-link one-time-token
issuer/verifier as a REUSABLE service (not inlined into the purchase flow) - `sysadmin-console/01`'s operator login
reuses the exact same plumbing against a separate allowlist; `purchaser == admin` must remain structurally
impossible. **Exports:** `IAccountStore` (create-or-get by identity) and the token issuer/verifier - consumed by
story 03 (sign-in), by billing-entitlements/01 (#70, the session-creation gate's purchaser-lookup piece), and by
`sysadmin-console/01` (#135). **Gotcha:** the account-creation UI is embedded *inside* the purchase flow (tip jar /
gated purchase), not a standalone "Sign up" page reachable from Home - keep it out of the kid play-flow per
feature.md.

### 03 - Sign-in and restore on a new device
**Approach:** a REST endpoint (new controller in `api/src/Controllers/`) that issues a fresh magic-link token
(reusing story 02's issuer/verifier, not a second implementation) and, once followed, resolves the verified email
to an existing `Account` via `IAccountStore` (no create-on-miss, no create-on-duplicate) and issues a short-lived,
purchaser-scoped credential kept entirely separate from room/player/hub state. Web side: a small purchaser-only
screen, reachable from a settings-style AppBar affordance (reuse `AppBar.tsx`, do not fork a second app-bar), styled
from theme tokens only. **Exports:** the "signed in as purchaser X" credential that billing-entitlements/05's
restore view consumes to look up entitlements, and (per ADR 0002 Decision F) the same credential a host's SignalR
connection supplies via `accessTokenFactory` so `GameHub.CreateRoom` can resolve a real purchaser identity.
**Gotcha:** this credential must never be required by, or even checked in, `GameHub.cs` or any player-facing
endpoint - keep the auth boundary strictly on the purchaser side of the app; `CreateRoom` reads the resolved
identity briefly to look up capabilities and discards it, never storing it on `Room`.

### 04 - Magic-link email delivery
**Approach:** add ONE `IEmailSender` seam and register it with the same config-presence idiom the telemetry sink /
AI client / published-tale store use in `Program.cs`: unconfigured => a no-op / dev sender that preserves today's
`Development` token echo and a neutral no-op elsewhere; configured => a real provider-backed sender (Azure
Communication Services Email or SendGrid - OPEN, see feature.md / the story). Inject it into
`AccountsController.RequestLink` and `OperatorLoginController.RequestLink` (api/src/Admin/), calling it right after
`IMagicLinkTokenService.Issue(...)`. Also promote `Accounts:TokenSigningKey` to a durable Key Vault secret so a
delivered link survives an app recycle (today it is the ephemeral per-process fallback on UAT). **Exports:** the
delivery transport that makes purchaser sign-in (03) and operator login (sysadmin-console/01) completable in a
deployed environment - nothing else consumes it. **Gotcha:** delivery must NOT change the neutral, no-enumeration
response (identical shape/timing whether an account/operator exists and whether the send succeeds); a provider
failure returns the neutral 200 and is logged without the token / link / secret. Keep the `IsDevelopment()` echo so
local walkthroughs still run with zero email config.

### 05 - Stable account id spine (ADR 0003 Layer 0, foundation)
**Approach:** re-key three already-shipped stores in place - no new seam, no new service interface. `Account` gains
a `Guid Id`; `TableStorageAccountStore`/`InMemoryAccountStore` move to a primary-row-plus-email-index shape (see the
story's Technical Notes for the exact two-row pattern); `TableStorageEntitlementGrantStore`/
`TableStorageCloudGalleryStore` (and their in-memory siblings) partition by `account.Id` instead of
`AccountIdentity.KeyFor(account.Email)`; `StoredValueEntitlementService` and `CloudGalleryController` update their
few call sites accordingly. **Exports:** the stable `AccountId` every later story in this arc (06-09) and every
Layer-1/2 ADR-0003 feature (`keepsake-vault`, `control-plane`) keys off - plus a NEW `IAccountStore.GetByIdAsync`
lookup for callers that already hold an id. **Gotcha:** this is the ONE story in the arc allowed to touch
`api/src/Entitlements/` and `api/src/CloudGallery/` alongside `api/src/Accounts/` - land it alone (wave 5) so
nothing else re-keys against a moving target.

### 06 - Purchaser proof at CreateRoom (ADR 0002 Decision F, finally wired)
**Approach:** a small connect-time credential resolution step in `GameHub` (NOT a full ASP.NET Core auth scheme -
`PurchaserCredentialService`'s credential is a Data-Protection payload, not a JWT) plus one `accessTokenFactory`
line on the web hub connection. **Exports:** the per-connection resolved-identity lookup `CreateRoom` reads (and
story 09 extends to also recognize a family-device token, rather than forking a second resolver). **Gotcha:** this
is the story every future reviewer checks against ADR 0002's load-bearing invariant - no field on `Room`/`Player`,
ever. Must serialize with `control-plane/02` (cross-feature, both touch `api/src/Entitlements/` - see the Wave Plan
note above), even though within THIS feature it can run alongside 07.

### 07 - The free family account
**Approach:** widen the EXISTING `AccountsController`'s request/verify pair with a sign-up purpose, so a "no-account"
verify outcome creates (via `IAccountStore.CreateOrGetAsync`, already idempotent) instead of only guiding to
purchase. **Exports:** the "family account with zero grants" shape every later story (08, 09, and `keepsake-vault`'s
claim flow) assumes is a normal, expected account state. **Gotcha:** must not regress the no-enumeration contract -
the request endpoint stays neutral regardless of purpose; only the VERIFY-time "no account" branch's behavior
changes (create vs guide-to-purchase), and only on the sign-up purpose.

### 08 - Kid seat presets
**Approach:** a small new preset store keyed by `AccountId` (05), a presets-manager panel on the Account page, and a
one-tap picker layered ABOVE `PlayerIdentityFields` on Join/HostSetup - never a new submit path. **Exports:** the
device-holds-a-family-credential check, built as one small shared helper so story 09 only has to extend it (add a
second credential type) rather than touch the picker component. **Gotcha:** the hard boundary (ADR 0003's
kid-profile boundary, quoted in the story) is the single most important thing a reviewer checks - a preset must be
indistinguishable, server-side, from a manually typed name.

### 09 - Family device link
**Approach:** a link-code minter + a `FamilyDeviceToken` store (opaque, revocable-by-row - deliberately NOT a Data-
Protection payload, since it must be revocable before its own TTL), a redeem/list/revoke REST surface, and an
extension of story 06's connect-time resolver to also recognize a device token. PLUS the kid-device flag: a boolean
on the SAME token row that, when set, makes `CreateRoom` capture a sibling `Room.FamilySafeForced` boolean (never
folded into `Room.Entitlements`), which `StartRound` then honors server-side regardless of the client's submitted
`familySafe` value. **Exports:** nothing further downstream within this feature - this is the last story in the
arc. **Gotcha:** two DISTINCT invariants apply here, not one - (a) the entitlement invariant (identity resolved and
discarded at `CreateRoom`, exactly like 06) and (b) the NEW content-safety invariant (a kid-flagged device can never
get an unfiltered room, enforced server-side at `StartRound`, independent of any client toggle state). Do not
conflate the two capture-once booleans (`SessionEntitlements` vs `FamilySafeForced`) into one field.

## Cross-cutting concerns

- **No account state ever gates or touches gameplay directly.** Every story in this feature stops at "does a
  purchaser exist / is one signed in" - the actual unlock decision is billing-entitlements/01's job, evaluated once
  at session-creation. This feature must not grow its own per-request checks.
- **Free play stays login-free, permanently.** Every story's ACs include an explicit "declining/ignoring sign-in
  has zero effect on free play" guard - treat any regression here as a P0, since it breaks README section 3's core
  identity promise.
- **No PII beyond the one identity field.** No name, birthdate, address, phone number, or player/nickname
  cross-reference is ever stored on the `Account` record (accounts-identity/02 AC-01/AC-03).
- **Secrets in Key Vault, never `VITE_*`.** The magic-link token-signing key (and any email-delivery provider key)
  follows the same Key Vault pattern billing-entitlements/03 uses for Stripe keys - do not introduce a second
  secrets convention.
- **`purchaser == admin` must be impossible.** Story 02's token issuer/verifier is reused by
  `sysadmin-console/01`'s operator login, but admin authorization is a SEPARATE allowlist check, never inferred
  from a valid purchaser sign-in - guard this the same way ADR 0002 and `sysadmin-console`'s implementation.md do.
- **No i18n** (plain strings). **No em dashes.** Big tap targets and the stone-tablet/Guardian visual language
  extend to the purchaser-only screens too, even though they are adult-facing - the whole app is one visual
  language, not two.
- **ADR 0003 Layer 0 (stories 05-09) - the invariant is unchanged, only who may hold an account widens.** Every
  story in this arc is reviewed against the SAME load-bearing invariant ADR 0002 established: entitlement identity
  is resolved to capabilities at `CreateRoom` and discarded at that boundary; only `SessionEntitlements` (and, as of
  story 09, the sibling `FamilySafeForced` boolean) ever lands on `Room` - never an `AccountId`, email, device-token
  id, or preset id.
- **`Program.cs` is the systemic hotspot for this arc, as it is for every ADR 0003 Wave-1/2 story across the whole
  program.** Story 05 touches no NEW registration (same interfaces, re-keyed implementations); stories 08 and 09 DO
  add new store registrations (`ISeatPresetStore`, `IFamilyDeviceTokenStore`) - land those as small, separately
  rebased PRs rather than batching with any concurrent feature's `Program.cs` edit.
- **The kid-profile boundary (story 08) and the kid-device flag (story 09) are both content/UX conveniences, never
  identity.** No story in this arc may add a field to `Room`/`Player` beyond the two capture-once booleans already
  named above - if a future idea needs one, that is a new ADR (ADR 0003's own words), not a slide in one of these
  stories.
- **Per-device capability scoping stays parked (ADR 0003, recorded in feature.md's Parked section).** A linked
  device (story 09) always resolves the WHOLE family grant set - never a subset chosen per device. Only the
  family-safe content state is ever device-scoped.
