<!--
  Implementation plan for the accounts-identity feature. Bridges feature.md + stories to orchestration.
  Refreshed 2026-07-03 against shipped reality: ADR 0002 Decision A resolved the identity provider to
  magic-link email (no OAuth), and ai-cost-gate/02 (#121, PR #132) already ships the entitlement-capture
  seam (Room.Entitlements) story 01 used to have to NAME as a placeholder. Use hyphens/colons/parentheses,
  never em dashes.
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

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. This feature is a short,
mostly-serial chain (account exists before you can sign into it) with no meaningful fan-out.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 anonymous-player-forever | #67 | header-comment hardening on `api/src/Rooms/Room.cs` pointing at the already-shipped `Room.Entitlements`/`Room.CaptureEntitlements`; no new files | session-engine/02, child-safety/01 | - (do first, it is a near-zero-diff contract pass) | 1 | low |
| 02 lightweight-purchaser-account | #68 | `api/src/Accounts/Account.cs`, `IAccountStore.cs`, `AccountStore.cs`, the magic-link one-time-token issuer/verifier; `Program.cs` (one DI line) | 01, infra (Table Storage) | - | 2 | medium |
| 03 sign-in-and-restore | #69 | `api/src/Controllers/AccountsController.cs` (or similar); `web/src/pages/Account.tsx` | 02 | - | 3 | medium |

**Concurrency per wave:** Wave 1 = 1 (01, low-effort contract pass - unblocks nothing else technically but should
land first so its guarantee is verifiable before 02 is built). Wave 2 = 1 (02, the account record + store). Wave 3
= 1 (03, the sign-in surface + endpoint, consumes 02's store). No wave has genuine parallelism: this is a short
serial chain because each story's output is the next story's input, not a fan-out.

**Cross-feature order:** story 02 (magic-link + `IAccountStore`) is upstream of `billing-entitlements/01`'s (#70)
purchaser-lookup piece (that story's AC-06) - 02 must land before #70's stored-value evaluation can resolve a real
purchaser identity, though #70's catalog-extension and grant-store work do not themselves depend on this feature.
Story 02's magic-link token issuer/verifier is also upstream of `sysadmin-console/01`'s (#135) operator login,
which reuses the SAME plumbing against a separate allowlist.

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
