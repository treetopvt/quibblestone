# Story: Operator login and admin boundary (separate surface)

**Feature:** Sys-Admin Console  ·  **Status:** Complete  ·  **Issue:** #135

## Context
This is the foundation of the whole feature (feature.md's Candidate stories table, ADR 0002
Decision B): before anything can be granted, revoked, or moderated, there has to be a place for
an operator to sign in that a purchaser can never reach, and a signed-in purchaser must never be
mistaken for an operator. ADR 0002 Decision A resolved the mechanism - the same magic-link
one-time-token issue/verify plumbing the purchaser account uses (`accounts-identity/02`), but
checked against a *separate* operator allowlist held in config / Key Vault, resolved at verify
time. `purchaser == admin` must be impossible: that equivalence is the exact bug this story exists
to prevent. This story also stands up the back office's own bundle/route tree - never reachable
from, or bundled with, the kid-facing PWA (ADR 0002 Decision B, option A) - because the admin
surface will shortly carry purchaser-email lookups and moderation actions, and the kid app's blast
radius must stay minimal (CLAUDE.md section 5). See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given an operator (an email on the operator allowlist) requests a sign-in link, when
      they submit their email to the back office's login screen, then a one-time magic link is
      issued and emailed - using the same token issue/verify plumbing as the purchaser flow (or,
      per Technical Notes, a thin contract-compatible issuer this story builds if that plumbing
      has not landed yet) - and following it signs them into the back office.
- [x] AC-02: Given an email that is NOT on the operator allowlist, when a magic-link sign-in for
      that email is attempted (issued or followed), then it never resolves to an operator session
      - the allowlist check happens at verify time, not at token-issue time, so no operator scope
      is ever granted based on possessing a valid link alone.
- [x] AC-03 (the load-bearing guard): Given a purchaser is signed in to their own purchaser account
      (accounts-identity/02, a *different* session/credential), when that purchaser's session token
      is presented to any admin endpoint, then the request is rejected - admin endpoints check
      membership in the operator allowlist, never "is this caller signed in as *some* account."
      `purchaser == admin` must be structurally impossible, not merely undocumented.
- [x] AC-04: Given the back office, then it lives in a separate bundle/route tree (its own entry
      point, e.g. a distinct build output or route prefix never linked from the kid PWA's nav,
      deep links, or service worker) - there is no path from Join, Lobby, FillBlank, Reveal, or Home
      that reaches it, and no shared JS chunk ships admin-only code to the kid app.
- [x] AC-05: Given the operator allowlist, then it is held in configuration backed by Azure Key
      Vault (never a `VITE_*` variable, never committed to source, never inferred from any player-
      or purchaser-facing data) - adding or removing an operator is a config change, not a code
      change.
- [x] AC-06: Given an unauthenticated visitor reaches any back-office route, then they see only the
      login screen - no admin data, no room/player data, and no purchaser data of any kind is
      rendered or fetched before an operator session is established.
- [x] AC-07 (no PII beyond the operator's own email): Given the operator login flow, then it
      collects nothing about the operator beyond the email used to issue and verify the magic
      link (no name, no player/session cross-reference) - consistent with README section 6's
      minimal-data posture, applied here to the one adult-facing account this feature adds.

## Out of Scope
- Any actual admin *capability* (grant/revoke, report/takedown) - this story is the login +
  boundary only; stories 02 and 03 are its first consumers.
- Multi-operator roles or a role hierarchy (owner vs. moderator) - Parked in feature.md; alpha has
  one operator, checked by flat allowlist membership.
- An audit trail of operator sign-ins or actions - Parked in feature.md (toy, not a system of
  record).
- Building or changing the purchaser-facing magic-link flow itself - that is
  `accounts-identity/02`; this story only reuses its token plumbing (or a thin, contract-compatible
  stand-in if that plumbing is not yet built - see Technical Notes) against a different allowlist.
- Rate-limiting or abuse-hardening of the login-link request endpoint beyond a sane, generous cap -
  a full anti-abuse pass is not this story's job; note it in Technical Notes if it comes up.

## Technical Notes
- **Dependency reality: `accounts-identity/02`'s magic-link plumbing is currently unbuilt** (ADR
  0002 "State of the tree" - Status: Not Started, no `api/src/Accounts/` folder yet). Mirror
  `ai-cost-gate/02`'s handling of an unbuilt seam: either (a) serialize this story after
  `accounts-identity/02` lands, or (b) build a minimal, contract-compatible one-time-token
  issuer/verifier now (issue a token for an email, verify it once, expire it) that
  `accounts-identity/02` later subsumes - the public shape (issue-by-email, verify-once,
  short-lived) stays stable either way so this story's operator login never has to change when the
  purchaser flow's real plumbing lands. Prefer (a) if `accounts-identity/02` is scheduled close by;
  otherwise (b) keeps this feature unblocked. Whichever is chosen, the token issuer/verifier is a
  small, shared, reusable service (`api/src/Accounts/` or a new `api/src/Admin/` companion) - not
  two independent implementations of "email a one-time link."
  *(2026-07-07: stale - #68 (accounts-identity/02, PR #147) has since shipped, and the built story
  took option (a): `OperatorLoginController` reuses the real `IMagicLinkTokenService`, no stand-in
  issuer. The entitlement grant store #70 (PR #152) has shipped too.)*
- New `api/src/Admin/` folder (mirrors the existing per-concern layout: `Rooms/`, `Safety/`,
  `Accounts/`, `PublishedTales/`). An `IOperatorAllowlist` (reads the allowlist from configuration
  backed by Key Vault - see `infra/main.bicep`'s `keyVault` resource, the same pattern
  `billing-entitlements/03`'s Stripe keys use) and an `OperatorAuthenticationHandler`/middleware
  that resolves a verified token to an email, checks it against `IOperatorAllowlist`, and issues a
  short-lived operator session (a signed cookie or bearer token, kept entirely separate from any
  purchaser or player credential - AC-03). Admin controllers require this scope via a dedicated
  authorization policy/attribute (e.g. `[Authorize(Policy = "Operator")]`), never mere
  `[Authorize]` (which would only prove "signed in as something").
- **Separate surface (AC-04):** the simplest option for a solo build is a distinct Vite entry
  point/route prefix (e.g. `web/admin/` as its own small app, or a separate build target) that is
  never imported by, linked from, or code-split into the kid PWA's bundle - verify with a bundle
  audit, not just "no visible link." A separate subdomain/path served by the same ASP.NET Core app
  is acceptable; the requirement is bundle and route isolation, not a second Azure resource.
  Reuses `web/src/theme.ts` for styling (same visual language, different bundle) and FontAwesome
  icons per CLAUDE.md section 4 - it is a separate surface, not a separate design system.
- Admin session token: same posture as `accounts-identity/03`'s purchaser credential notes - never
  required by, or checked in, `GameHub.cs` or any player-facing endpoint; keep it fully separate
  from `api/src/Rooms/` state.
- The allowlist itself: a simple list of emails (or a hash of each) in App Service configuration
  sourced from Key Vault - a toy-appropriate mechanism (CLAUDE.md section 10), not an RBAC table.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Admin/OperatorLoginTests.cs: issuing then following a magic link for an allowlisted email establishes an operator session.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Admin/OperatorLoginTests.cs + OperatorAllowlistTests.cs: a valid, followed link for a non-allowlisted email never yields an operator-scoped session (allowlist checked at verify time, not issue time).` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Admin/OperatorAuthorizationTests.cs: a genuine purchaser-purpose DataProtection credential presented to an Operator-policy endpoint is rejected (401), while an allowlisted operator credential returns 200 (WebApplicationFactory boots the real app).` |
| AC-04 | `manual + static: bundle/route audit - the admin bundle (web/admin.html -> web/src/admin/) builds as its own chunk with no import edge from web/src kid-app code and no nav/deep-link path reaches it (confirmed at build time; re-confirm in the browser at verification).` |
| AC-05 | `manual: config/secret audit - the allowlist is sourced from Key Vault-backed configuration (Operator:AllowedEmails), fail-closed empty in appsettings.json, never a VITE_* var or a committed literal.` |
| AC-06 | `manual: load each back-office route unauthenticated - confirm only the login screen renders and no admin/purchaser data is fetched.` |
| AC-07 | `manual: code read of the operator login path - confirm no field beyond the operator's email is collected or stored.` |

## Dependencies
- `accounts-identity/02` (#68) - the magic-link one-time-token issue/verify plumbing this story
  reuses (or the thin contract-compatible stand-in per Technical Notes, if #68 has not landed).
- infra (Azure Key Vault, already provisioned per README section 9) - holds the operator allowlist
  and any session-signing key.
- child-safety - no direct dependency, but the back office must never become a path that
  deanonymizes a player (see implementation.md Cross-cutting concerns).
